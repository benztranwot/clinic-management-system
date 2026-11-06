using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace HealthcareApp.Api.IntegrationTests;

public class AuthEndpointTests
{
    [Fact]
    public async Task GetAuthCheck_WithoutToken_ReturnsUnauthorized()
    {
        // Start the API inside the test and prevent automatic redirects.
        await using var factory = new HealthcareApiFactory();
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }
        );

        // Request the protected endpoint without an Authorization header.
        using var response = await client.GetAsync("/api/auth/check");

        // Confirm anonymous access is rejected.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // Confirm the API challenges the caller to provide a bearer token.
        Assert.Contains(
            response.Headers.WwwAuthenticate,
            challenge => challenge.Scheme == "Bearer"
        );
    }

    [Theory]
    [InlineData("Valid", HttpStatusCode.OK)]
    [InlineData("WrongSigningKey", HttpStatusCode.Unauthorized)]
    [InlineData("WrongIssuer", HttpStatusCode.Unauthorized)]
    [InlineData("WrongAudience", HttpStatusCode.Unauthorized)]
    [InlineData("Expired", HttpStatusCode.Unauthorized)]
    public async Task GetAuthCheck_TokenValidation_ReturnsExpectedStatus(
        string scenario,
        HttpStatusCode expectedStatus
    )
    {
        const string issuer = "https://auth.integration.test/";
        const string audience = "clinic-management-api-tests";

        // Generate the signing key trusted by this test server.
        using var rsa = RSA.Create(2048);
        var signingKey = new RsaSecurityKey(rsa) { KeyId = Guid.NewGuid().ToString("N") };

        var validationKey = new RsaSecurityKey(rsa.ExportParameters(false))
        {
            KeyId = signingKey.KeyId,
        };

        // Use a different key with the same ID to test signature verification.
        using var untrustedRsa = RSA.Create(2048);
        var untrustedSigningKey = new RsaSecurityKey(untrustedRsa) { KeyId = signingKey.KeyId };

        // Change one token property for each invalid scenario.
        var now = DateTime.UtcNow;
        var token = new JsonWebTokenHandler().CreateToken(
            new SecurityTokenDescriptor
            {
                Issuer = scenario == "WrongIssuer" ? "https://untrusted.integration.test/" : issuer,
                Audience = scenario == "WrongAudience" ? "another-api" : audience,
                Subject = new ClaimsIdentity([new Claim("sub", "auth0|integration-test-user")]),
                IssuedAt = now.AddHours(-1),
                NotBefore = now.AddHours(-1),

                // Expire well beyond the default clock-skew allowance.
                Expires = scenario == "Expired" ? now.AddMinutes(-10) : now.AddMinutes(5),

                SigningCredentials = new SigningCredentials(
                    scenario == "WrongSigningKey" ? untrustedSigningKey : signingKey,
                    SecurityAlgorithms.RsaSha256
                ),
            }
        );

        await using var factory = new HealthcareApiFactory();

        // Configure the test server with the expected issuer, audience, and key.
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

                        // Supply local metadata so validation works offline.
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

        // Send this scenario's token through the actual JWT middleware.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/api/auth/check");

        Assert.Equal(expectedStatus, response.StatusCode);

        if (expectedStatus == HttpStatusCode.OK)
        {
            // Confirm the valid token reaches the protected endpoint.
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal("Authentication succeeded.", body.GetProperty("message").GetString());
        }
        else
        {
            // Confirm invalid tokens receive a bearer authentication challenge.
            Assert.Contains(
                response.Headers.WwwAuthenticate,
                challenge => challenge.Scheme == "Bearer"
            );
        }
    }
}
