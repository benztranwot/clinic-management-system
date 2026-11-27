using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HealthcareApp.Api.IntegrationTests;

public class HealthEndpointTests
{
    [Fact]
    public async Task GetHealth_ReturnsOk()
    {
        // Arrange: start the API inside the test and create its HTTP client.
        using var factory = new HealthcareApiFactory();
        using var client = factory.CreateClient();

        // Act: call the same health route we use in Postman.
        using var response = await client.GetAsync("/api/health");

        // Assert: verify the HTTP status and JSON body.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore == true);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ok", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task GetReadiness_WhenDatabaseIsReachable_ReturnsOk()
    {
        // The readiness check only opens a connection, so the server's master database is enough.
        using var factory = new HealthcareApiFactory(
            @"Server=(localdb)\MSSQLLocalDB;Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=True");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore == true);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ok", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task GetReadiness_WhenDatabaseIsUnreachable_Returns503WithoutDetails()
    {
        // Nothing listens on port 1, so the connection fails quickly.
        using var factory = new HealthcareApiFactory(
            "Server=tcp:127.0.0.1,1;Database=HealthcareApp;User ID=nobody;Password=unused;Connect Timeout=2;Encrypt=True;TrustServerCertificate=True");
        using var client = factory.CreateClient();

        using var ready = await client.GetAsync("/api/health/ready");
        using var live = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.True(ready.Headers.CacheControl?.NoStore == true);
        var text = await ready.Content.ReadAsStringAsync();
        Assert.Equal("unavailable", JsonDocument.Parse(text).RootElement.GetProperty("status").GetString());
        Assert.DoesNotContain("127.0.0.1", text);

        // Liveness stays independent of the database.
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    [Fact]
    public async Task RootPath_HasNoPlaceholderRoute()
    {
        using var factory = new HealthcareApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnknownApiRoute_ReturnsProblemDetails()
    {
        using var factory = new HealthcareApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/unknown-route");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore == true);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(404, body.GetProperty("status").GetInt32());
        Assert.Equal("API route not found.", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task UnsupportedMethod_OnKnownApiRoute_RemainsMethodNotAllowed()
    {
        using var factory = new HealthcareApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/health", content: null);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore == true);
    }
}
