using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HealthcareApp.Api.IntegrationTests;

public class RateLimitingTests
{
    [Fact]
    public async Task ApiRequests_ExcessiveTraffic_Returns429WithRetryAfter()
    {
        using var tokens = new TestJwtIssuer();
        await using var factory = new HealthcareApiFactory();
        await using var configuredFactory = tokens.CreateFactory(factory);
        using var client = configuredFactory.CreateClient();
        tokens.Authorize(client);

        await ExhaustQuotaAsync(client);

        using var rejected = await client.GetAsync("/api/auth/check");
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.CacheControl?.NoStore == true);
        Assert.InRange(rejected.Headers.RetryAfter!.Delta!.Value.TotalSeconds, 1, 60);
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType?.MediaType);

        var problem = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(429, problem.GetProperty("status").GetInt32());
        Assert.Equal("Too many requests.", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task ApiRequests_QuotaFollowsIdentityAcrossTokensClientsAndRoutes()
    {
        using var tokens = new TestJwtIssuer();
        await using var factory = new HealthcareApiFactory();
        await using var configuredFactory = tokens.CreateFactory(factory);
        using var firstClient = configuredFactory.CreateClient();
        tokens.AuthorizeSubject(firstClient, "auth0|first-user");

        await ExhaustQuotaAsync(firstClient);

        // Another client and newly issued token must share the original quota.
        using var anotherClient = configuredFactory.CreateClient();
        tokens.AuthorizeSubject(anotherClient, "auth0|first-user");
        anotherClient.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.42");
        using var sameAccount = await anotherClient.GetAsync("/api/auth/check");
        Assert.Equal(HttpStatusCode.TooManyRequests, sameAccount.StatusCode);

        // Search uses POST; changing the route or method must not bypass the limit.
        using var search = await anotherClient.PostAsJsonAsync("/api/patients/search", new { page = 1 });
        Assert.Equal(HttpStatusCode.TooManyRequests, search.StatusCode);

        tokens.AuthorizeSubject(anotherClient, "auth0|second-user");
        using var otherAccount = await anotherClient.GetAsync("/api/auth/check");
        Assert.Equal(HttpStatusCode.OK, otherAccount.StatusCode);
    }

    [Fact]
    public async Task HealthAndAnonymousRequests_DoNotUseAuthenticatedQuota()
    {
        using var tokens = new TestJwtIssuer();
        await using var factory = new HealthcareApiFactory();
        await using var configuredFactory = tokens.CreateFactory(factory);
        using var client = configuredFactory.CreateClient();
        tokens.Authorize(client);

        // Health probes must not consume a signed-in user's quota either.
        for (var request = 0; request < 121; request++)
        {
            using var health = await client.GetAsync("/api/health");
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        }

        await ExhaustQuotaAsync(client);

        using var healthy = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, healthy.StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        using var anonymous = await client.GetAsync("/api/auth/check");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Contains(anonymous.Headers.WwwAuthenticate, challenge => challenge.Scheme == "Bearer");

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "invalid-token");
        using var invalid = await client.GetAsync("/api/auth/check");
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
    }

    private static async Task ExhaustQuotaAsync(HttpClient client)
    {
        // Exercise the real HTTP pipeline without creating a database or waiting for a window.
        for (var request = 0; request < 120; request++)
        {
            using var response = await client.GetAsync("/api/auth/check");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
