using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HealthcareApp.Api.Data;
using HealthcareApp.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HealthcareApp.Api.IntegrationTests;

public class AccountEndpointTests
{
    [Fact]
    public async Task GetMe_WithoutToken_ReturnsUnauthorized()
    {
        await using var factory = new HealthcareApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMe_TokenWithoutSubject_ReturnsUnauthorized()
    {
        using var tokens = new TestJwtIssuer();
        await using var factory = new HealthcareApiFactory();
        await using var configuredFactory = tokens.CreateFactory(factory);
        using var client = configuredFactory.CreateClient();
        tokens.AuthorizeSubject(client, subject: null);

        using var response = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMe_MatchesIssuerAndSubject_ReturnsOnlyOwnMemberships()
    {
        const string subject = "auth0|demo-account";

        using var tokens = new TestJwtIssuer();
        await using var factory = new HealthcareApiFactory();
        await using var configuredFactory = tokens.CreateFactory(factory);
        using var client = configuredFactory.CreateClient();
        await using var scope = configuredFactory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthcareDbContext>();

        // Check the database target before creating or deleting anything.
        var connection = db.Database.GetDbConnection();
        Assert.Equal(@"(localdb)\MSSQLLocalDB", connection.DataSource);
        Assert.Equal(factory.DatabaseName, connection.Database);

        try
        {
            await db.Database.MigrateAsync();

            var clinic = new Clinic { Name = "Account Test Clinic" };
            var otherClinic = new Clinic { Name = "Other Test Clinic" };
            var patient = new Patient
            {
                FirstName = "Taylor",
                LastName = "Demo",
                DateOfBirth = new DateOnly(1995, 5, 10),
            };
            db.AddRange(clinic, otherClinic, patient);
            await db.SaveChangesAsync();

            var provider = new Provider
            {
                FirstName = "Jordan",
                LastName = "Demo",
                ClinicId = clinic.Id,
            };
            db.Providers.Add(provider);
            await db.SaveChangesAsync();

            var account = new AppAccount
            {
                Issuer = TestJwtIssuer.Issuer,
                Subject = subject,
                ProviderId = provider.Id,
                PatientId = patient.Id,
            };
            var otherAccount = new AppAccount
            {
                Issuer = "https://other.integration.test/",
                Subject = subject,
                IsAdministrator = true,
            };
            db.AppAccounts.AddRange(account, otherAccount);
            await db.SaveChangesAsync();

            db.ClinicMemberships.AddRange(
                new ClinicMembership
                {
                    AppAccountId = account.Id,
                    ClinicId = clinic.Id,
                    Role = ClinicStaffRole.Clinician,
                },
                new ClinicMembership
                {
                    AppAccountId = otherAccount.Id,
                    ClinicId = otherClinic.Id,
                    Role = ClinicStaffRole.FrontDesk,
                }
            );
            await db.SaveChangesAsync();

            // A valid token for an unlinked or differently cased subject grants no app access.
            tokens.AuthorizeSubject(client, "auth0|Demo-Account");
            using var forbidden = await client.GetAsync("/api/me");
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

            tokens.AuthorizeSubject(client, subject);
            using var response = await client.GetAsync("/api/me");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);

            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(body.GetProperty("isAdministrator").GetBoolean());
            Assert.Equal(provider.PublicId, body.GetProperty("providerPublicId").GetGuid());
            Assert.Equal(patient.PublicId, body.GetProperty("patientPublicId").GetGuid());

            var membership = Assert.Single(body.GetProperty("memberships").EnumerateArray());
            Assert.Equal(clinic.PublicId, membership.GetProperty("clinicPublicId").GetGuid());
            Assert.Equal(clinic.Name, membership.GetProperty("clinicName").GetString());
            Assert.Equal("Clinician", membership.GetProperty("role").GetString());
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
