using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HealthcareApp.Api.Data;
using HealthcareApp.Api.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HealthcareApp.Api.IntegrationTests;

public class ProviderAuthorizationTests
{
    [Theory]
    [InlineData("List", false, null, HttpStatusCode.Unauthorized)]
    [InlineData("List", true, null, HttpStatusCode.Forbidden)]
    [InlineData("List", true, "read:clinics", HttpStatusCode.Forbidden)]
    [InlineData("List", true, "read:providers", HttpStatusCode.OK)]
    [InlineData("Read", false, null, HttpStatusCode.Unauthorized)]
    [InlineData("Read", true, null, HttpStatusCode.Forbidden)]
    [InlineData("Read", true, "read:providers", HttpStatusCode.OK)]
    [InlineData("Create", false, null, HttpStatusCode.Unauthorized)]
    [InlineData("Create", true, "read:providers", HttpStatusCode.Forbidden)]
    [InlineData("Create", true, "manage:providers", HttpStatusCode.Created)]
    [InlineData("Update", false, null, HttpStatusCode.Unauthorized)]
    [InlineData("Update", true, "read:providers", HttpStatusCode.Forbidden)]
    [InlineData("Update", true, "manage:providers", HttpStatusCode.OK)]
    [InlineData("UpdateMissing", true, "manage:providers", HttpStatusCode.NotFound)]
    [InlineData("Delete", false, null, HttpStatusCode.Unauthorized)]
    [InlineData("Delete", true, "read:providers", HttpStatusCode.Forbidden)]
    [InlineData("Delete", true, "manage:providers", HttpStatusCode.NoContent)]
    public async Task ProviderRoutes_AccessRequirements_ReturnExpectedStatus(
        string operation,
        bool sendToken,
        string? permission,
        HttpStatusCode expectedStatus
    )
    {
        // Use locally signed tokens while keeping real JWT validation enabled.
        using var tokens = new TestJwtIssuer();
        await using var factory = new HealthcareApiFactory();
        await using var configuredFactory = tokens.CreateFactory(factory);

        using var client = configuredFactory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }
        );
        await using var scope = configuredFactory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthcareDbContext>();

        // Verify the database target before creating or deleting anything.
        var connection = db.Database.GetDbConnection();
        Assert.Equal(@"(localdb)\MSSQLLocalDB", connection.DataSource);
        Assert.Equal(factory.DatabaseName, connection.Database);

        try
        {
            // Seed two clinics so filtering and clinic assignment can be verified.
            await db.Database.MigrateAsync();
            var clinic = new Clinic
            {
                Name = "Provider Authorization Clinic",
                Address = "100 Test Avenue",
                TimeZoneId = "America/Chicago",
            };
            var otherClinic = new Clinic
            {
                Name = "Other Provider Test Clinic",
                Address = "100 Test Avenue",
                TimeZoneId = "America/Chicago",
            };
            db.Clinics.AddRange(otherClinic, clinic);
            await db.SaveChangesAsync();

            var provider = new Provider
            {
                ClinicId = clinic.Id,
                FirstName = "Taylor",
                LastName = "Demo",
            };
            var otherProvider = new Provider
            {
                ClinicId = otherClinic.Id,
                FirstName = "Casey",
                LastName = "Other",
            };
            db.Providers.AddRange(provider, otherProvider);
            await db.SaveChangesAsync();

            if (sendToken)
            {
                // Give this request exactly the permissions specified by the test case.
                string[] permissions = permission is null ? [] : [permission];
                tokens.Authorize(client, permissions);
            }

            // List and create use the clinic URL; other operations use the provider URL.
            var collectionUrl = $"/api/clinics/{clinic.PublicId}/providers";
            var targetProviderId = operation == "UpdateMissing" ? Guid.NewGuid() : provider.PublicId;
            var providerUrl = $"/api/providers/{targetProviderId}";
            using var request = operation switch
            {
                "List" => new HttpRequestMessage(HttpMethod.Get, collectionUrl),
                "Read" => new HttpRequestMessage(HttpMethod.Get, providerUrl),
                "Create" => new HttpRequestMessage(HttpMethod.Post, collectionUrl)
                {
                    Content = JsonContent.Create(
                        new { firstName = "Morgan", lastName = "Example" }
                    ),
                },
                "Update" or "UpdateMissing" => new HttpRequestMessage(HttpMethod.Put, providerUrl)
                {
                    Content = JsonContent.Create(
                        new { firstName = "Jordan", lastName = "Updated" }
                    ),
                },
                "Delete" => new HttpRequestMessage(HttpMethod.Delete, providerUrl),
                _ => throw new ArgumentOutOfRangeException(nameof(operation)),
            };

            using var response = await client.SendAsync(request);
            Assert.Equal(expectedStatus, response.StatusCode);

            if (expectedStatus == HttpStatusCode.Unauthorized)
            {
                Assert.Contains(
                    response.Headers.WwwAuthenticate,
                    challenge => challenge.Scheme == "Bearer"
                );
            }

            if (expectedStatus == HttpStatusCode.OK)
            {
                // The clinic list must exclude the provider from the other clinic.
                var body = await response.Content.ReadFromJsonAsync<JsonElement>();
                var returnedProvider =
                    operation == "List" ? Assert.Single(body.EnumerateArray()) : body;

                Assert.Equal(provider.PublicId, returnedProvider.GetProperty("publicId").GetGuid());
                Assert.Equal(
                    operation == "Update" ? "Jordan" : "Taylor",
                    returnedProvider.GetProperty("firstName").GetString()
                );
                Assert.Equal(
                    operation == "Update" ? "Updated" : "Demo",
                    returnedProvider.GetProperty("lastName").GetString()
                );
            }

            // Read fresh values so rejected writes and clinic links are checked in SQL Server.
            var savedProviders = await db.Providers.AsNoTracking().ToListAsync();
            var untouchedProvider = Assert.Single(
                savedProviders,
                p => p.PublicId == otherProvider.PublicId
            );
            AssertProvider(untouchedProvider, otherClinic.Id, "Casey", "Other");

            if (expectedStatus == HttpStatusCode.Created)
            {
                var body = await response.Content.ReadFromJsonAsync<JsonElement>();
                var createdId = body.GetProperty("publicId").GetGuid();

                Assert.NotEqual(Guid.Empty, createdId);
                Assert.NotEqual(provider.PublicId, createdId);
                Assert.NotEqual(otherProvider.PublicId, createdId);
                Assert.Equal("Morgan", body.GetProperty("firstName").GetString());
                Assert.Equal("Example", body.GetProperty("lastName").GetString());
                Assert.Equal(
                    $"/api/providers/{createdId}",
                    response.Headers.Location?.OriginalString
                );
                Assert.Equal(3, savedProviders.Count);

                var createdProvider = Assert.Single(savedProviders, p => p.PublicId == createdId);
                AssertProvider(createdProvider, clinic.Id, "Morgan", "Example");
                var originalProvider = Assert.Single(
                    savedProviders,
                    p => p.PublicId == provider.PublicId
                );
                AssertProvider(originalProvider, clinic.Id, "Taylor", "Demo");
            }
            else if (expectedStatus == HttpStatusCode.NoContent)
            {
                Assert.Empty(await response.Content.ReadAsStringAsync());
                var remainingProvider = Assert.Single(savedProviders);
                Assert.Equal(otherProvider.PublicId, remainingProvider.PublicId);
            }
            else
            {
                Assert.Equal(2, savedProviders.Count);
                var originalProvider = Assert.Single(
                    savedProviders,
                    p => p.PublicId == provider.PublicId
                );

                if (expectedStatus == HttpStatusCode.OK && operation == "Update")
                {
                    AssertProvider(originalProvider, clinic.Id, "Jordan", "Updated");
                }
                else
                {
                    // Rejected writes must preserve names and clinic membership.
                    AssertProvider(originalProvider, clinic.Id, "Taylor", "Demo");
                }
            }
        }
        finally
        {
            // Remove this test's temporary database, including after a failure.
            await db.Database.EnsureDeletedAsync();
        }
    }

    // Check provider details and their clinic foreign key.
    private static void AssertProvider(
        Provider provider,
        int clinicId,
        string firstName,
        string lastName
    )
    {
        Assert.Equal(clinicId, provider.ClinicId);
        Assert.Equal(firstName, provider.FirstName);
        Assert.Equal(lastName, provider.LastName);
    }
}
