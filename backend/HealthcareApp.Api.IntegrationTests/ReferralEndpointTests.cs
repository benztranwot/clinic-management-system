using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HealthcareApp.Api.Data;
using HealthcareApp.Api.Models;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace HealthcareApp.Api.IntegrationTests;

public class ReferralEndpointTests
{
    [Fact]
    public async Task CreateReferral_ConcurrentDuplicates_SaveOnlyOne()
    {
        using var tokens = new TestJwtIssuer();
        await using var factory = new HealthcareApiFactory();

        // Suppress the save-failure log in this test, which deliberately causes a duplicate insert.
        await using var configuredFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                tokens.ConfigureServices(services);
                services.ConfigureDbContext<HealthcareDbContext>(options =>
                    options.ConfigureWarnings(warnings =>
                        warnings.Ignore(CoreEventId.SaveChangesFailed)
                    )
                );
            });
        });

        using var client = configuredFactory.CreateClient();
        tokens.Authorize(client, "read:referrals", "manage:referrals");
        await using var scope = configuredFactory.Services.CreateAsyncScope();
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
            var provider = new Provider { ClinicId = clinic.Id, FirstName = "Beth", LastName = "Demo" };
            db.AddRange(service, provider);
            await db.SaveChangesAsync();
            db.ProviderServices.Add(new ProviderService
            {
                ClinicId = clinic.Id, ProviderId = provider.Id, ClinicalServiceId = service.Id,
            });
            await db.SaveChangesAsync();

            using var missingProvider = await client.PostAsJsonAsync("/api/referrals", new
            {
                patientPublicId = patient.PublicId,
                clinicalServicePublicId = service.PublicId,
                referringProviderName = "Dr. Demo",
                reason = "Routine physical exam",
            });
            Assert.Equal(HttpStatusCode.BadRequest, missingProvider.StatusCode);

            var unlinkedProvider = new Provider { ClinicId = clinic.Id, FirstName = "John", LastName = "Demo" };
            db.Providers.Add(unlinkedProvider);
            await db.SaveChangesAsync();
            using var ineligibleProvider = await client.PostAsJsonAsync("/api/referrals", new
            {
                patientPublicId = patient.PublicId,
                clinicalServicePublicId = service.PublicId,
                assignedProviderPublicId = unlinkedProvider.PublicId,
                referringProviderName = "Dr. Demo",
                reason = "Routine physical exam",
            });
            Assert.Equal(HttpStatusCode.BadRequest, ineligibleProvider.StatusCode);
            Assert.False(await db.Referrals.AnyAsync());

            // Arrange: build one request body for both submissions.
            var request = new
            {
                patientPublicId = patient.PublicId,
                clinicalServicePublicId = service.PublicId,
                assignedProviderPublicId = provider.PublicId,
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
            Assert.Equal(provider.PublicId, saved.GetProperty("assignedProviderPublicId").GetGuid());
        }
        finally
        {
            // Drop the temporary database even if an assertion fails.
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task ReassignProvider_WithoutServiceEligibility_KeepsOriginalProvider()
    {
        using var tokens = new TestJwtIssuer();
        await using var factory = new HealthcareApiFactory();
        await using var configuredFactory = tokens.CreateFactory(factory);
        using var client = configuredFactory.CreateClient();
        tokens.Authorize(client, "read:referrals", "manage:referrals");
        await using var scope = configuredFactory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthcareDbContext>();

        // Verify the target before creating or deleting a database.
        var connection = db.Database.GetDbConnection();
        Assert.Equal(@"(localdb)\MSSQLLocalDB", connection.DataSource);
        Assert.Equal(factory.DatabaseName, connection.Database);

        try
        {
            // Apply the real migrations to this test's temporary database.
            await db.Database.MigrateAsync();

            // Create the clinic and patient needed by the referral.
            var clinic = new Clinic { Name = "Provider Eligibility Test Clinic" };
            var patient = new Patient
            {
                FirstName = "Taylor",
                LastName = "Demo",
                DateOfBirth = new DateOnly(1998, 4, 12),
            };

            db.Clinics.Add(clinic);
            db.Patients.Add(patient);
            await db.SaveChangesAsync();

            // Create a provider and service at the same clinic.
            var provider = new Provider
            {
                ClinicId = clinic.Id,
                FirstName = "Jordan",
                LastName = "Demo",
            };

            var service = new ClinicalService
            {
                ClinicId = clinic.Id,
                Name = "Nutrition consultation",
            };

            db.Providers.Add(provider);
            db.ClinicalServices.Add(service);
            await db.SaveChangesAsync();

            // Only the original provider offers this service.
            var originalProvider = await ReferralTestData.AddEligibleProviderAsync(db, service);
            var referral = new Referral
            {
                PatientId = patient.Id,
                ClinicalServiceId = service.Id,
                AssignedProviderId = originalProvider.Id,
                ReferringProviderName = "Dr. Demo",
                Reason = "Nutrition counseling referral",
            };

            db.Referrals.Add(referral);
            await db.SaveChangesAsync();

            await AddAdministratorAsync(db);

            // Attempt to assign the provider through the actual API route.
            using var response = await client.PutAsJsonAsync(
                $"/api/referrals/{referral.PublicId}/provider",
                new { providerPublicId = provider.PublicId }
            );

            // Confirm the API rejected the assignment for the expected reason.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(
                "This provider does not offer the requested service.",
                body.GetProperty("message").GetString()
            );

            // Query SQL Server again instead of using the tracked referral object.
            var saved = await db
                .Referrals.AsNoTracking()
                .SingleAsync(r => r.PublicId == referral.PublicId);

            Assert.Equal(originalProvider.Id, saved.AssignedProviderId);
            Assert.Equal(ReferralStatus.PendingReview, saved.Status);
        }
        finally
        {
            // Remove the temporary database even if the test fails.
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateStatus_MissingInformationWithoutNote_LeavesStatusUnchanged(string? note)
    {
        using var tokens = new TestJwtIssuer();
        await using var factory = new HealthcareApiFactory();
        await using var configuredFactory = tokens.CreateFactory(factory);
        using var client = configuredFactory.CreateClient();
        tokens.Authorize(client, "read:referrals", "manage:referrals");
        await using var scope = configuredFactory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthcareDbContext>();

        // Verify the target before creating or deleting a database.
        var connection = db.Database.GetDbConnection();
        Assert.Equal(@"(localdb)\MSSQLLocalDB", connection.DataSource);
        Assert.Equal(factory.DatabaseName, connection.Database);

        try
        {
            // Apply the real migrations to this test's temporary database.
            await db.Database.MigrateAsync();

            // Create the clinic and patient needed by the referral.
            var clinic = new Clinic { Name = "Missing Information Test Clinic" };
            var patient = new Patient
            {
                FirstName = "Taylor",
                LastName = "Demo",
                DateOfBirth = new DateOnly(1998, 4, 12),
            };

            db.Clinics.Add(clinic);
            db.Patients.Add(patient);
            await db.SaveChangesAsync();

            // Create a service offered by the clinic.
            var service = new ClinicalService { ClinicId = clinic.Id, Name = "Physical exam" };

            db.ClinicalServices.Add(service);
            await db.SaveChangesAsync();

            var provider = await ReferralTestData.AddEligibleProviderAsync(db, service);

            // Start with a pending referral that has no missing information note.
            var referral = new Referral
            {
                PatientId = patient.Id,
                ClinicalServiceId = service.Id,
                AssignedProviderId = provider.Id,
                ReferringProviderName = "Dr. Demo",
                Reason = "Routine physical exam",
            };

            db.Referrals.Add(referral);
            await db.SaveChangesAsync();

            // Attempt to mark the referral as missing information without a valid note.
            using var response = await client.PutAsJsonAsync(
                $"/api/referrals/{referral.PublicId}/status",
                new { status = "MissingInformation", missingInformationNote = note }
            );

            // Confirm the API explains why it rejected the request.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(
                "Enter a missing information note between 1 and 1000 characters.",
                body.GetProperty("message").GetString()
            );

            // Read SQL Server again to confirm the rejected request changed nothing.
            var saved = await db
                .Referrals.AsNoTracking()
                .SingleAsync(r => r.PublicId == referral.PublicId);

            Assert.Equal(ReferralStatus.PendingReview, saved.Status);
            Assert.Null(saved.MissingInformationNote);
        }
        finally
        {
            // Remove the temporary database even if the test fails.
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task UpdateStatus_ClosedReferral_CannotBeReopened()
    {
        using var tokens = new TestJwtIssuer();
        await using var factory = new HealthcareApiFactory();
        await using var configuredFactory = tokens.CreateFactory(factory);
        using var client = configuredFactory.CreateClient();
        tokens.Authorize(client, "read:referrals", "manage:referrals");
        await using var scope = configuredFactory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthcareDbContext>();

        // Verify the target before creating or deleting a database.
        var connection = db.Database.GetDbConnection();
        Assert.Equal(@"(localdb)\MSSQLLocalDB", connection.DataSource);
        Assert.Equal(factory.DatabaseName, connection.Database);

        try
        {
            // Apply the real migrations to this test's temporary database.
            await db.Database.MigrateAsync();

            // Create the clinic and patient needed by the referral.
            var clinic = new Clinic { Name = "Closed Referral Test Clinic" };
            var patient = new Patient
            {
                FirstName = "Taylor",
                LastName = "Demo",
                DateOfBirth = new DateOnly(1998, 4, 12),
            };

            db.Clinics.Add(clinic);
            db.Patients.Add(patient);
            await db.SaveChangesAsync();

            // Create a service offered by the clinic.
            var service = new ClinicalService { ClinicId = clinic.Id, Name = "Physical exam" };

            db.ClinicalServices.Add(service);
            await db.SaveChangesAsync();

            var provider = await ReferralTestData.AddEligibleProviderAsync(db, service);

            // Arrange an existing referral that has already been closed.
            var referral = new Referral
            {
                PatientId = patient.Id,
                ClinicalServiceId = service.Id,
                AssignedProviderId = provider.Id,
                ReferringProviderName = "Dr. Demo",
                Reason = "Routine physical exam",
                Status = ReferralStatus.Closed,
            };

            db.Referrals.Add(referral);
            await db.SaveChangesAsync();

            await AddAdministratorAsync(db);

            // Attempt to reopen the closed referral through the status route.
            using var response = await client.PutAsJsonAsync(
                $"/api/referrals/{referral.PublicId}/status",
                new { status = "PendingReview" }
            );

            // Confirm the API rejects reopening a closed referral.
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("This referral is closed.", body.GetProperty("message").GetString());

            // Read SQL Server again to verify the referral remains closed.
            var saved = await db
                .Referrals.AsNoTracking()
                .SingleAsync(r => r.PublicId == referral.PublicId);

            Assert.Equal(ReferralStatus.Closed, saved.Status);
        }
        finally
        {
            // Remove the temporary database even if the test fails.
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task CreateReferral_AfterPreviousReferralClosed_PreservesBothRecords()
    {
        using var tokens = new TestJwtIssuer();
        await using var factory = new HealthcareApiFactory();
        await using var configuredFactory = tokens.CreateFactory(factory);
        using var client = configuredFactory.CreateClient();
        tokens.Authorize(client, "read:referrals", "manage:referrals");
        await using var scope = configuredFactory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthcareDbContext>();

        // Verify the target before creating or deleting a database.
        var connection = db.Database.GetDbConnection();
        Assert.Equal(@"(localdb)\MSSQLLocalDB", connection.DataSource);
        Assert.Equal(factory.DatabaseName, connection.Database);

        try
        {
            // Apply the real migrations to this test's temporary database.
            await db.Database.MigrateAsync();

            // Create the clinic and patient needed by the referral.
            var clinic = new Clinic { Name = "Referral History Test Clinic" };
            var patient = new Patient
            {
                FirstName = "Taylor",
                LastName = "Demo",
                DateOfBirth = new DateOnly(1998, 4, 12),
            };

            db.Clinics.Add(clinic);
            db.Patients.Add(patient);
            await db.SaveChangesAsync();

            // Create a service offered by the clinic.
            var service = new ClinicalService { ClinicId = clinic.Id, Name = "Physical exam" };

            var provider = new Provider { ClinicId = clinic.Id, FirstName = "Beth", LastName = "Demo" };
            db.AddRange(service, provider);
            await db.SaveChangesAsync();
            db.ProviderServices.Add(new ProviderService
            {
                ClinicId = clinic.Id, ProviderId = provider.Id, ClinicalServiceId = service.Id,
            });
            await AddAdministratorAsync(db);

            var request = new
            {
                patientPublicId = patient.PublicId,
                clinicalServicePublicId = service.PublicId,
                assignedProviderPublicId = provider.PublicId,
                referringProviderName = "Dr. Demo",
                reason = "Routine physical exam",
            };

            // Create the first referral through the API.
            using var firstResponse = await client.PostAsJsonAsync("/api/referrals", request);

            Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

            var firstBody = await firstResponse.Content.ReadFromJsonAsync<JsonElement>();
            var firstPublicId = firstBody.GetProperty("publicId").GetGuid();

            // Close the first referral through the status route.
            using var closeResponse = await client.PutAsJsonAsync(
                $"/api/referrals/{firstPublicId}/status",
                new { status = "Closed" }
            );

            Assert.Equal(HttpStatusCode.OK, closeResponse.StatusCode);

            // Create another referral for the same patient and service.
            using var secondResponse = await client.PostAsJsonAsync("/api/referrals", request);

            Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);

            var secondBody = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();
            var secondPublicId = secondBody.GetProperty("publicId").GetGuid();

            Assert.NotEqual(firstPublicId, secondPublicId);

            // Read both records from SQL Server to verify their saved statuses.
            var saved = await db
                .Referrals.AsNoTracking()
                .Where(r => r.PatientId == patient.Id && r.ClinicalServiceId == service.Id)
                .ToListAsync();

            Assert.Equal(2, saved.Count);

            var original = Assert.Single(saved, r => r.PublicId == firstPublicId);
            var replacement = Assert.Single(saved, r => r.PublicId == secondPublicId);

            Assert.Equal(ReferralStatus.Closed, original.Status);
            Assert.Equal(ReferralStatus.PendingReview, replacement.Status);
        }
        finally
        {
            // Remove the temporary database even if the test fails.
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static async Task AddAdministratorAsync(HealthcareDbContext db)
    {
        db.AppAccounts.Add(new AppAccount
        {
            Issuer = TestJwtIssuer.Issuer,
            Subject = "auth0|integration-test-user",
            IsAdministrator = true,
        });
        await db.SaveChangesAsync();
    }
}
