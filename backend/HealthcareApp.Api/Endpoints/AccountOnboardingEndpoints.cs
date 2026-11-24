using System.Net;
using System.Text.Json;
using HealthcareApp.Api.Authorization;
using HealthcareApp.Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;

namespace HealthcareApp.Api.Endpoints;

public static class AccountOnboardingEndpoints
{
    public static void MapAccountOnboardingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/me/onboarding", GetAsync).RequireAuthorization();
    }

    private static async Task<IResult> GetAsync(HttpContext http, HealthcareDbContext db,
        Auth0EmailProfileClient profiles, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var identity = AccountIdentity.FromPrincipal(http.User);
        if (identity is null) return Results.Challenge();

        // Existing account links retain their administrator-managed access.
        if (await identity.QueryAccounts(db).AnyAsync(ct))
            return Results.Ok(new { Status = "ready" });

        var token = await http.GetTokenAsync(JwtBearerDefaults.AuthenticationScheme, "access_token");
        if (string.IsNullOrEmpty(token)) return Results.Challenge();

        try
        {
            // Only the matching identity provider profile can establish verification.
            var profile = await profiles.GetAsync(token, identity.Subject, ct);
            if (profile is null)
                return Failure(422, "Your sign-in provider did not share a usable email address.");

            // Verification does not create an account, workspace, or role assignment.
            return Results.Ok(new
            {
                Status = profile.EmailVerified ? "workspace_pending" : "email_confirmation_required",
                profile.Email,
            });
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            return Results.Challenge();
        }
        catch (Exception exception) when (!ct.IsCancellationRequested
            && exception is HttpRequestException or JsonException or OperationCanceledException)
        {
            loggerFactory.CreateLogger(typeof(AccountOnboardingEndpoints).FullName!)
                .LogWarning("Email profile lookup failed ({ExceptionType}).", exception.GetType().Name);
            return Failure(503, "Could not check your email status. Please try again later.");
        }
    }

    private static IResult Failure(int status, string message) => Results.Problem(
        statusCode: status, extensions: new Dictionary<string, object?> { ["message"] = message });
}
