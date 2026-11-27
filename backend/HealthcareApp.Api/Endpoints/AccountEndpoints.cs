using System.Security.Claims;
using HealthcareApp.Api.Authorization;
using HealthcareApp.Api.Data;
using HealthcareApp.Api.Models;
using HealthcareApp.Api.Workspaces;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HealthcareApp.Api.Endpoints;

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        // Return only the signed-in user's account and clinic roles.
        app.MapGet("/api/me", GetMeAsync).RequireAuthorization();

        // Patient reads always use the account's patient link.
        app.MapGet("/api/me/referrals/{publicId:guid?}", GetMyReferralsAsync).RequireAuthorization();

        app.MapGet("/api/me/appointments/{publicId:guid?}", GetMyAppointmentsAsync).RequireAuthorization("ReadAppointments");
    }

    private static async Task<IResult> GetMeAsync(
        ClaimsPrincipal user,
        HttpContext http,
        IAuthorizationService authorization,
        IOptions<VisitorWorkspaceOptions> visitorOptions,
        HealthcareDbContext db,
        CancellationToken cancellationToken
    )
    {
        var identity = AccountIdentity.FromPrincipal(user);

        if (identity is null)
        {
            return Results.Challenge();
        }

        var ownProviders = identity.QueryOwnProviders(db);

        // Load the account and its memberships using the identity from the validated token.
        var account = await identity.QueryAccounts(db)
            .Select(account => new
            {
                account.IsAdministrator,
                IsLinkedClinician = ownProviders.Any(),
                ProviderPublicId = db.Providers
                    .Where(provider => provider.Id == account.ProviderId)
                    .Select(provider => (Guid?)provider.PublicId)
                    .FirstOrDefault(),
                PatientPublicId = db.Patients
                    .Where(patient => patient.Id == account.PatientId)
                    .Select(patient => (Guid?)patient.PublicId)
                    .FirstOrDefault(),
                Memberships = (
                    from membership in db.ClinicMemberships
                    join clinic in db.Clinics on membership.ClinicId equals clinic.Id
                    where membership.AppAccountId == account.Id
                    orderby clinic.Name, clinic.Id, membership.Role
                    select new
                    {
                        ClinicPublicId = clinic.PublicId,
                        ClinicName = clinic.Name,
                        membership.Role,
                    }
                ).ToList(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (account is null)
        {
            return Results.Forbid();
        }

        http.Response.Headers.CacheControl = "no-store";

        return Results.Ok(new
        {
            account.IsAdministrator,
            IsVisitorWorkspace = db.CurrentWorkspaceId != Guid.Empty,
            WorkspacePermissions = db.CurrentWorkspaceId != Guid.Empty && account.IsAdministrator && visitorOptions.Value.Enabled
                ? WorkspacePermissionHandler.OwnerPermissions : Array.Empty<string>(),
            CanCreateReferrals = (account.IsAdministrator || account.IsLinkedClinician)
                && (await authorization.AuthorizeAsync(user, HealthcareApp.Api.Authorization.AuthorizationServiceExtensions.ReadReferralReceivingOptions)).Succeeded
                && (await authorization.AuthorizeAsync(user, "ReadPatients")).Succeeded,
            account.ProviderPublicId,
            account.PatientPublicId,
            Memberships = account.Memberships.Select(membership => new
            {
                membership.ClinicPublicId,
                membership.ClinicName,
                Role = membership.Role.ToString(),
            }),
        });
    }

    private static async Task<IResult> GetMyReferralsAsync(
        Guid? publicId,
        ClaimsPrincipal user,
        HttpContext http,
        IAuthorizationService authorization,
        HealthcareDbContext db,
        CancellationToken cancellationToken
    )
    {
        var identity = AccountIdentity.FromPrincipal(user);
        if (identity is null)
        {
            return Results.Challenge();
        }

        // Use the patient's account link to scope every row in the response.
        var account = await identity.QueryAccounts(db)
            .Where(account => account.PatientId != null)
            .Select(account => new
            {
                Referrals = (
                    from referral in db.Referrals
                    join service in db.ClinicalServices on referral.ClinicalServiceId equals service.Id
                    join clinic in db.Clinics on service.ClinicId equals clinic.Id
                    join provider in db.Providers on referral.AssignedProviderId equals provider.Id
                    where referral.PatientId == account.PatientId
                        && (publicId == null || referral.PublicId == publicId)
                    orderby referral.CreatedAtUtc descending, referral.Id descending
                    select new
                    {
                        referral.PublicId,
                        ServiceName = service.Name,
                        ClinicName = clinic.Name,
                        ClinicAddress = clinic.Address,
                        ProviderName = provider.FirstName + " " + provider.LastName,
                        referral.Status,
                        ConfirmedAppointmentPublicId = db.Appointments
                            .Where(appointment => appointment.ReferralId == referral.Id
                                && appointment.PatientId == account.PatientId
                                && appointment.Status == AppointmentStatus.Confirmed)
                            .Select(appointment => (Guid?)appointment.PublicId)
                            .FirstOrDefault(),
                        referral.CreatedAtUtc,
                    }
                ).ToList(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (account is null)
        {
            return Results.Forbid();
        }

        http.Response.Headers.CacheControl = "no-store";

        if (publicId is not null)
        {
            var referral = account.Referrals.SingleOrDefault();
            if (referral is null) return Results.NotFound();

            var stage = ReferralEndpoints.GetStage(referral.Status, referral.ConfirmedAppointmentPublicId.HasValue);
            var canSeeAppointment = stage == "Scheduled"
                && (await authorization.AuthorizeAsync(user, "ReadAppointments")).Succeeded;
            return Results.Ok(new
            {
                referral.PublicId,
                referral.ServiceName,
                referral.ClinicName,
                referral.ClinicAddress,
                referral.ProviderName,
                Status = referral.Status.ToString(),
                Stage = stage,
                referral.CreatedAtUtc,
                AppointmentPublicId = canSeeAppointment ? referral.ConfirmedAppointmentPublicId : null,
            });
        }

        return Results.Ok(
            account.Referrals.Select(referral => new
            {
                referral.PublicId,
                referral.ServiceName,
                referral.ClinicName,
                Status = referral.Status.ToString(),
                Stage = ReferralEndpoints.GetStage(referral.Status, referral.ConfirmedAppointmentPublicId.HasValue),
                referral.CreatedAtUtc,
            })
        );
    }

    private static async Task<IResult> GetMyAppointmentsAsync(
        Guid? publicId,
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

        var account = await identity.QueryAccounts(db)
            .Where(account => account.PatientId != null)
            .Select(account => new
            {
                Appointments = (
                    from appointment in db.Appointments
                    join service in db.ClinicalServices on appointment.ClinicalServiceId equals service.Id
                    join clinic in db.Clinics on service.ClinicId equals clinic.Id
                    join provider in db.Providers on appointment.ProviderId equals provider.Id
                    where appointment.PatientId == account.PatientId
                        && (publicId == null || appointment.PublicId == publicId)
                    orderby appointment.StartUtc descending, appointment.Id descending
                    select new
                    {
                        appointment.PublicId,
                        appointment.StartUtc,
                        appointment.Status,
                        ServiceName = service.Name,
                        ClinicName = clinic.Name,
                        ClinicAddress = clinic.Address,
                        clinic.TimeZoneId,
                        ProviderName = provider.FirstName + " " + provider.LastName,
                        ReferralPublicId = db.Referrals
                            .Where(referral => referral.Id == appointment.ReferralId && referral.PatientId == account.PatientId)
                            .Select(referral => (Guid?)referral.PublicId)
                            .FirstOrDefault(),
                    }
                ).ToList(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (account is null)
        {
            return Results.Forbid();
        }

        http.Response.Headers.CacheControl = "no-store";

        if (publicId is not null)
        {
            var appointment = account.Appointments.SingleOrDefault();
            if (appointment is null) return Results.NotFound();

            return Results.Ok(new
            {
                appointment.PublicId,
                appointment.StartUtc,
                EndUtc = appointment.StartUtc.AddMinutes(Appointment.DurationMinutes),
                Status = appointment.Status.ToString(),
                appointment.ServiceName,
                appointment.ClinicName,
                appointment.ClinicAddress,
                appointment.TimeZoneId,
                appointment.ProviderName,
                appointment.ReferralPublicId,
            });
        }

        return Results.Ok(account.Appointments.Select(appointment => new
        {
            appointment.PublicId,
            appointment.StartUtc,
            EndUtc = appointment.StartUtc.AddMinutes(Appointment.DurationMinutes),
            Status = appointment.Status.ToString(),
            appointment.ServiceName,
            appointment.ClinicName,
            appointment.TimeZoneId,
            appointment.ProviderName,
        }));
    }
}
