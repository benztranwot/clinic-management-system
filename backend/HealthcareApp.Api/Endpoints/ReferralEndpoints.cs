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

        // Require permission to create a referral.
        app.MapPost("/api/referrals", CreateReferralAsync).RequireAuthorization("ManageReferrals");

        // Require permission to view a referral.
        app.MapGet("/api/referrals/{publicId:guid}", GetReferralByIdAsync)
            .RequireAuthorization("ReadReferrals");

        // List providers who offer the exact service on this referral.
        app.MapGet("/api/referrals/{publicId:guid}/eligible-providers", GetEligibleProvidersAsync)
            .RequireAuthorization("ReadReferrals", "ReadProviders", "ReadProviderServices");

        // Require permission to assign a provider.
        app.MapPut("/api/referrals/{publicId:guid}/provider", AssignReferralProviderAsync)
            .RequireAuthorization("ManageReferrals");

        // Require permission to review or close a referral.
        app.MapPut("/api/referrals/{publicId:guid}/status", UpdateReferralStatusAsync)
            .RequireAuthorization("ManageReferrals");
    }

    private static async Task<IResult> GetReferralsAsync(
        HealthcareDbContext db,
        CancellationToken cancellationToken
    )
    {
        // Join each referral to its patient and service, with newest referrals first.
        var referrals = await (
            from referral in db.Referrals
            join patient in db.Patients on referral.PatientId equals patient.Id
            join service in db.ClinicalServices on referral.ClinicalServiceId equals service.Id
            join clinic in db.Clinics on service.ClinicId equals clinic.Id
            orderby referral.CreatedAtUtc descending
            select new
            {
                referral.PublicId,
                PatientFirstName = patient.FirstName,
                PatientLastName = patient.LastName,
                ServiceName = service.Name,
                ClinicName = clinic.Name,
                referral.Status,
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
        // Check that the request includes a patient, a service, and valid text.
        var referringProviderName = request.ReferringProviderName?.Trim();
        var reason = request.Reason?.Trim();

        if (request.PatientPublicId == Guid.Empty || request.ClinicalServicePublicId == Guid.Empty)
        {
            return Results.BadRequest(new { message = "Select a patient and a clinical service." });
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

        // Create a pending referral with the provider assignment left empty.
        var referral = new Referral
        {
            PatientId = patient.Id,
            ClinicalServiceId = service.Id,
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
            // Handle a patient or service that became unavailable before saving.
            return Results.Conflict(
                new { message = "The patient or service is no longer available for referral." }
            );
        }

        // Return 201 Created with the new referral's URL in the Location header.
        return Results.Created(
            $"/api/referrals/{referral.PublicId}",
            new
            {
                referral.PublicId,
                Status = referral.Status.ToString(),
                referral.CreatedAtUtc,
            }
        );
    }

    private static async Task<IResult> GetReferralByIdAsync(
        Guid publicId,
        HealthcareDbContext db,
        CancellationToken cancellationToken
    )
    {
        // Join the referral to its patient and service to get their public IDs.
        var referral = await (
            from r in db.Referrals
            join patient in db.Patients on r.PatientId equals patient.Id
            join service in db.ClinicalServices on r.ClinicalServiceId equals service.Id
            join clinic in db.Clinics on service.ClinicId equals clinic.Id
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

                // Look up the assigned provider's public ID, if there is one.
                AssignedProviderPublicId = db
                    .Providers.Where(p => p.Id == r.AssignedProviderId)
                    .Select(p => (Guid?)p.PublicId)
                    .FirstOrDefault(),

                r.ReferringProviderName,
                r.Reason,
                r.Status,
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

        var providers = await (
            from link in db.ProviderServices
            join provider in db.Providers on link.ProviderId equals provider.Id
            where link.ClinicalServiceId == clinicalServiceId.Value
            orderby provider.LastName, provider.FirstName
            select new { provider.PublicId, provider.FirstName, provider.LastName }
        ).ToListAsync(cancellationToken);

        return Results.Ok(providers);
    }

    private static async Task<IResult> AssignReferralProviderAsync(
        Guid publicId,
        AssignReferralProviderRequest request,
        HealthcareDbContext db,
        CancellationToken cancellationToken
    )
    {
        if (request.ProviderPublicId == Guid.Empty)
        {
            return Results.BadRequest(new { message = "Select a provider." });
        }

        // Find the referral and the service it requests.
        var referral = await db
            .Referrals.Where(r => r.PublicId == publicId)
            .Select(r => new
            {
                r.Id,
                r.ClinicalServiceId,
                r.Status,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (referral is null)
        {
            return Results.NotFound(new { message = "Referral was not found." });
        }

        if (referral.Status == ReferralStatus.Closed)
        {
            return Results.Conflict(new { message = "This referral is closed." });
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

        try
        {
            // Update the assignment only if the referral is still open.
            var updatedCount = await db
                .Referrals.Where(r => r.Id == referral.Id && r.Status != ReferralStatus.Closed)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(r => r.AssignedProviderId, (int?)provider.Id),
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

        return Results.Ok(new { publicId, AssignedProviderPublicId = request.ProviderPublicId });
    }

    private static async Task<IResult> UpdateReferralStatusAsync(
        Guid publicId,
        UpdateReferralStatusRequest request,
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

        // Read the current status and assignment before applying the change.
        var referral = await db
            .Referrals.Where(r => r.PublicId == publicId)
            .Select(r => new
            {
                r.Id,
                r.Status,
                r.AssignedProviderId,
                r.MissingInformationNote,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (referral is null)
        {
            return Results.NotFound(new { message = "Referral was not found." });
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
                        referral.MissingInformationNote,
                    }
                );
            }

            return Results.Conflict(new { message = "This referral is closed." });
        }

        if (status == ReferralStatus.ReadyToSchedule && referral.AssignedProviderId is null)
        {
            return Results.Conflict(
                new
                {
                    message = "Assign a provider before marking this referral ready to schedule.",
                }
            );
        }

        // Save only if the current status still matches and scheduling still has a provider.
        var updatedCount = await db
            .Referrals.Where(r =>
                r.Id == referral.Id
                && r.Status == referral.Status
                && (status != ReferralStatus.ReadyToSchedule || r.AssignedProviderId != null)
            )
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

        return Results.Ok(
            new
            {
                publicId,
                Status = status.ToString(),
                MissingInformationNote = note,
            }
        );
    }
}
