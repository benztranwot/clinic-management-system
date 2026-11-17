using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text.Json.Serialization;

namespace HealthcareApp.Api.Authorization;

public interface IDemoConfirmationEmail
{
    Task<bool> SendAsync(string accessToken, string subject, string code, CancellationToken cancellationToken);
}

internal sealed class DemoConfirmationEmail(HttpClient userInfo, IConfiguration settings) : IDemoConfirmationEmail
{
    public async Task<bool> SendAsync(
        string accessToken, string subject, string code, CancellationToken cancellationToken
    )
    {
        var host = settings["DemoEmail:SmtpHost"];
        if (string.IsNullOrWhiteSpace(host) || !MailAddress.TryCreate(settings["DemoEmail:From"], out var from))
        {
            throw new InvalidOperationException("The confirmation email sender is not configured.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await userInfo.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var profile = await response.Content.ReadFromJsonAsync<UserProfile>(cancellationToken);
        if (profile is null || !string.Equals(profile.Subject, subject, StringComparison.Ordinal)
            || !MailAddress.TryCreate(profile.Email, out var recipient))
        {
            return false;
        }

        using var message = new MailMessage(from, recipient)
        {
            Subject = "Your Clinic Management confirmation code",
            Body = $"Your confirmation code is {code}.\n\nEnter it on the Clinic Management screen where you signed in. The code expires in 20 minutes.\n\nIf you did not request this code, you can ignore this email.",
        };
        using var client = new SmtpClient(host, settings.GetValue("DemoEmail:SmtpPort", 587))
        {
            EnableSsl = settings.GetValue("DemoEmail:RequireTls", true),
        };
        if (settings["DemoEmail:SmtpUsername"] is { Length: > 0 } username)
        {
            client.Credentials = new NetworkCredential(username, settings["DemoEmail:SmtpPassword"]);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        await client.SendMailAsync(message, timeout.Token);
        return true;
    }

    private sealed record UserProfile(
        [property: JsonPropertyName("sub")] string? Subject,
        string? Email
    );
}
