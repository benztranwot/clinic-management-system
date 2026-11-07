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

public class PatientAuthorizationTests
{
    [Theory]
    [InlineData("List", false, null, HttpStatusCode.Unauthorized)]
    [InlineData("List", true, null, HttpStatusCode.Forbidden)]
    [InlineData("List", true, "read:clinics", HttpStatusCode.Forbidden)]
    [InlineData("List", true, "read:patients", HttpStatusCode.OK)]
    [InlineData("Read", false, null, HttpStatusCode.Unauthorized)]
    [InlineData("Read", true, null, HttpStatusCode.Forbidden)]
    [InlineData("Read", true, "read:patients", HttpStatusCode.OK)]
    [InlineData("Create", false, null, HttpStatusCode.Unauthorized)]
    [InlineData("Create", true, "read:patients", HttpStatusCode.Forbidden)]
    [InlineData("Create", true, "manage:patients", HttpStatusCode.Created)]
    [InlineData("Update", false, null, HttpStatusCode.Unauthorized)]
    [InlineData("Update", true, "read:patients", HttpStatusCode.Forbidden)]
    [InlineData("Update", true, "manage:patients", HttpStatusCode.OK)]
    [InlineData("Delete", false, null, HttpStatusCode.Unauthorized)]
    [InlineData("Delete", true, "read:patients", HttpStatusCode.Forbidden)]
    [InlineData("Delete", true, "manage:patients", HttpStatusCode.NoContent)]
    public async Task PatientRoutes_AccessRequirements_ReturnExpectedStatus(
        string operation,
        bool sendToken,
        string? permission,
        HttpStatusCode expectedStatus
    )
    {
        const string issuer = "https://auth.integration.test/";
        const string audience = "clinic-management-api-tests";
        var originalBirthDate = new DateOnly(1990, 5, 10);
        var createdBirthDate = new DateOnly(1996, 6, 15);
        var updatedBirthDate = new DateOnly(1991, 8, 20);

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
            // Apply the real migrations and insert one fictional patient.
            await db.Database.MigrateAsync();
            var patient = new Patient
            {
                FirstName = "Taylor",
                LastName = "Demo",
                DateOfBirth = originalBirthDate,
            };
            db.Patients.Add(patient);
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

            // Send complete, valid bodies so a denied write tests authorization.
            var patientUrl = $"/api/patients/{patient.PublicId}";
            using var request = operation switch
            {
                "List" => new HttpRequestMessage(HttpMethod.Get, "/api/patients"),
                "Read" => new HttpRequestMessage(HttpMethod.Get, patientUrl),
                "Create" => new HttpRequestMessage(HttpMethod.Post, "/api/patients")
                {
                    Content = JsonContent.Create(new
                    {
                        firstName = "Morgan",
                        lastName = "Example",
                        dateOfBirth = createdBirthDate,
                    }),
                },
                "Update" => new HttpRequestMessage(HttpMethod.Put, patientUrl)
                {
                    Content = JsonContent.Create(new
                    {
                        firstName = "Jordan",
                        lastName = "Updated",
                        dateOfBirth = updatedBirthDate,
                    }),
                },
                "Delete" => new HttpRequestMessage(HttpMethod.Delete, patientUrl),
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
                // The list returns names; detail and update also return birth date.
                var body = await response.Content.ReadFromJsonAsync<JsonElement>();
                var returnedPatient = operation == "List"
                    ? Assert.Single(body.EnumerateArray())
                    : body;

                Assert.Equal(patient.PublicId, returnedPatient.GetProperty("publicId").GetGuid());
                Assert.Equal(
                    operation == "Update" ? "Jordan" : "Taylor",
                    returnedPatient.GetProperty("firstName").GetString()
                );
                Assert.Equal(
                    operation == "Update" ? "Updated" : "Demo",
                    returnedPatient.GetProperty("lastName").GetString()
                );

                if (operation != "List")
                {
                    Assert.Equal(
                        operation == "Update" ? updatedBirthDate : originalBirthDate,
                        returnedPatient.GetProperty("dateOfBirth").Deserialize<DateOnly>()
                    );
                }
            }

            // Read fresh database values to verify persistence and rejected writes.
            var savedPatients = await db.Patients.AsNoTracking().ToListAsync();

            if (expectedStatus == HttpStatusCode.Created)
            {
                var body = await response.Content.ReadFromJsonAsync<JsonElement>();
                var createdId = body.GetProperty("publicId").GetGuid();

                Assert.NotEqual(Guid.Empty, createdId);
                Assert.NotEqual(patient.PublicId, createdId);
                Assert.Equal("Morgan", body.GetProperty("firstName").GetString());
                Assert.Equal("Example", body.GetProperty("lastName").GetString());
                Assert.Equal(createdBirthDate, body.GetProperty("dateOfBirth").Deserialize<DateOnly>());
                Assert.Equal(
                    $"/api/patients/{createdId}",
                    response.Headers.Location?.OriginalString
                );
                Assert.Equal(2, savedPatients.Count);

                var createdPatient = Assert.Single(savedPatients, p => p.PublicId == createdId);
                AssertDemographics(createdPatient, "Morgan", "Example", createdBirthDate);
                var originalPatient = Assert.Single(savedPatients, p => p.PublicId == patient.PublicId);
                AssertDemographics(originalPatient, "Taylor", "Demo", originalBirthDate);
            }
            else if (expectedStatus == HttpStatusCode.NoContent)
            {
                Assert.Empty(await response.Content.ReadAsStringAsync());
                Assert.Empty(savedPatients);
            }
            else
            {
                var savedPatient = Assert.Single(savedPatients);
                Assert.Equal(patient.PublicId, savedPatient.PublicId);

                if (expectedStatus == HttpStatusCode.OK && operation == "Update")
                {
                    AssertDemographics(savedPatient, "Jordan", "Updated", updatedBirthDate);
                }
                else
                {
                    // A rejected request must preserve every original demographic field.
                    AssertDemographics(savedPatient, "Taylor", "Demo", originalBirthDate);
                }
            }
        }
        finally
        {
            // Remove this test's temporary database, including after a failure.
            await db.Database.EnsureDeletedAsync();
        }
    }

    // Check every demographic field when inspecting the saved patient.
    private static void AssertDemographics(
        Patient patient,
        string firstName,
        string lastName,
        DateOnly dateOfBirth
    )
    {
        Assert.Equal(firstName, patient.FirstName);
        Assert.Equal(lastName, patient.LastName);
        Assert.Equal(dateOfBirth, patient.DateOfBirth);
    }
}
