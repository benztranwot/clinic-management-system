using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HealthcareApp.Api.Data;
using HealthcareApp.Api.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HealthcareApp.Api.IntegrationTests;

public class ClinicAuthorizationTests
{
    [Theory]
    [InlineData("List", false, null, HttpStatusCode.Unauthorized)]
    [InlineData("List", true, null, HttpStatusCode.Forbidden)]
    [InlineData("List", true, "read:patients", HttpStatusCode.Forbidden)]
    [InlineData("List", true, "read:clinics", HttpStatusCode.OK)]
    [InlineData("Read", false, null, HttpStatusCode.Unauthorized)]
    [InlineData("Read", true, null, HttpStatusCode.Forbidden)]
    [InlineData("Read", true, "read:clinics", HttpStatusCode.OK)]
    [InlineData("Create", false, null, HttpStatusCode.Unauthorized)]
    [InlineData("Create", true, "read:clinics", HttpStatusCode.Forbidden)]
    [InlineData("Create", true, "manage:clinics", HttpStatusCode.Created)]
    [InlineData("Update", false, null, HttpStatusCode.Unauthorized)]
    [InlineData("Update", true, "read:clinics", HttpStatusCode.Forbidden)]
    [InlineData("Update", true, "manage:clinics", HttpStatusCode.OK)]
    [InlineData("Delete", false, null, HttpStatusCode.Unauthorized)]
    [InlineData("Delete", true, "read:clinics", HttpStatusCode.Forbidden)]
    [InlineData("Delete", true, "manage:clinics", HttpStatusCode.NoContent)]
    public async Task ClinicRoutes_AccessRequirements_ReturnExpectedStatus(
        string operation,
        bool sendToken,
        string? permission,
        HttpStatusCode expectedStatus
    )
    {
        const string originalName = "Authorization Test Clinic";
        const string createdName = "Created Authorization Clinic";
        const string updatedName = "Updated Authorization Clinic";

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
            // Apply the real migrations and insert one fictional clinic.
            await db.Database.MigrateAsync();
            var clinic = new Clinic { Name = originalName };
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();

            if (sendToken)
            {
                // Give this request exactly the permissions specified by the test case.
                string[] permissions = permission is null ? [] : [permission];
                tokens.Authorize(client, permissions);
            }

            // Send valid requests so denied writes fail because of authorization.
            var clinicUrl = $"/api/clinics/{clinic.PublicId}";
            using var request = operation switch
            {
                "List" => new HttpRequestMessage(HttpMethod.Get, "/api/clinics"),
                "Read" => new HttpRequestMessage(HttpMethod.Get, clinicUrl),
                "Create" => new HttpRequestMessage(HttpMethod.Post, "/api/clinics")
                {
                    Content = JsonContent.Create(new { name = createdName }),
                },
                "Update" => new HttpRequestMessage(HttpMethod.Put, clinicUrl)
                {
                    Content = JsonContent.Create(new { name = updatedName }),
                },
                "Delete" => new HttpRequestMessage(HttpMethod.Delete, clinicUrl),
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
                // Verify the response identifies the correct clinic and name.
                var body = await response.Content.ReadFromJsonAsync<JsonElement>();
                var returnedClinic =
                    operation == "List" ? Assert.Single(body.EnumerateArray()) : body;

                Assert.Equal(clinic.PublicId, returnedClinic.GetProperty("publicId").GetGuid());
                Assert.Equal(
                    operation == "Update" ? updatedName : originalName,
                    returnedClinic.GetProperty("name").GetString()
                );
            }

            // Query fresh values instead of reusing the context's tracked clinic.
            var savedClinics = await db.Clinics.AsNoTracking().ToListAsync();

            if (expectedStatus == HttpStatusCode.Created)
            {
                var body = await response.Content.ReadFromJsonAsync<JsonElement>();
                var createdId = body.GetProperty("publicId").GetGuid();

                Assert.NotEqual(Guid.Empty, createdId);
                Assert.NotEqual(clinic.PublicId, createdId);
                Assert.Equal(createdName, body.GetProperty("name").GetString());
                Assert.Equal(
                    $"/api/clinics/{createdId}",
                    response.Headers.Location?.OriginalString
                );
                Assert.Equal(2, savedClinics.Count);

                var createdClinic = Assert.Single(savedClinics, c => c.PublicId == createdId);
                Assert.Equal(createdName, createdClinic.Name);
                var originalClinic = Assert.Single(
                    savedClinics,
                    c => c.PublicId == clinic.PublicId
                );
                Assert.Equal(originalName, originalClinic.Name);
            }
            else if (expectedStatus == HttpStatusCode.NoContent)
            {
                Assert.Empty(await response.Content.ReadAsStringAsync());
                Assert.Empty(savedClinics);
            }
            else
            {
                // Rejected writes must leave the original record unchanged.
                var savedClinic = Assert.Single(savedClinics);
                Assert.Equal(clinic.PublicId, savedClinic.PublicId);
                Assert.Equal(
                    expectedStatus == HttpStatusCode.OK && operation == "Update"
                        ? updatedName
                        : originalName,
                    savedClinic.Name
                );
            }
        }
        finally
        {
            // Remove this test's temporary database, including after a failure.
            await db.Database.EnsureDeletedAsync();
        }
    }
}
