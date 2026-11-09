using System.Security.Claims;
using HealthcareApp.Api.Authorization;
using HealthcareApp.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace HealthcareApp.Api.Endpoints;

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        // Return only the signed-in user's account and clinic roles.
        app.MapGet("/api/me", GetMeAsync).RequireAuthorization();
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
}
