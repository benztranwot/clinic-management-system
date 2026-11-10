using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HealthcareApp.Api.Data;
using HealthcareApp.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HealthcareApp.Api.IntegrationTests;

public class PatientReferralAccessTests
{
    private const string ReferralsUrl = "/api/me/referrals";

    [Fact]
    public async Task ListMyReferrals_WithoutToken_ReturnsUnauthorized()
    {
        await using var factory = new HealthcareApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(ReferralsUrl);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListMyReferrals_UsesPatientLinkedToSignedInAccount()
    {
        const string firstSubject = "auth0|patient-one";
        const string secondSubject = "auth0|patient-two";
        const string staffSubject = "auth0|front-desk";

        using var tokens = new TestJwtIssuer();
        await using var factory = new HealthcareApiFactory();
        await using var configuredFactory = tokens.CreateFactory(factory);
        using var client = configuredFactory.CreateClient();
        await using var scope = configuredFactory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthcareDbContext>();

        // Verify the database target before creating or deleting anything.
        var connection = db.Database.GetDbConnection();
        Assert.Equal(@"(localdb)\MSSQLLocalDB", connection.DataSource);
        Assert.Equal(factory.DatabaseName, connection.Database);

        try
        {
            await db.Database.MigrateAsync();

            var clinic = new Clinic { Name = "Patient Test Clinic" };
            var firstPatient = new Patient
            {
                FirstName = "Alex",
                LastName = "Demo",
                DateOfBirth = new DateOnly(1995, 5, 10),
            };
            var secondPatient = new Patient
            {
                FirstName = "Jamie",
                LastName = "Demo",
                DateOfBirth = new DateOnly(1998, 4, 12),
            };
            var staff = new AppAccount { Issuer = TestJwtIssuer.Issuer, Subject = staffSubject };
            db.AddRange(clinic, firstPatient, secondPatient, staff);
            await db.SaveChangesAsync();

            var service = new ClinicalService { Name = "Physical Exam", ClinicId = clinic.Id };
            db.AddRange(
                service,
                new AppAccount
                {
                    Issuer = TestJwtIssuer.Issuer,
                    Subject = firstSubject,
                    PatientId = firstPatient.Id,
                },
                new AppAccount
                {
                    Issuer = TestJwtIssuer.Issuer,
                    Subject = secondSubject,
                    PatientId = secondPatient.Id,
                },
                new ClinicMembership
                {
                    AppAccountId = staff.Id,
                    ClinicId = clinic.Id,
                    Role = ClinicStaffRole.FrontDesk,
                }
            );
            await db.SaveChangesAsync();

            var provider = await ReferralTestData.AddEligibleProviderAsync(db, service);

            // Both patients use the same service at the same clinic.
            var firstReferral = new Referral
            {
                PatientId = firstPatient.Id,
                ClinicalServiceId = service.Id,
                AssignedProviderId = provider.Id,
                ReferringProviderName = "Dr. Demo",
                Reason = "First patient referral",
            };
            var secondReferral = new Referral
            {
                PatientId = secondPatient.Id,
                ClinicalServiceId = service.Id,
                AssignedProviderId = provider.Id,
                ReferringProviderName = "Dr. Demo",
                Reason = "Second patient referral",
                Status = ReferralStatus.Closed,
            };
            db.Referrals.AddRange(firstReferral, secondReferral);
            await db.SaveChangesAsync();

            // Supplying a different patient ID cannot change the account's scope.
            tokens.AuthorizeSubject(client, firstSubject);
            using var firstResponse = await client.GetAsync(
                $"{ReferralsUrl}?patientPublicId={secondPatient.PublicId}"
            );
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
            Assert.True(firstResponse.Headers.CacheControl?.NoStore);
            var firstBody = await firstResponse.Content.ReadFromJsonAsync<JsonElement>();
            var firstRow = Assert.Single(firstBody.EnumerateArray());
            Assert.Equal(firstReferral.PublicId, firstRow.GetProperty("publicId").GetGuid());
            Assert.Equal(service.Name, firstRow.GetProperty("serviceName").GetString());
            Assert.Equal(clinic.Name, firstRow.GetProperty("clinicName").GetString());
            Assert.False(firstRow.TryGetProperty("reason", out _));
            Assert.False(firstRow.TryGetProperty("missingInformationNote", out _));

            tokens.AuthorizeSubject(client, secondSubject);
            using var secondResponse = await client.GetAsync(ReferralsUrl);
            Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
            var secondBody = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();
            var secondRow = Assert.Single(secondBody.EnumerateArray());
            Assert.Equal(secondReferral.PublicId, secondRow.GetProperty("publicId").GetGuid());
            Assert.Equal("Closed", secondRow.GetProperty("status").GetString());

            tokens.AuthorizeSubject(client, staffSubject, "read:referrals");
            using var staffResponse = await client.GetAsync(ReferralsUrl);
            Assert.Equal(HttpStatusCode.Forbidden, staffResponse.StatusCode);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
