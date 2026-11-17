using System.Globalization;
using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using HealthcareApp.Api.Authorization;
using HealthcareApp.Api.Data;
using HealthcareApp.Api.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;

namespace HealthcareApp.Api.Endpoints;

internal static class DemoOnboardingEndpoints
{
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan RequestWindow = TimeSpan.FromDays(1);
    private const int RequestsPerAccount = 3;
    private const int MaximumFailedAttempts = 5;
    private const string WorkspaceLock = "DemoWorkspace:Provision";

    public static void MapDemoOnboardingEndpoints(
        this IEndpointRouteBuilder app, int maximumWorkspaces, int maximumDailyEmails
    )
    {
        app.MapPost("/api/me/demo-email", (
            HttpContext http, HealthcareDbContext db, IDemoConfirmationEmail email, TimeProvider clock, CancellationToken ct
        ) => SendAsync(http, db, email, clock, maximumWorkspaces, maximumDailyEmails, ct))
            .RequireAuthorization();
        app.MapPost("/api/me/demo-workspace", (
            ConfirmationRequest request, ClaimsPrincipal user, HealthcareDbContext db, TimeProvider clock, CancellationToken ct
        ) => ConfirmAsync(request, user, db, clock, maximumWorkspaces, ct))
            .RequireAuthorization();
    }

    private static async Task<IResult> SendAsync(
        HttpContext http,
        HealthcareDbContext db,
        IDemoConfirmationEmail email,
        TimeProvider clock,
        int maximumWorkspaces,
        int maximumDailyEmails,
        CancellationToken ct
    )
    {
        if (AccountIdentity.FromPrincipal(http.User) is not { } identity
            || await http.GetTokenAsync(JwtBearerDefaults.AuthenticationScheme, "access_token") is not { Length: > 0 } accessToken)
        {
            return Results.Challenge();
        }

        var code = RandomNumberGenerator.GetInt32(100_000_000).ToString("D8", CultureInfo.InvariantCulture);
        var now = clock.GetUtcNow();
        var oldest = now - RequestWindow;
        var accounts = AccountsAcrossWorkspaces(db, identity);
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            if (!await TransactionLock.TryAcquireAsync(db, WorkspaceLock, ct)) return Busy();
            if (await accounts.AnyAsync(ct)) return Results.NoContent();
            if (await IsAtCapacityAsync(db, maximumWorkspaces, ct)) return AtCapacity();

            // Reserve an attempt before contacting either Auth0 or the email provider.
            await db.DemoEmailChallenges.Where(challenge => challenge.RequestedAtUtc <= oldest).ExecuteDeleteAsync(ct);
            if (await db.DemoEmailChallenges.CountAsync(ct) >= maximumDailyEmails)
            {
                return Results.Problem(title: "The demo email limit has been reached. Try again later.", statusCode: 429);
            }

            var requests = await ChallengesForIdentity(db, identity)
                .Select(challenge => challenge.RequestedAtUtc)
                .ToListAsync(ct);
            if (requests.Count >= RequestsPerAccount)
            {
                return Results.Problem(title: "You have reached the confirmation email limit for today.", statusCode: 429);
            }

            if (requests.Any(requestedAt => requestedAt > now.AddMinutes(-1)))
            {
                http.Response.Headers.RetryAfter = "60";
                return Results.Problem(title: "Wait one minute before requesting another code.", statusCode: 429);
            }

            var salt = RandomNumberGenerator.GetBytes(16);
            db.DemoEmailChallenges.Add(new DemoEmailChallenge
            {
                Issuer = identity.Issuer,
                Subject = identity.Subject,
                TokenHash = HashCode(code, salt),
                CodeSalt = salt,
                RequestedAtUtc = now,
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        try
        {
            return await email.SendAsync(accessToken, identity.Subject, code, ct)
                ? Results.Accepted()
                : Results.Problem(title: "Your sign-in provider did not share an email address.", statusCode: 422);
        }
        catch (Exception exception) when (!ct.IsCancellationRequested && exception is
            HttpRequestException or JsonException or SmtpException or OperationCanceledException or InvalidOperationException)
        {
            var logger = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DemoOnboardingEndpoints));
            logger.LogWarning("Demo confirmation email request failed ({ExceptionType}).", exception.GetType().Name);
            return Results.Problem(title: "Could not send the confirmation email. Please try again later.", statusCode: 503);
        }
    }

    private static async Task<IResult> ConfirmAsync(
        ConfirmationRequest request,
        ClaimsPrincipal user,
        HealthcareDbContext db,
        TimeProvider clock,
        int maximumWorkspaces,
        CancellationToken ct
    )
    {
        if (AccountIdentity.FromPrincipal(user) is not { } identity) return Results.Challenge();
        if (request.Code is not { Length: 8 } code || !code.All(char.IsAsciiDigit)) return InvalidCode();

        var now = clock.GetUtcNow();
        var oldest = now - CodeLifetime;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (!await TransactionLock.TryAcquireAsync(db, WorkspaceLock, ct)) return Busy();

        // An already provisioned account keeps its role and data on a repeated submission.
        if (await AccountsAcrossWorkspaces(db, identity).AnyAsync(ct)) return Results.NoContent();

        var challenges = ChallengesForIdentity(db, identity);
        var challenge = await challenges.AsNoTracking()
            .Where(challenge => challenge.RequestedAtUtc > oldest)
            .OrderByDescending(challenge => challenge.RequestedAtUtc)
            .ThenByDescending(challenge => challenge.Id)
            .FirstOrDefaultAsync(ct);
        if (challenge is not { CodeSalt: { } salt, ConsumedAtUtc: null }) return InvalidCode();
        if (challenge.FailedAttempts >= MaximumFailedAttempts) return TooManyCodeAttempts();

        var selectedChallenge = challenges.Where(row => row.Id == challenge.Id);
        if (!CryptographicOperations.FixedTimeEquals(challenge.TokenHash, HashCode(code, salt)))
        {
            var failedAttempts = challenge.FailedAttempts + 1;
            await selectedChallenge.ExecuteUpdateAsync(
                updates => updates.SetProperty(row => row.FailedAttempts, failedAttempts), ct);
            await transaction.CommitAsync(ct);
            return failedAttempts >= MaximumFailedAttempts ? TooManyCodeAttempts() : InvalidCode();
        }

        if (await IsAtCapacityAsync(db, maximumWorkspaces, ct)) return AtCapacity();

        // Keep the tracker empty until the new workspace is selected. The update shares the provisioning transaction.
        await selectedChallenge.ExecuteUpdateAsync(updates => updates.SetProperty(row => row.ConsumedAtUtc, now), ct);
        db.SelectWorkspace(Guid.NewGuid());
        var administrator = new AppAccount { Issuer = identity.Issuer, Subject = identity.Subject, IsAdministrator = true };
        db.AppAccounts.Add(administrator);
        await db.SaveChangesAsync(ct);
        await DemoWorkspaceSeed.CreateAsync(db, administrator.Id, now, ct);
        await transaction.CommitAsync(ct);

        return Results.Created("/api/me", value: null);
    }

    private static IQueryable<AppAccount> AccountsAcrossWorkspaces(HealthcareDbContext db, AccountIdentity identity) =>
        identity.QueryAccounts(db).IgnoreQueryFilters([HealthcareDbContext.WorkspaceQueryFilter]);

    private static IQueryable<DemoEmailChallenge> ChallengesForIdentity(HealthcareDbContext db, AccountIdentity identity)
    {
        var issuerBytes = identity.Issuer.Length * sizeof(char);
        var subjectBytes = identity.Subject.Length * sizeof(char);
        return db.DemoEmailChallenges.Where(challenge => challenge.Issuer == identity.Issuer
            && challenge.Subject == identity.Subject
            && EF.Functions.DataLength(challenge.Issuer) == issuerBytes
            && EF.Functions.DataLength(challenge.Subject) == subjectBytes);
    }

    private static async Task<bool> IsAtCapacityAsync(HealthcareDbContext db, int maximumWorkspaces, CancellationToken ct) =>
        await db.AppAccounts.IgnoreQueryFilters([HealthcareDbContext.WorkspaceQueryFilter])
            .CountAsync(account => account.WorkspaceId != Guid.Empty, ct) >= maximumWorkspaces;

    private static byte[] HashCode(string code, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(code, salt, 100_000, HashAlgorithmName.SHA256, 32);

    private static IResult InvalidCode() =>
        Results.Problem(title: "This confirmation code is incorrect or expired. Use the newest code or request another email.", statusCode: 400);

    private static IResult TooManyCodeAttempts() =>
        Results.Problem(title: "Too many incorrect attempts. Request a new code.", statusCode: 429);

    private static IResult Busy() =>
        Results.Problem(title: "Workspace setup is busy. Please try again.", statusCode: 503);

    private static IResult AtCapacity() =>
        Results.Problem(title: "The public demo is currently at capacity.", statusCode: 503);

    internal sealed record ConfirmationRequest(string? Code);
}
