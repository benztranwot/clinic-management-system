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

public class ClinicAuthorizationTests
{
    [Theory]
    [InlineData(false, null, HttpStatusCode.Unauthorized)]
    [InlineData(true, null, HttpStatusCode.Forbidden)]
    [InlineData(true, "read:patients", HttpStatusCode.Forbidden)]
    [InlineData(true, "read:clinics", HttpStatusCode.OK)]
    public async Task GetClinics_AccessRequirements_ReturnExpectedStatus(
        bool sendToken,
        string? permission,
        HttpStatusCode expectedStatus
    )
    {
        const string issuer = "https://auth.integration.test/";
        const string audience = "clinic-management-api-tests";

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
            // Apply the real migrations and insert one fictional clinic.
            await db.Database.MigrateAsync();

            var clinic = new Clinic { Name = "Authorization Test Clinic" };
            db.Clinics.Add(clinic);
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

            // Send the request through authentication and the ReadClinics policy.
            using var response = await client.GetAsync("/api/clinics");

            Assert.Equal(expectedStatus, response.StatusCode);

            if (expectedStatus == HttpStatusCode.OK)
            {
                // Confirm authorized access reaches the database-backed endpoint.
                var body = await response.Content.ReadFromJsonAsync<JsonElement>();
                var returnedClinic = Assert.Single(body.EnumerateArray());

                Assert.Equal(clinic.PublicId, returnedClinic.GetProperty("publicId").GetGuid());
                Assert.Equal(clinic.Name, returnedClinic.GetProperty("name").GetString());
            }

            if (expectedStatus == HttpStatusCode.Unauthorized)
            {
                Assert.Contains(
                    response.Headers.WwwAuthenticate,
                    challenge => challenge.Scheme == "Bearer"
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
