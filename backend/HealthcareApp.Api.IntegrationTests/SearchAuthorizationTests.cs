using System.Net;
using System.Net.Http.Json;

namespace HealthcareApp.Api.IntegrationTests;

public class SearchAuthorizationTests
{
    [Theory]
    [InlineData("/api/clinics/search", false, null, HttpStatusCode.Unauthorized)]
    [InlineData("/api/clinics/search", true, null, HttpStatusCode.Forbidden)]
    [InlineData("/api/clinics/search", true, "read:patients", HttpStatusCode.Forbidden)]
    [InlineData("/api/patients/search", false, null, HttpStatusCode.Unauthorized)]
    [InlineData("/api/patients/search", true, null, HttpStatusCode.Forbidden)]
    [InlineData("/api/patients/search", true, "read:clinics", HttpStatusCode.Forbidden)]
    [InlineData("/api/referrals/search", false, null, HttpStatusCode.Unauthorized)]
    [InlineData("/api/referrals/search", true, null, HttpStatusCode.Forbidden)]
    [InlineData("/api/referrals/search", true, "read:patients", HttpStatusCode.Forbidden)]
    public async Task GlobalSearch_WithoutRequiredPermission_DeniesAccess(
        string path, bool sendToken, string? permission, HttpStatusCode expectedStatus)
    {
        using var tokens = new TestJwtIssuer();
        await using var factory = new HealthcareApiFactory();
        await using var configuredFactory = tokens.CreateFactory(factory);
        using var client = configuredFactory.CreateClient();

        if (sendToken)
            tokens.Authorize(client, permission is null ? [] : [permission]);

        // Valid request bodies ensure denial comes from authorization, not validation.
        using var response = await client.PostAsJsonAsync(path, new { page = 1 });
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore == true);
        if (expectedStatus == HttpStatusCode.Unauthorized)
            Assert.Contains(response.Headers.WwwAuthenticate, challenge => challenge.Scheme == "Bearer");
    }

    [Theory]
    [InlineData("POST", "/api/appointments/upcoming/search")]
    [InlineData("POST", "/api/appointments/needs-attention/search")]
    [InlineData("POST", "/api/clinics/11111111-1111-4111-8111-111111111111/referrals/search")]
    [InlineData("GET", "/api/appointments/needs-attention/count")]
    [InlineData("GET", "/api/appointments/booking-options")]
    public async Task ScopedSearchAndOptions_WithoutToken_ReturnsUnauthorized(string method, string path)
    {
        await using var factory = new HealthcareApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
            request.Content = JsonContent.Create(new { page = 1 });

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore == true);
        Assert.Contains(response.Headers.WwwAuthenticate, challenge => challenge.Scheme == "Bearer");
    }
}
