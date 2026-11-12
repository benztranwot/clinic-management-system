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

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ok", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task UnknownApiRoute_ReturnsProblemDetails()
    {
        using var factory = new HealthcareApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/unknown-route");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
    }
}
