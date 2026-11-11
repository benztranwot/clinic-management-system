using HealthcareApp.Api.Data;
using HealthcareApp.Api.Models;

namespace HealthcareApp.Api.IntegrationTests;

internal static class ReferralTestData
{
    public static async Task<Provider> AddEligibleProviderAsync(
        HealthcareDbContext db,
        ClinicalService service,
        string firstName = "Beth"
    )
    {
        var provider = new Provider
        {
            ClinicId = service.ClinicId, FirstName = firstName, LastName = "Demo",
        };
        db.Providers.Add(provider);
        await db.SaveChangesAsync();
        db.ProviderServices.Add(new ProviderService
        {
            ClinicId = service.ClinicId, ProviderId = provider.Id, ClinicalServiceId = service.Id,
        });
        await db.SaveChangesAsync();
        return provider;
    }
}
