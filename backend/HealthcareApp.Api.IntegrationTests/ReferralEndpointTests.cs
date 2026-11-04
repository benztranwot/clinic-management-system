using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HealthcareApp.Api.Data;
using HealthcareApp.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HealthcareApp.Api.IntegrationTests;

public class ReferralEndpointTests
{
    [Fact]
    public async Task CreateReferral_ConcurrentDuplicates_SaveOnlyOne()
    {
        await using var factory = new HealthcareApiFactory();
        using var client = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthcareDbContext>();

        // Verify the target before creating or deleting a database.
        var connection = db.Database.GetDbConnection();
        Assert.Equal(@"(localdb)\MSSQLLocalDB", connection.DataSource);
        Assert.Equal(factory.DatabaseName, connection.Database);

        try
        {
            // Arrange: apply the real migration to the temporary database.
            await db.Database.MigrateAsync();

            var clinic = new Clinic { Name = "Referral Test Clinic" };
            var patient = new Patient
            {
                FirstName = "Taylor",
                LastName = "Demo",
                DateOfBirth = new DateOnly(1998, 4, 12),
            };

            db.Clinics.Add(clinic);
            db.Patients.Add(patient);
            await db.SaveChangesAsync();

            // Use the clinic ID that SQL Server generated when saving it.
            var service = new ClinicalService { ClinicId = clinic.Id, Name = "Physical exam" };
            db.ClinicalServices.Add(service);
            await db.SaveChangesAsync();

            // Arrange: build one request body for both submissions.
            var request = new
            {
                patientPublicId = patient.PublicId,
                clinicalServicePublicId = service.PublicId,
                referringProviderName = "Dr. Demo",
                reason = "Routine physical exam",
            };

            // Act: start both POST requests before awaiting their responses.
            var responses = await Task.WhenAll(
                client.PostAsJsonAsync("/api/referrals", request),
                client.PostAsJsonAsync("/api/referrals", request)
            );

            using var firstResponse = responses[0];
            using var secondResponse = responses[1];

            // Assert: either request can win, but only one may create a referral.
            var created = Assert.Single(
                responses,
                response => response.StatusCode == HttpStatusCode.Created
            );

            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);

            // Confirm SQL Server stored exactly one referral for this patient and service.
            var savedCount = await db.Referrals.CountAsync(referral =>
                referral.PatientId == patient.Id && referral.ClinicalServiceId == service.Id
            );

            Assert.Equal(1, savedCount);
            var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>();
            var referralId = createdBody.GetProperty("publicId").GetGuid();
            var location = created.Headers.Location;
            Assert.NotNull(location);
            Assert.Equal($"/api/referrals/{referralId}", location.OriginalString);

            // Follow the returned URL and verify the saved referral.
            using var read = await client.GetAsync(location);
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);

            var saved = await read.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(referralId, saved.GetProperty("publicId").GetGuid());
            Assert.Equal(patient.PublicId, saved.GetProperty("patientPublicId").GetGuid());
            Assert.Equal(service.PublicId, saved.GetProperty("clinicalServicePublicId").GetGuid());
            Assert.Equal("PendingReview", saved.GetProperty("status").GetString());
            Assert.Equal(
                JsonValueKind.Null,
                saved.GetProperty("assignedProviderPublicId").ValueKind
            );
        }
        finally
        {
            // Drop the temporary database even if an assertion fails.
            await db.Database.EnsureDeletedAsync();
        }
    }
}
