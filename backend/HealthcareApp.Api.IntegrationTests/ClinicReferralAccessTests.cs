using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HealthcareApp.Api.Data;
using HealthcareApp.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HealthcareApp.Api.IntegrationTests;

public class ClinicReferralAccessTests
{
    [Fact]
    public async Task ListClinicReferrals_WithoutToken_ReturnsUnauthorized()
    {
        await using var factory = new HealthcareApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/clinics/{Guid.NewGuid()}/referrals");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListClinicReferrals_UsesReceivingClinicAndStaffMembership()
    {
        const string frontDeskSubject = "auth0|front-desk-test";
        const string clinicianSubject = "auth0|clinician-test";
        const string patientSubject = "auth0|patient-test";
        const string administratorSubject = "auth0|administrator-test";

        using var tokens = new TestJwtIssuer();
        await using var factory = new HealthcareApiFactory();
        await using var configuredFactory = tokens.CreateFactory(factory);
        using var client = configuredFactory.CreateClient();
        await using var scope = configuredFactory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthcareDbContext>();

        var connection = db.Database.GetDbConnection();
        Assert.Equal(@"(localdb)\MSSQLLocalDB", connection.DataSource);
        Assert.Equal(factory.DatabaseName, connection.Database);

        try
        {
            await db.Database.MigrateAsync();

            var clinic = new Clinic { Name = "First Test Clinic" };
            var otherClinic = new Clinic { Name = "Second Test Clinic" };
            var patient = new Patient
            {
                FirstName = "Taylor",
                LastName = "Demo",
                DateOfBirth = new DateOnly(1995, 5, 10),
            };
            var frontDesk = new AppAccount
            {
                Issuer = TestJwtIssuer.Issuer,
                Subject = frontDeskSubject,
            };
            var clinician = new AppAccount
            {
                Issuer = TestJwtIssuer.Issuer,
                Subject = clinicianSubject,
            };
            var administrator = new AppAccount
            {
                Issuer = TestJwtIssuer.Issuer,
                Subject = administratorSubject,
                IsAdministrator = true,
            };
            db.AddRange(clinic, otherClinic, patient, frontDesk, clinician, administrator);
            await db.SaveChangesAsync();

            var service = new ClinicalService { Name = "Physical Exam", ClinicId = clinic.Id };
            var otherService = new ClinicalService { Name = "Physical Exam", ClinicId = otherClinic.Id };
            db.AddRange(
                service,
                otherService,
                new AppAccount
                {
                    Issuer = TestJwtIssuer.Issuer,
                    Subject = patientSubject,
                    PatientId = patient.Id,
                },
                new ClinicMembership
                {
                    AppAccountId = frontDesk.Id,
                    ClinicId = clinic.Id,
                    Role = ClinicStaffRole.FrontDesk,
                },
                new ClinicMembership
                {
                    AppAccountId = clinician.Id,
                    ClinicId = clinic.Id,
                    Role = ClinicStaffRole.Clinician,
                }
            );
            await db.SaveChangesAsync();

            var provider = await ReferralTestData.AddEligibleProviderAsync(db, service);
            var otherProvider = await ReferralTestData.AddEligibleProviderAsync(db, otherService);

            var referral = new Referral
            {
                PatientId = patient.Id,
                ClinicalServiceId = service.Id,
                AssignedProviderId = provider.Id,
                ReferringProviderName = "Dr. Demo",
                Reason = "First clinic referral",
            };
            var otherReferral = new Referral
            {
                PatientId = patient.Id,
                ClinicalServiceId = otherService.Id,
                AssignedProviderId = otherProvider.Id,
                ReferringProviderName = "Dr. Demo",
                Reason = "Second clinic referral",
            };
            db.Referrals.AddRange(referral, otherReferral);
            await db.SaveChangesAsync();

            var clinicUrl = $"/api/clinics/{clinic.PublicId}/referrals";
            var otherClinicUrl = $"/api/clinics/{otherClinic.PublicId}/referrals";

            tokens.AuthorizeSubject(client, frontDeskSubject);
            await AssertSingleReferralAsync(client, clinicUrl, referral.PublicId);

            // A broad token permission does not grant access to the other clinic's list.
            tokens.AuthorizeSubject(client, frontDeskSubject, "read:referrals");
            using var crossClinic = await client.GetAsync(otherClinicUrl);
            Assert.Equal(HttpStatusCode.Forbidden, crossClinic.StatusCode);

            tokens.AuthorizeSubject(client, clinicianSubject);
            await AssertSingleReferralAsync(client, clinicUrl, referral.PublicId);

            tokens.AuthorizeSubject(client, patientSubject, "read:referrals");
            using var patientRequest = await client.GetAsync(clinicUrl);
            Assert.Equal(HttpStatusCode.Forbidden, patientRequest.StatusCode);

            tokens.AuthorizeSubject(client, administratorSubject);
            await AssertSingleReferralAsync(client, otherClinicUrl, otherReferral.PublicId);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static async Task AssertSingleReferralAsync(HttpClient client, string url, Guid publicId)
    {
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var row = Assert.Single(body.EnumerateArray());
        Assert.Equal(publicId, row.GetProperty("publicId").GetGuid());
    }
}
