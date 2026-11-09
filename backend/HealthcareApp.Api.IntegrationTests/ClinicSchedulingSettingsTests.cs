using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HealthcareApp.Api.Data;
using HealthcareApp.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HealthcareApp.Api.IntegrationTests;

public class ClinicSchedulingSettingsTests
{
    [Fact]
    public async Task UpdateSchedulingSettings_WithoutToken_ReturnsUnauthorized()
    {
        await using var factory = new HealthcareApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.PutAsJsonAsync(
            $"/api/clinics/{Guid.NewGuid()}/scheduling-settings",
            new { address = "100 Demo Way", timeZoneId = "America/Chicago" }
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UpdateSchedulingSettings_ValidatesAndPersistsForAnExistingClinic()
    {
        const string address = "100 Demo Way, St. Cloud, MN";
        const string timeZoneId = "America/Chicago";

        using var tokens = new TestJwtIssuer();
        await using var factory = new HealthcareApiFactory();
        await using var configuredFactory = tokens.CreateFactory(factory);
        using var client = configuredFactory.CreateClient();
        await using var scope = configuredFactory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthcareDbContext>();

        // Verify the test database before applying the migrations.
        var connection = db.Database.GetDbConnection();
        Assert.Equal(@"(localdb)\MSSQLLocalDB", connection.DataSource);
        Assert.Equal(factory.DatabaseName, connection.Database);

        try
        {
            await db.Database.MigrateAsync();
            var clinic = new Clinic { Name = "Scheduling Test Clinic" };
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();

            var clinicUrl = $"/api/clinics/{clinic.PublicId}";
            var settingsUrl = $"{clinicUrl}/scheduling-settings";
            var settings = new { address, timeZoneId };

            tokens.Authorize(client, "read:clinics");
            var before = await client.GetFromJsonAsync<JsonElement>(clinicUrl);
            Assert.Equal(JsonValueKind.Null, before.GetProperty("address").ValueKind);
            Assert.Equal(JsonValueKind.Null, before.GetProperty("timeZoneId").ValueKind);

            using var forbidden = await client.PutAsJsonAsync(settingsUrl, settings);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

            tokens.Authorize(client, "read:clinics", "manage:clinics");
            using var invalidAddress = await client.PutAsJsonAsync(
                settingsUrl,
                new { address = "  ", timeZoneId }
            );
            Assert.Equal(HttpStatusCode.BadRequest, invalidAddress.StatusCode);

            using var invalidZone = await client.PutAsJsonAsync(
                settingsUrl,
                new { address, timeZoneId = "Mars/Olympus" }
            );
            Assert.Equal(HttpStatusCode.BadRequest, invalidZone.StatusCode);

            var afterInvalid = await client.GetFromJsonAsync<JsonElement>(clinicUrl);
            Assert.Equal(JsonValueKind.Null, afterInvalid.GetProperty("address").ValueKind);
            Assert.Equal(JsonValueKind.Null, afterInvalid.GetProperty("timeZoneId").ValueKind);

            using var missingClinic = await client.PutAsJsonAsync(
                $"/api/clinics/{Guid.NewGuid()}/scheduling-settings",
                settings
            );
            Assert.Equal(HttpStatusCode.NotFound, missingClinic.StatusCode);

            using var updated = await client.PutAsJsonAsync(settingsUrl, settings);
            Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);

            using var renamed = await client.PutAsJsonAsync(
                clinicUrl,
                new { name = "Renamed Scheduling Clinic" }
            );
            Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

            var after = await client.GetFromJsonAsync<JsonElement>(clinicUrl);
            Assert.Equal("Renamed Scheduling Clinic", after.GetProperty("name").GetString());
            Assert.Equal(address, after.GetProperty("address").GetString());
            Assert.Equal(timeZoneId, after.GetProperty("timeZoneId").GetString());
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
