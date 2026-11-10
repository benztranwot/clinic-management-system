using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HealthcareApp.Api.Data;
using HealthcareApp.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HealthcareApp.Api.IntegrationTests;

public class ProviderTimeOffTests
{
    private const string TimeOffUrl = "/api/me/time-off";

    [Fact]
    public async Task GetTimeOff_WithoutToken_ReturnsUnauthorized()
    {
        await using var factory = new HealthcareApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(TimeOffUrl);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TimeOff_ValidatesIntervalsAndLimitsChangesToTheSignedInProvider()
    {
        const string firstSubject = "auth0|time-off-doctor";
        const string secondSubject = "auth0|time-off-other-doctor";
        const string start = "2030-01-07T15:15:00Z";
        const string end = "2030-01-07T16:00:00Z";

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

            var clinic = new Clinic { Name = "Time Off Test Clinic", TimeZoneId = "America/Chicago" };
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();

            var firstProvider = new Provider
            {
                ClinicId = clinic.Id, FirstName = "Beth", LastName = "Demo",
            };
            var secondProvider = new Provider
            {
                ClinicId = clinic.Id, FirstName = "John", LastName = "Demo",
            };
            db.Providers.AddRange(firstProvider, secondProvider);
            await db.SaveChangesAsync();

            var firstAccount = new AppAccount
            {
                Issuer = TestJwtIssuer.Issuer, Subject = firstSubject, ProviderId = firstProvider.Id,
            };
            var secondAccount = new AppAccount
            {
                Issuer = TestJwtIssuer.Issuer, Subject = secondSubject, ProviderId = secondProvider.Id,
            };
            db.AppAccounts.AddRange(firstAccount, secondAccount);
            await db.SaveChangesAsync();

            db.ClinicMemberships.AddRange(
                new ClinicMembership
                {
                    AppAccountId = firstAccount.Id, ClinicId = clinic.Id, Role = ClinicStaffRole.Clinician,
                },
                new ClinicMembership
                {
                    AppAccountId = secondAccount.Id, ClinicId = clinic.Id, Role = ClinicStaffRole.Clinician,
                }
            );
            await db.SaveChangesAsync();

            var block = new { startUtc = start, endUtc = end };

            tokens.AuthorizeSubject(client, "auth0|unlinked-account", "manage:providers");
            using var denied = await client.PostAsJsonAsync(TimeOffUrl, block);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

            tokens.AuthorizeSubject(client, firstSubject);
            using var ambiguousTime = await client.PostAsJsonAsync(
                TimeOffUrl, new { startUtc = "2030-01-07T15:15:00", endUtc = end }
            );
            Assert.Equal(HttpStatusCode.BadRequest, ambiguousTime.StatusCode);

            using var reversed = await client.PostAsJsonAsync(
                TimeOffUrl, new { startUtc = end, endUtc = start }
            );
            Assert.Equal(HttpStatusCode.BadRequest, reversed.StatusCode);

            using var created = await client.PostAsJsonAsync(TimeOffUrl, block);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>();
            var publicId = createdBody.GetProperty("publicId").GetGuid();
            var blockUrl = $"{TimeOffUrl}/{publicId}";
            Assert.Equal(blockUrl, created.Headers.Location?.OriginalString);

            var detail = await client.GetFromJsonAsync<JsonElement>(blockUrl);
            Assert.Equal(publicId, detail.GetProperty("publicId").GetGuid());
            Assert.Equal(DateTimeOffset.Parse(start), detail.GetProperty("startUtc").GetDateTimeOffset());

            using var overlap = await client.PostAsJsonAsync(
                TimeOffUrl, new { startUtc = "2030-01-07T15:45:00Z", endUtc = "2030-01-07T16:30:00Z" }
            );
            Assert.Equal(HttpStatusCode.Conflict, overlap.StatusCode);

            using var adjacent = await client.PostAsJsonAsync(
                TimeOffUrl, new { startUtc = "2030-01-07T16:00:00+00:00", endUtc = "2030-01-07T16:30:00Z" }
            );
            Assert.Equal(HttpStatusCode.Created, adjacent.StatusCode);

            tokens.AuthorizeSubject(client, secondSubject);
            var otherCalendar = await client.GetFromJsonAsync<JsonElement>(TimeOffUrl);
            Assert.Empty(otherCalendar.EnumerateArray());
            using var hidden = await client.GetAsync(blockUrl);
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
            using var deleteDenied = await client.DeleteAsync(blockUrl);
            Assert.Equal(HttpStatusCode.NotFound, deleteDenied.StatusCode);

            tokens.AuthorizeSubject(client, firstSubject);
            var ownCalendar = await client.GetFromJsonAsync<JsonElement>(TimeOffUrl);
            Assert.Equal(2, ownCalendar.GetArrayLength());
            Assert.Equal(publicId, ownCalendar[0].GetProperty("publicId").GetGuid());

            using var deleted = await client.DeleteAsync(blockUrl);
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
            var remaining = await client.GetFromJsonAsync<JsonElement>(TimeOffUrl);
            Assert.Single(remaining.EnumerateArray());
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
