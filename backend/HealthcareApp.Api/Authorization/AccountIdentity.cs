using System.Security.Claims;
using HealthcareApp.Api.Data;
using HealthcareApp.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HealthcareApp.Api.Authorization;

internal sealed record AccountIdentity(string Issuer, string Subject)
{
    public static AccountIdentity? FromPrincipal(ClaimsPrincipal user)
    {
        var issuer = GetIdentityClaim(user, "iss");
        var subject = GetIdentityClaim(user, "sub");

        return issuer is not null && subject is not null
            ? new AccountIdentity(issuer, subject)
            : null;
    }

    public IQueryable<AppAccount> QueryAccounts(HealthcareDbContext db)
    {
        var issuer = Issuer;
        var subject = Subject;
        var issuerBytes = issuer.Length * sizeof(char);
        var subjectBytes = subject.Length * sizeof(char);

        // The identity columns use a binary collation. Check their byte lengths as well
        // because SQL Server can ignore trailing spaces when comparing strings.
        return db.AppAccounts.Where(account =>
            account.Issuer == issuer
            && account.Subject == subject
            && EF.Functions.DataLength(account.Issuer) == issuerBytes
            && EF.Functions.DataLength(account.Subject) == subjectBytes
        );
    }

    public IQueryable<Clinic> QueryStaffClinics(HealthcareDbContext db)
    {
        var accounts = QueryAccounts(db);
        return db.Clinics.Where(clinic =>
            accounts.Any(account =>
                account.IsAdministrator
                || db.ClinicMemberships.Any(membership =>
                    membership.AppAccountId == account.Id
                    && membership.ClinicId == clinic.Id
                    && (membership.Role == ClinicStaffRole.Clinician
                        || membership.Role == ClinicStaffRole.FrontDesk)
                )
            )
        );
    }

    public IQueryable<Clinic> QuerySchedulingClinics(HealthcareDbContext db)
    {
        var accounts = QueryAccounts(db);
        return db.Clinics.Where(clinic =>
            accounts.Any(account =>
                account.IsAdministrator
                || db.ClinicMemberships.Any(membership =>
                    membership.AppAccountId == account.Id
                    && membership.ClinicId == clinic.Id
                    && membership.Role == ClinicStaffRole.FrontDesk)
            )
        );
    }

    public IQueryable<Provider> QueryOwnProviders(HealthcareDbContext db)
    {
        var accounts = QueryAccounts(db);
        return db.Providers.Where(provider =>
            accounts.Any(account =>
                account.ProviderId == provider.Id
                && db.ClinicMemberships.Any(membership =>
                    membership.AppAccountId == account.Id
                    && membership.ClinicId == provider.ClinicId
                    && membership.Role == ClinicStaffRole.Clinician
                )
            )
        );
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
