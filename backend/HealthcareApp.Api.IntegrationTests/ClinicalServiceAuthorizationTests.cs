using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HealthcareApp.Api.Data;
using HealthcareApp.Api.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HealthcareApp.Api.IntegrationTests;

public class ClinicalServiceAuthorizationTests
{
    [Theory]
    [InlineData("List", false, null, HttpStatusCode.Unauthorized)]
    [InlineData("List", true, null, HttpStatusCode.Forbidden)]
    [InlineData("List", true, "read:providers", HttpStatusCode.Forbidden)]
    [InlineData("List", true, "read:services", HttpStatusCode.OK)]
    [InlineData("Read", false, null, HttpStatusCode.Unauthorized)]
    [InlineData("Read", true, null, HttpStatusCode.Forbidden)]
    [InlineData("Read", true, "read:services", HttpStatusCode.OK)]
    [InlineData("Create", false, null, HttpStatusCode.Unauthorized)]
    [InlineData("Create", true, "read:services", HttpStatusCode.Forbidden)]
    [InlineData("Create", true, "manage:services", HttpStatusCode.Created)]
    [InlineData("Update", false, null, HttpStatusCode.Unauthorized)]
    [InlineData("Update", true, "read:services", HttpStatusCode.Forbidden)]
    [InlineData("Update", true, "manage:services", HttpStatusCode.OK)]
    [InlineData("Delete", false, null, HttpStatusCode.Unauthorized)]
    [InlineData("Delete", true, "read:services", HttpStatusCode.Forbidden)]
    [InlineData("Delete", true, "manage:services", HttpStatusCode.NoContent)]
    public async Task ServiceRoutes_AccessRequirements_ReturnExpectedStatus(
        string operation,
        bool sendToken,
        string? permission,
        HttpStatusCode expectedStatus
    )
    {
        const string originalName = "Physical exam";
        const string createdName = "Nutrition consultation";
        const string updatedName = "Therapy assessment";

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
            // Seed two clinics so service filtering and clinic assignment can be checked.
            await db.Database.MigrateAsync();
            var clinic = new Clinic { Name = "Service Authorization Clinic" };
            var otherClinic = new Clinic { Name = "Other Service Test Clinic" };
            db.Clinics.AddRange(otherClinic, clinic);
            await db.SaveChangesAsync();

            // Different clinics may offer services with the same name.
            var service = new ClinicalService { ClinicId = clinic.Id, Name = originalName };
            var otherService = new ClinicalService
            {
                ClinicId = otherClinic.Id,
                Name = originalName,
            };
            db.ClinicalServices.AddRange(service, otherService);
            await db.SaveChangesAsync();

            if (sendToken)
            {
                // Give this request exactly the permissions specified by the test case.
                string[] permissions = permission is null ? [] : [permission];
                tokens.Authorize(client, permissions);
            }

            // List and create use the clinic URL; other operations use the service URL.
            var collectionUrl = $"/api/clinics/{clinic.PublicId}/services";
            var serviceUrl = $"/api/services/{service.PublicId}";
            using var request = operation switch
            {
                "List" => new HttpRequestMessage(HttpMethod.Get, collectionUrl),
                "Read" => new HttpRequestMessage(HttpMethod.Get, serviceUrl),
                "Create" => new HttpRequestMessage(HttpMethod.Post, collectionUrl)
                {
                    Content = JsonContent.Create(new { name = createdName }),
                },
                "Update" => new HttpRequestMessage(HttpMethod.Put, serviceUrl)
                {
                    Content = JsonContent.Create(new { name = updatedName }),
                },
                "Delete" => new HttpRequestMessage(HttpMethod.Delete, serviceUrl),
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
                // The clinic list must exclude the service from the other clinic.
                var body = await response.Content.ReadFromJsonAsync<JsonElement>();
                var returnedService =
                    operation == "List" ? Assert.Single(body.EnumerateArray()) : body;

                Assert.Equal(service.PublicId, returnedService.GetProperty("publicId").GetGuid());
                Assert.Equal(
                    operation == "Update" ? updatedName : originalName,
                    returnedService.GetProperty("name").GetString()
                );
            }

            // Inspect fresh database values, including the other clinic's service.
            var savedServices = await db.ClinicalServices.AsNoTracking().ToListAsync();
            var untouchedService = Assert.Single(
                savedServices,
                s => s.PublicId == otherService.PublicId
            );
            AssertService(untouchedService, otherClinic.Id, originalName);

            if (expectedStatus == HttpStatusCode.Created)
            {
                var body = await response.Content.ReadFromJsonAsync<JsonElement>();
                var createdId = body.GetProperty("publicId").GetGuid();

                Assert.NotEqual(Guid.Empty, createdId);
                Assert.NotEqual(service.PublicId, createdId);
                Assert.NotEqual(otherService.PublicId, createdId);
                Assert.Equal(createdName, body.GetProperty("name").GetString());
                Assert.Equal(
                    $"/api/services/{createdId}",
                    response.Headers.Location?.OriginalString
                );
                Assert.Equal(3, savedServices.Count);

                var createdService = Assert.Single(savedServices, s => s.PublicId == createdId);
                AssertService(createdService, clinic.Id, createdName);
                var originalService = Assert.Single(
                    savedServices,
                    s => s.PublicId == service.PublicId
                );
                AssertService(originalService, clinic.Id, originalName);
            }
            else if (expectedStatus == HttpStatusCode.NoContent)
            {
                Assert.Empty(await response.Content.ReadAsStringAsync());
                var remainingService = Assert.Single(savedServices);
                Assert.Equal(otherService.PublicId, remainingService.PublicId);
            }
            else
            {
                Assert.Equal(2, savedServices.Count);
                var originalService = Assert.Single(
                    savedServices,
                    s => s.PublicId == service.PublicId
                );

                // Rejected writes must preserve both the name and clinic membership.
                AssertService(
                    originalService,
                    clinic.Id,
                    expectedStatus == HttpStatusCode.OK && operation == "Update"
                        ? updatedName
                        : originalName
                );
            }
        }
        finally
        {
            // Remove this test's temporary database, including after a failure.
            await db.Database.EnsureDeletedAsync();
        }
    }

    // Check the service name and its clinic foreign key.
    private static void AssertService(ClinicalService service, int clinicId, string name)
    {
        Assert.Equal(clinicId, service.ClinicId);
        Assert.Equal(name, service.Name);
    }
}
