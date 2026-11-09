using System.Security.Claims;
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
        var issuer = GetIdentityClaim(user, "iss");
        var subject = GetIdentityClaim(user, "sub");

        if (issuer is null || subject is null)
        {
            return Results.Challenge();
        }

        // Load the account and its memberships using the identity from the validated token.
        var account = await db.AppAccounts
            .Where(account => account.Issuer == issuer && account.Subject == subject)
            .Select(account => new
            {
                account.Issuer,
                account.Subject,
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

        // SQL Server can ignore trailing spaces in equality comparisons.
        if (account is null || account.Issuer != issuer || account.Subject != subject)
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

    private static string? GetIdentityClaim(ClaimsPrincipal user, string claimType)
    {
        var claims = user.FindAll(claimType).Take(2).ToArray();
        if (claims.Length != 1)
        {
            return null;
        }

        var value = claims[0].Value;
        return !string.IsNullOrWhiteSpace(value) && value.Length <= 255 && value == value.Trim()
            ? value
            : null;
    }
}
