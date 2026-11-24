using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text.Json.Serialization;

namespace HealthcareApp.Api.Authorization;

public sealed record Auth0EmailProfile(string Email, bool EmailVerified);

public sealed class Auth0EmailProfileClient(HttpClient client)
{
    public async Task<Auth0EmailProfile?> GetAsync(
        string accessToken, string expectedSubject, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var profile = await response.Content.ReadFromJsonAsync<UserInfo>(cancellationToken);
        if (profile is null
            || !string.Equals(profile.Subject, expectedSubject, StringComparison.Ordinal)
            || profile.Email is not { Length: > 0 and <= 254 } email
            || !MailAddress.TryCreate(email, out var address)
            || !string.Equals(address.Address, email, StringComparison.Ordinal))
        {
            return null;
        }

        return new Auth0EmailProfile(email, profile.EmailVerified == true);
    }

    private sealed record UserInfo(
        [property: JsonPropertyName("sub")] string? Subject,
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("email_verified")] bool? EmailVerified);
}
