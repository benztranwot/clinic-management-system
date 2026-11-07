using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using HealthcareApp.Api.Data;
using HealthcareApp.Api.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

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
        const string issuer = "https://auth.integration.test/";
        const string audience = "clinic-management-api-tests";
        const string originalName = "Physical exam";
        const string createdName = "Nutrition consultation";
        const string updatedName = "Therapy assessment";

        // Generate a signing key used only by this test.
        using var rsa = RSA.Create(2048);
        var signingKey = new RsaSecurityKey(rsa) { KeyId = Guid.NewGuid().ToString("N") };
        var validationKey = new RsaSecurityKey(rsa.ExportParameters(false))
        {
            KeyId = signingKey.KeyId,
        };

        await using var factory = new HealthcareApiFactory();

        // Trust local test metadata while keeping real JWT validation enabled.
        await using var configuredFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.Configure<JwtBearerOptions>(
                    JwtBearerDefaults.AuthenticationScheme,
                    options =>
                    {
                        options.Authority = issuer;
                        options.Audience = audience;

                        var configuration = new OpenIdConnectConfiguration { Issuer = issuer };
                        configuration.SigningKeys.Add(validationKey);

                        options.ConfigurationManager =
                            new StaticConfigurationManager<OpenIdConnectConfiguration>(
                                configuration
                            );
                    }
                );
            });
        });

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
            var otherService = new ClinicalService { ClinicId = otherClinic.Id, Name = originalName };
            db.ClinicalServices.AddRange(service, otherService);
            await db.SaveChangesAsync();

            if (sendToken)
            {
                var now = DateTime.UtcNow;

                // Represent permissions as a JSON array, matching Auth0 tokens.
                string[] permissions = permission is null ? [] : [permission];
                var token = new JsonWebTokenHandler().CreateToken(
                    new SecurityTokenDescriptor
                    {
                        Issuer = issuer,
                        Audience = audience,
                        Subject = new ClaimsIdentity([
                            new Claim("sub", "auth0|integration-test-user"),
                        ]),
                        Claims = new Dictionary<string, object> { ["permissions"] = permissions },
                        IssuedAt = now,
                        NotBefore = now.AddMinutes(-1),
                        Expires = now.AddMinutes(5),
                        SigningCredentials = new SigningCredentials(
                            signingKey,
                            SecurityAlgorithms.RsaSha256
                        ),
                    }
                );
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    token
                );
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
                var returnedService = operation == "List"
                    ? Assert.Single(body.EnumerateArray())
                    : body;

                Assert.Equal(service.PublicId, returnedService.GetProperty("publicId").GetGuid());
                Assert.Equal(
                    operation == "Update" ? updatedName : originalName,
                    returnedService.GetProperty("name").GetString()
                );
            }

            // Inspect fresh database values, including the other clinic's service.
            var savedServices = await db.ClinicalServices.AsNoTracking().ToListAsync();
            var untouchedService = Assert.Single(savedServices, s => s.PublicId == otherService.PublicId);
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
                var originalService = Assert.Single(savedServices, s => s.PublicId == service.PublicId);
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
                var originalService = Assert.Single(savedServices, s => s.PublicId == service.PublicId);

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
