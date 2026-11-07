using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using HealthcareApp.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HealthcareApp.Api.IntegrationTests;

public class ErrorHandlingTests
{
    [Fact]
    public async Task GetClinics_UnexpectedFailure_ReturnsSafeProblemDetails()
    {
        using var tokens = new TestJwtIssuer();
        await using var factory = new HealthcareApiFactory();
        var failure = new FailingConnectionInterceptor();

        await using var configuredFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                tokens.ConfigureServices(services);
                services.ConfigureDbContext<HealthcareDbContext>(options =>
                    options.AddInterceptors(failure)
                );
            });

            // This test deliberately throws; keep its expected error out of the console.
            // This configuration applies only to this test host.
            builder.ConfigureLogging(logging => logging.ClearProviders());
        });

        using var client = configuredFactory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }
        );
        tokens.Authorize(client, "read:clinics");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.GetAsync("/api/clinics");

        // Prove the intended failure occurred, rather than an unrelated startup error.
        Assert.True(failure.Triggered);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var responseText = await response.Content.ReadAsStringAsync();
        var body = JsonSerializer.Deserialize<JsonElement>(responseText);
        Assert.Equal(500, body.GetProperty("status").GetInt32());
        Assert.Equal("An unexpected error occurred.", body.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("traceId").GetString()));
        Assert.DoesNotContain("internal-test-diagnostic-marker", responseText);
        Assert.DoesNotContain(nameof(InvalidOperationException), responseText);
        Assert.False(body.TryGetProperty("detail", out var detail) && detail.ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public async Task CreateClinic_MalformedJson_RemainsBadRequest()
    {
        using var tokens = new TestJwtIssuer();
        await using var factory = new HealthcareApiFactory();
        await using var configuredFactory = tokens.CreateFactory(factory);
        using var client = configuredFactory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }
        );
        tokens.Authorize(client, "manage:clinics");

        // Invalid JSON must remain a client error, not become a server error.
        using var content = new StringContent("{", Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/clinics", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed class FailingConnectionInterceptor : DbConnectionInterceptor
    {
        public bool Triggered { get; private set; }

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            // Fail before opening SQL Server, so this test creates no database.
            Triggered = true;
            throw new InvalidOperationException("internal-test-diagnostic-marker");
        }
    }
}
