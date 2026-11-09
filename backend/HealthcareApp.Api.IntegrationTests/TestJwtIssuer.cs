using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace HealthcareApp.Api.IntegrationTests;

// Issue locally signed tokens and configure the test host to validate them.
internal sealed class TestJwtIssuer : IDisposable
{
    internal const string Issuer = "https://auth.integration.test/";
    private const string Audience = "clinic-management-api-tests";
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RsaSecurityKey _signingKey;

    public TestJwtIssuer()
    {
        _signingKey = new RsaSecurityKey(_rsa) { KeyId = Guid.NewGuid().ToString("N") };
    }

    public WebApplicationFactory<Program> CreateFactory(HealthcareApiFactory factory)
    {
        return factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => ConfigureServices(services))
        );
    }

    public void ConfigureServices(IServiceCollection services)
    {
        // Trust only the public key; keep the production JWT validation pipeline.
        var validationKey = new RsaSecurityKey(_rsa.ExportParameters(false))
        {
            KeyId = _signingKey.KeyId,
        };

        services.Configure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            options =>
            {
                options.Authority = Issuer;
                options.Audience = Audience;
                var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                configuration.SigningKeys.Add(validationKey);
                options.ConfigurationManager =
                    new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            }
        );
    }

    public void Authorize(HttpClient client, params string[] permissions)
    {
        AuthorizeSubject(client, "auth0|integration-test-user", permissions);
    }

    public void AuthorizeSubject(HttpClient client, string? subject, params string[] permissions)
    {
        // The token includes exactly the permissions requested by the test.
        var now = DateTime.UtcNow;
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Subject = subject is null ? null : new ClaimsIdentity([new Claim("sub", subject)]),
            Claims = new Dictionary<string, object> { ["permissions"] = permissions },
            IssuedAt = now,
            NotBefore = now.AddMinutes(-1),
            Expires = now.AddMinutes(5),
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256),
        });

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public void Dispose()
    {
        _rsa.Dispose();
    }
}
