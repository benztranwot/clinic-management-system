using System.Security.Claims;
using HealthcareApp.Api.Authorization;
using HealthcareApp.Api.Data;
using HealthcareApp.Api.Dtos;
using HealthcareApp.Api.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HealthcareApp.Api.Endpoints;

public static class ReferralEndpoints
{
    public static void MapReferralEndpoints(this IEndpointRouteBuilder app)
    {
        // Require permission to list referrals.
        app.MapGet("/api/referrals", GetReferralsAsync).RequireAuthorization("ReadReferrals");

        // Staff can list referrals for a clinic where they have a membership.
        app.MapGet("/api/clinics/{clinicPublicId:guid}/referrals", GetClinicReferralsAsync)
            .RequireAuthorization();

        // Require permission to create a referral.
        app.MapPost("/api/referrals", CreateReferralAsync).RequireAuthorization("ManageReferrals");

        // Require permission to view a referral.
        app.MapGet("/api/referrals/{publicId:guid}", GetReferralByIdAsync)
            .RequireAuthorization("ReadReferrals");

        // List providers who offer the exact service on this referral.
        app.MapGet("/api/referrals/{publicId:guid}/eligible-providers", GetEligibleProvidersAsync)
            .RequireAuthorization("ReadReferrals", "ReadProviders", "ReadProviderServices");

        // List receiving providers for the selected service when creating a referral.
        app.MapGet("/api/services/{servicePublicId:guid}/eligible-providers", GetServiceEligibleProvidersAsync)
            .RequireAuthorization("ManageReferrals", "ReadProviders", "ReadProviderServices");

        // Reassign the receiving provider before booking.
        app.MapPut("/api/referrals/{publicId:guid}/provider", AssignReferralProviderAsync)
            .RequireAuthorization("ManageReferrals");

        // Require permission to review or close a referral.
        app.MapPut("/api/referrals/{publicId:guid}/status", UpdateReferralStatusAsync)
            .RequireAuthorization("ManageReferrals");
    }

    private static Task<IResult> GetReferralsAsync(
        HealthcareDbContext db,
        CancellationToken cancellationToken
    ) => ListReferralsAsync(db, db.Clinics, cancellationToken);

    private static async Task<IResult> GetClinicReferralsAsync(
        Guid clinicPublicId,
        ClaimsPrincipal user,
        HttpContext http,
        HealthcareDbContext db,
        CancellationToken cancellationToken
    )
    {
        var identity = AccountIdentity.FromPrincipal(user);
        if (identity is null)
        {
            return Results.Challenge();
        }

        var allowedClinics = identity.QueryStaffClinics(db).Where(clinic => clinic.PublicId == clinicPublicId);

        if (!await allowedClinics.AnyAsync(cancellationToken))
        {
            return Results.Forbid();
        }

        http.Response.Headers.CacheControl = "no-store";

        // Keep the access filter in the data query as well.
        return await ListReferralsAsync(db, allowedClinics, cancellationToken);
    }

    private static async Task<IResult> ListReferralsAsync(
        HealthcareDbContext db,
        IQueryable<Clinic> clinics,
        CancellationToken cancellationToken
    )
    {
        // Join each referral to its patient and service, with newest referrals first.
        var referrals = await (
            from referral in db.Referrals
            join patient in db.Patients on referral.PatientId equals patient.Id
            join service in db.ClinicalServices on referral.ClinicalServiceId equals service.Id
            join clinic in clinics on service.ClinicId equals clinic.Id
            orderby referral.CreatedAtUtc descending
            select new
            {
                referral.PublicId,
                PatientFirstName = patient.FirstName,
                PatientLastName = patient.LastName,
                ServiceName = service.Name,
                ClinicName = clinic.Name,
                referral.Status,
                HasConfirmedAppointment = db.Appointments.Any(appointment =>
                    appointment.ReferralId == referral.Id && appointment.Status == AppointmentStatus.Confirmed),
                referral.CreatedAtUtc,
            }
        ).ToListAsync(cancellationToken);

        // Return readable status names in the list.
        return Results.Ok(
            referrals.Select(referral => new
            {
                referral.PublicId,
                referral.PatientFirstName,
                referral.PatientLastName,
                referral.ServiceName,
                referral.ClinicName,
                Status = referral.Status.ToString(),
                Stage = GetStage(referral.Status, referral.HasConfirmedAppointment),
                referral.CreatedAtUtc,
            })
        );
    }

    private static async Task<IResult> CreateReferralAsync(
        CreateReferralRequest request,
        HealthcareDbContext db,
        CancellationToken cancellationToken
    )
    {
        // Validate the patient, service, receiving provider, and text.
        var referringProviderName = request.ReferringProviderName?.Trim();
        var reason = request.Reason?.Trim();

        if (request.PatientPublicId == Guid.Empty || request.ClinicalServicePublicId == Guid.Empty)
        {
            return Results.BadRequest(new { message = "Select a patient and a clinical service." });
        }

        if (request.AssignedProviderPublicId == Guid.Empty)
        {
            return Results.BadRequest(new { message = "Select a receiving provider." });
        }

        if (string.IsNullOrEmpty(referringProviderName) || referringProviderName.Length > 200)
        {
            return Results.BadRequest(
                new { message = "Referring provider name must be between 1 and 200 characters." }
            );
        }

        if (string.IsNullOrEmpty(reason) || reason.Length > 2000)
        {
            return Results.BadRequest(
                new { message = "Referral reason must be between 1 and 2000 characters." }
            );
        }

        // Resolve the patient public ID to the internal ID used by the foreign key.
        var patient = await db
            .Patients.Where(p => p.PublicId == request.PatientPublicId)
            .Select(p => new { p.Id })
            .SingleOrDefaultAsync(cancellationToken);

        if (patient is null)
        {
            return Results.NotFound(new { message = "Patient was not found." });
        }

        // Resolve the clinical service public ID to its internal ID.
        var service = await db
            .ClinicalServices.Where(s => s.PublicId == request.ClinicalServicePublicId)
            .Select(s => new { s.Id })
            .SingleOrDefaultAsync(cancellationToken);

        if (service is null)
        {
            return Results.NotFound(new { message = "Clinical service was not found." });
        }

        var providerId = await FindEligibleProviders(db, service.Id)
            .Where(provider => provider.PublicId == request.AssignedProviderPublicId)
            .Select(provider => (int?)provider.Id)
            .SingleOrDefaultAsync(cancellationToken);

        if (providerId is null)
        {
            return Results.BadRequest(new { message = "This provider does not offer the requested service." });
        }
        var referral = new Referral
        {
            PatientId = patient.Id,
            ClinicalServiceId = service.Id,
            AssignedProviderId = providerId.Value,
            ReferringProviderName = referringProviderName,
            Reason = reason,
            Status = ReferralStatus.PendingReview,
        };

        db.Referrals.Add(referral);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is SqlException { Number: 2601 or 2627 } sql
                && sql.Message.Contains(
                    "IX_Referrals_PatientId_ClinicalServiceId",
                    StringComparison.Ordinal
                )
            )
        {
            // Return 409 when the patient already has an active referral for this service.
            return Results.Conflict(
                new { message = "This patient already has an active referral for this service." }
            );
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is SqlException { Number: 547 })
        {
            // Handle a patient, service, or eligibility link that changed before saving.
            return Results.Conflict(
                new { message = "The patient, service, or provider is no longer available for referral." }
            );
        }

        // Return 201 Created with the new referral's URL in the Location header.
        return Results.Created(
            $"/api/referrals/{referral.PublicId}",
            new
            {
                referral.PublicId,
                Status = referral.Status.ToString(),
                Stage = GetStage(referral.Status, hasConfirmedAppointment: false),
                AssignedProviderPublicId = request.AssignedProviderPublicId,
                referral.CreatedAtUtc,
            }
        );
    }

    private static async Task<IResult> GetReferralByIdAsync(
        Guid publicId,
        ClaimsPrincipal user,
        HealthcareDbContext db,
        CancellationToken cancellationToken
    )
    {
        var identity = AccountIdentity.FromPrincipal(user);
        var reviewable = QueryReviewableReferrals(db, identity);
        var schedulingClinics = identity?.QuerySchedulingClinics(db) ?? db.Clinics.Where(_ => false);

        // Join the referral to its patient and service to get their public IDs.
        var referral = await (
            from r in db.Referrals
            join patient in db.Patients on r.PatientId equals patient.Id
            join service in db.ClinicalServices on r.ClinicalServiceId equals service.Id
            join clinic in db.Clinics on service.ClinicId equals clinic.Id
            join provider in db.Providers on r.AssignedProviderId equals provider.Id
            where r.PublicId == publicId
            select new
            {
                r.PublicId,
                PatientPublicId = patient.PublicId,
                PatientFirstName = patient.FirstName,
                PatientLastName = patient.LastName,
                ClinicalServicePublicId = service.PublicId,
                ClinicPublicId = clinic.PublicId,
                ServiceName = service.Name,
                ClinicName = clinic.Name,

                AssignedProviderPublicId = provider.PublicId,

                r.ReferringProviderName,
                r.Reason,
                r.Status,
                HasConfirmedAppointment = db.Appointments.Any(appointment =>
                    appointment.ReferralId == r.Id && appointment.Status == AppointmentStatus.Confirmed),
                CanReview = reviewable.Any(item => item.Id == r.Id),
                CanReassign = schedulingClinics.Any(item => item.Id == clinic.Id),
                r.MissingInformationNote,
                r.CreatedAtUtc,
            }
        ).SingleOrDefaultAsync(cancellationToken);

        // Return 404 when the requested referral does not exist.
        if (referral is null)
        {
            return Results.NotFound();
        }

        // Return the status as a readable name such as PendingReview.
        return Results.Ok(
            new
            {
                referral.PublicId,
                referral.PatientPublicId,
                referral.PatientFirstName,
                referral.PatientLastName,
                referral.ClinicalServicePublicId,
                referral.ClinicPublicId,
                referral.ServiceName,
                referral.ClinicName,
                referral.AssignedProviderPublicId,
                referral.ReferringProviderName,
                referral.Reason,
                Status = referral.Status.ToString(),
                Stage = GetStage(referral.Status, referral.HasConfirmedAppointment),
                referral.CanReview,
                referral.CanReassign,
                referral.MissingInformationNote,
                referral.CreatedAtUtc,
            }
        );
    }

    private static async Task<IResult> GetEligibleProvidersAsync(
        Guid publicId,
        HealthcareDbContext db,
        CancellationToken cancellationToken
    )
    {
        var clinicalServiceId = await db.Referrals
            .Where(referral => referral.PublicId == publicId)
            .Select(referral => (int?)referral.ClinicalServiceId)
            .SingleOrDefaultAsync(cancellationToken);

        if (clinicalServiceId is null)
        {
            return Results.NotFound();
        }

        return await ListEligibleProvidersAsync(db, clinicalServiceId.Value, cancellationToken);
    }

    private static async Task<IResult> GetServiceEligibleProvidersAsync(
        Guid servicePublicId,
        HealthcareDbContext db,
        CancellationToken cancellationToken
    )
    {
        var serviceId = await db.ClinicalServices
            .Where(service => service.PublicId == servicePublicId)
            .Select(service => (int?)service.Id)
            .SingleOrDefaultAsync(cancellationToken);

        return serviceId is int id
            ? await ListEligibleProvidersAsync(db, id, cancellationToken)
            : Results.NotFound();
    }

    private static IQueryable<Provider> FindEligibleProviders(HealthcareDbContext db, int serviceId) =>
        from link in db.ProviderServices
        join provider in db.Providers on link.ProviderId equals provider.Id
        where link.ClinicalServiceId == serviceId
        select provider;

    private static async Task<IResult> ListEligibleProvidersAsync(
        HealthcareDbContext db,
        int serviceId,
        CancellationToken cancellationToken
    )
    {
        var providers = await FindEligibleProviders(db, serviceId)
            .OrderBy(provider => provider.LastName)
            .ThenBy(provider => provider.FirstName)
            .Select(provider => new { provider.PublicId, provider.FirstName, provider.LastName })
            .ToListAsync(cancellationToken);

        return Results.Ok(providers);
    }

    private static IQueryable<Referral> QueryReviewableReferrals(
        HealthcareDbContext db,
        AccountIdentity? identity
    )
    {
        if (identity is null)
        {
            return db.Referrals.Where(_ => false);
        }

        var administrators = identity.QueryAccounts(db).Where(account => account.IsAdministrator);
        var ownProviders = identity.QueryOwnProviders(db);
        return db.Referrals.Where(referral =>
            administrators.Any() || ownProviders.Any(provider => provider.Id == referral.AssignedProviderId)
        );
    }

    private static async Task<IResult> AssignReferralProviderAsync(
        Guid publicId,
        AssignReferralProviderRequest request,
        ClaimsPrincipal user,
        HealthcareDbContext db,
        CancellationToken cancellationToken
    )
    {
        if (request.ProviderPublicId == Guid.Empty)
        {
            return Results.BadRequest(new { message = "Select a provider." });
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await TransactionLock.TryAcquireReferralAsync(db, publicId, cancellationToken))
        {
            return Results.Conflict(new { message = "The referral is being updated. Try again." });
        }

        var identity = AccountIdentity.FromPrincipal(user);
        if (identity is null)
        {
            return Results.Challenge();
        }

        var schedulingClinics = identity.QuerySchedulingClinics(db);
        var referral = await db
            .Referrals.Where(r => r.PublicId == publicId)
            .Select(r => new
            {
                r.Id,
                r.ClinicalServiceId,
                r.AssignedProviderId,
                r.Status,
                CanReassign = db.ClinicalServices.Any(service => service.Id == r.ClinicalServiceId
                    && schedulingClinics.Any(clinic => clinic.Id == service.ClinicId)),
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (referral is null)
        {
            return Results.NotFound(new { message = "Referral was not found." });
        }

        if (!referral.CanReassign)
        {
            return Results.Forbid();
        }

        if (referral.Status == ReferralStatus.Closed)
        {
            return Results.Conflict(new { message = "This referral is closed." });
        }

        if (await db.Appointments.AnyAsync(appointment =>
                appointment.ReferralId == referral.Id
                && appointment.Status != AppointmentStatus.Cancelled,
                cancellationToken))
        {
            return Results.Conflict(new { message = "Change the provider through the appointment booking." });
        }

        // Find the provider's internal ID using the public ID from the request.
        var provider = await db
            .Providers.Where(p => p.PublicId == request.ProviderPublicId)
            .Select(p => new { p.Id })
            .SingleOrDefaultAsync(cancellationToken);

        if (provider is null)
        {
            return Results.NotFound(new { message = "Provider was not found." });
        }

        // Check that the provider offers the exact service requested by the referral.
        var offersService = await db.ProviderServices.AnyAsync(
            link =>
                link.ProviderId == provider.Id
                && link.ClinicalServiceId == referral.ClinicalServiceId,
            cancellationToken
        );

        if (!offersService)
        {
            return Results.BadRequest(
                new { message = "This provider does not offer the requested service." }
            );
        }

        if (provider.Id == referral.AssignedProviderId)
        {
            return Results.Ok(new { publicId, AssignedProviderPublicId = request.ProviderPublicId });
        }

        try
        {
            // A new receiving provider reviews the referral before it can be booked.
            var updatedCount = await db
                .Referrals.Where(r => r.Id == referral.Id && r.Status != ReferralStatus.Closed)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(r => r.AssignedProviderId, provider.Id)
                        .SetProperty(r => r.Status, r => r.Status == ReferralStatus.ReadyToSchedule
                            ? ReferralStatus.PendingReview : r.Status),
                    cancellationToken
                );

            if (updatedCount == 0)
            {
                return Results.Conflict(
                    new { message = "The referral changed before assignment." }
                );
            }
        }
        catch (SqlException exception) when (exception.Number == 547)
        {
            // Handle a service link that was removed between the eligibility check and the update.
            return Results.Conflict(
                new { message = "The provider is no longer linked to the requested service." }
            );
        }

        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new { publicId, AssignedProviderPublicId = request.ProviderPublicId });
    }

    private static async Task<IResult> UpdateReferralStatusAsync(
        Guid publicId,
        UpdateReferralStatusRequest request,
        ClaimsPrincipal user,
        HealthcareDbContext db,
        CancellationToken cancellationToken
    )
    {
        // Accept the same status names returned by the referral GET routes.
        ReferralStatus? requestedStatus = request.Status?.Trim() switch
        {
            nameof(ReferralStatus.PendingReview) => ReferralStatus.PendingReview,
            nameof(ReferralStatus.MissingInformation) => ReferralStatus.MissingInformation,
            nameof(ReferralStatus.ReadyToSchedule) => ReferralStatus.ReadyToSchedule,
            nameof(ReferralStatus.Closed) => ReferralStatus.Closed,
            _ => null,
        };

        if (requestedStatus is null)
        {
            return Results.BadRequest(new { message = "Select a valid referral status." });
        }

        var status = requestedStatus.Value;

        // Keep a note only while the referral is missing information.
        var note =
            status == ReferralStatus.MissingInformation
                ? request.MissingInformationNote?.Trim()
                : null;

        if (
            status == ReferralStatus.MissingInformation
            && (string.IsNullOrEmpty(note) || note.Length > 1000)
        )
        {
            return Results.BadRequest(
                new { message = "Enter a missing information note between 1 and 1000 characters." }
            );
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await TransactionLock.TryAcquireReferralAsync(db, publicId, cancellationToken))
        {
            return Results.Conflict(new { message = "The referral is being updated. Try again." });
        }

        var reviewable = QueryReviewableReferrals(db, AccountIdentity.FromPrincipal(user));

        // Read the current status and assignment while holding the referral lock.
        var referral = await db
            .Referrals.Where(r => r.PublicId == publicId)
            .Select(r => new
            {
                r.Id,
                r.Status,
                r.MissingInformationNote,
                CanReview = reviewable.Any(item => item.Id == r.Id),
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (referral is null)
        {
            return Results.NotFound(new { message = "Referral was not found." });
        }

        if (!referral.CanReview)
        {
            return Results.Forbid();
        }

        if (referral.Status == ReferralStatus.Closed)
        {
            if (status == ReferralStatus.Closed)
            {
                return Results.Ok(
                    new
                    {
                        publicId,
                        Status = referral.Status.ToString(),
                        Stage = GetStage(referral.Status, hasConfirmedAppointment: false),
                        referral.MissingInformationNote,
                    }
                );
            }

            return Results.Conflict(new { message = "This referral is closed." });
        }

        if (await db.Appointments.AnyAsync(appointment =>
                appointment.ReferralId == referral.Id
                && appointment.Status == AppointmentStatus.Confirmed,
                cancellationToken))
        {
            return Results.Conflict(new { message = "Resolve the confirmed appointment before changing this referral." });
        }

        var updatedCount = await db
            .Referrals.Where(r => r.Id == referral.Id && r.Status == referral.Status)
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(r => r.Status, status)
                        .SetProperty(r => r.MissingInformationNote, note),
                cancellationToken
            );

        if (updatedCount == 0)
        {
            return Results.Conflict(
                new { message = "The referral changed. Reload it and try again." }
            );
        }

        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(
            new
            {
                publicId,
                Status = status.ToString(),
                Stage = GetStage(status, hasConfirmedAppointment: false),
                MissingInformationNote = note,
            }
        );
    }

    internal static string GetStage(ReferralStatus status, bool hasConfirmedAppointment) =>
        status == ReferralStatus.Closed ? "Closed"
        : hasConfirmedAppointment ? "Scheduled"
        : status == ReferralStatus.ReadyToSchedule ? "ReadyToSchedule"
        : "PendingReview";
}
