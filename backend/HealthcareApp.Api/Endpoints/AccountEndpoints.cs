using System.Security.Claims;
using HealthcareApp.Api.Authorization;
using HealthcareApp.Api.Data;
using HealthcareApp.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HealthcareApp.Api.Endpoints;

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        // Return only the signed-in user's account and clinic roles.
        app.MapGet("/api/me", GetMeAsync).RequireAuthorization();

        // Patients can list referrals linked to their own patient record.
        app.MapGet("/api/me/referrals", GetMyReferralsAsync).RequireAuthorization();
    }

    private static async Task<IResult> GetMeAsync(
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

        // Load the account and its memberships using the identity from the validated token.
        var account = await identity.QueryAccounts(db)
            .Select(account => new
            {
                account.IsAdministrator,
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

        // Use the patient's account link to scope every row in the response.
        var account = await identity.QueryAccounts(db)
            .Where(account => account.PatientId != null)
            .Select(account => new
            {
                Referrals = (
                    from referral in db.Referrals
                    join service in db.ClinicalServices on referral.ClinicalServiceId equals service.Id
                    join clinic in db.Clinics on service.ClinicId equals clinic.Id
                    where referral.PatientId == account.PatientId
                    orderby referral.CreatedAtUtc descending, referral.Id descending
                    select new
                    {
                        referral.PublicId,
                        ServiceName = service.Name,
                        ClinicName = clinic.Name,
                        referral.Status,
                        HasConfirmedAppointment = db.Appointments.Any(appointment =>
                            appointment.ReferralId == referral.Id && appointment.Status == AppointmentStatus.Confirmed),
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

        return Results.Ok(
            account.Referrals.Select(referral => new
            {
                referral.PublicId,
                referral.ServiceName,
                referral.ClinicName,
                Status = referral.Status.ToString(),
                Stage = ReferralEndpoints.GetStage(referral.Status, referral.HasConfirmedAppointment),
                referral.CreatedAtUtc,
            })
        );
    }
}
