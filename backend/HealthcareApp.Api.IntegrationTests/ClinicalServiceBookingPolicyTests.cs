using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HealthcareApp.Api.Data;
using HealthcareApp.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HealthcareApp.Api.IntegrationTests;

public class ClinicalServiceBookingPolicyTests
{
    [Fact]
    public async Task UpdateBookingPolicy_WithoutToken_ReturnsUnauthorized()
    {
        await using var factory = new HealthcareApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.PutAsJsonAsync(
            $"/api/services/{Guid.NewGuid()}/booking-policy",
            new { allowsDirectBooking = true }
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UpdateBookingPolicy_AppliesOnlyToTheSelectedService()
    {
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

            var clinic = new Clinic { Name = "Booking Test Clinic" };
            var otherClinic = new Clinic { Name = "Other Booking Test Clinic" };
            db.Clinics.AddRange(clinic, otherClinic);
            await db.SaveChangesAsync();

            // The same name at another clinic has a separate booking policy.
            var service = new ClinicalService { ClinicId = clinic.Id, Name = "Annual Physical" };
            var otherService = new ClinicalService
            {
                ClinicId = otherClinic.Id,
                Name = "Annual Physical",
            };
            db.ClinicalServices.AddRange(service, otherService);
            await db.SaveChangesAsync();

            var serviceUrl = $"/api/services/{service.PublicId}";
            var policyUrl = $"{serviceUrl}/booking-policy";
            var otherServiceUrl = $"/api/services/{otherService.PublicId}";
            var enableDirectBooking = new { allowsDirectBooking = true };

            tokens.Authorize(client, "read:services");
            Assert.False(await AllowsDirectBookingAsync(client, serviceUrl));
            using var denied = await client.PutAsJsonAsync(policyUrl, enableDirectBooking);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

            tokens.Authorize(client, "read:services", "manage:services");
            using var missingChoice = await client.PutAsJsonAsync(policyUrl, new { });
            Assert.Equal(HttpStatusCode.BadRequest, missingChoice.StatusCode);
            Assert.False(await AllowsDirectBookingAsync(client, serviceUrl));

            using var missingService = await client.PutAsJsonAsync(
                $"/api/services/{Guid.NewGuid()}/booking-policy",
                enableDirectBooking
            );
            Assert.Equal(HttpStatusCode.NotFound, missingService.StatusCode);

            using var enabled = await client.PutAsJsonAsync(policyUrl, enableDirectBooking);
            Assert.Equal(HttpStatusCode.NoContent, enabled.StatusCode);
            Assert.True(await AllowsDirectBookingAsync(client, serviceUrl));
            Assert.False(await AllowsDirectBookingAsync(client, otherServiceUrl));

            using var renamed = await client.PutAsJsonAsync(
                serviceUrl,
                new { name = "Annual Physical Exam" }
            );
            Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
            Assert.True(await AllowsDirectBookingAsync(client, serviceUrl));

            var list = await client.GetFromJsonAsync<JsonElement>(
                $"/api/clinics/{clinic.PublicId}/services"
            );
            var listedService = Assert.Single(list.EnumerateArray());
            Assert.True(listedService.GetProperty("allowsDirectBooking").GetBoolean());

            using var disabled = await client.PutAsJsonAsync(
                policyUrl,
                new { allowsDirectBooking = false }
            );
            Assert.Equal(HttpStatusCode.NoContent, disabled.StatusCode);
            Assert.False(await AllowsDirectBookingAsync(client, serviceUrl));
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static async Task<bool> AllowsDirectBookingAsync(HttpClient client, string serviceUrl)
    {
        var service = await client.GetFromJsonAsync<JsonElement>(serviceUrl);
        return service.GetProperty("allowsDirectBooking").GetBoolean();
    }
}
