using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HealthcareApp.Api.Authorization;
using HealthcareApp.Api.Data;
using HealthcareApp.Api.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HealthcareApp.Api.IntegrationTests;

public class DemoOnboardingTests
{
    private const string RequestCode = "/api/me/demo-email";
    private const string Provision = "/api/me/demo-workspace";

    [Fact]
    public async Task ProvisioningRouteIsAvailableOnlyWhenEnabledAndRequiresSignIn()
    {
        await using var factory = new HealthcareApiFactory();
        await using var enabled = factory.WithWebHostBuilder(webHost =>
            webHost.UseSetting("DemoWorkspaces:Enabled", "true"));
        using var disabledClient = factory.CreateClient();
        using var enabledClient = enabled.CreateClient();

        using var notAvailable = await disabledClient.PostAsync(RequestCode, null);
        using var unauthorized = await enabledClient.PostAsync(RequestCode, null);
        Assert.Equal(HttpStatusCode.NotFound, notAvailable.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
    }

    [Fact]
    public async Task ConfirmedNewAccountsGetExactlyOneSeededWorkspaceWithinCapacity()
    {
        using var tokens = new TestJwtIssuer();
        await using var factory = new HealthcareApiFactory();
        await using var authenticated = tokens.CreateFactory(factory);
        var mail = new TestConfirmationEmail();
        var clock = new TestClock(DateTimeOffset.UtcNow);
        await using var configured = authenticated.WithWebHostBuilder(webHost => webHost
            .UseSetting("DemoWorkspaces:Enabled", "true")
            .UseSetting("DemoWorkspaces:MaximumWorkspaces", "2")
            .ConfigureTestServices(services =>
            {
                services.AddSingleton<IDemoConfirmationEmail>(mail);
                services.AddSingleton<TimeProvider>(clock);
            }));

        using var first = configured.CreateClient();
        using var second = configured.CreateClient();
        using var third = configured.CreateClient();
        using var existing = configured.CreateClient();

        await using var scope = configured.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthcareDbContext>();
        var connection = db.Database.GetDbConnection();
        Assert.Equal(@"(localdb)\MSSQLLocalDB", connection.DataSource);
        Assert.Equal(factory.DatabaseName, connection.Database);

        try
        {
            await db.Database.MigrateAsync();
            db.AppAccounts.Add(new AppAccount { Issuer = TestJwtIssuer.Issuer, Subject = "auth0|existing-staff" });
            await db.SaveChangesAsync();

            tokens.AuthorizeSubject(first, "auth0|first");
            tokens.AuthorizeSubject(second, "auth0|second");
            tokens.AuthorizeSubject(third, "auth0|third");
            tokens.AuthorizeSubject(existing, "auth0|existing-staff");

            using var hasClinicAccess = await existing.PostAsync(RequestCode, null);
            Assert.Equal(HttpStatusCode.NoContent, hasClinicAccess.StatusCode);

            using var firstEmail = await first.PostAsync(RequestCode, null);
            Assert.Equal(HttpStatusCode.Accepted, firstEmail.StatusCode);
            var firstCode = mail.TakeCode();
            using var resend = await first.PostAsync(RequestCode, null);
            Assert.Equal(HttpStatusCode.TooManyRequests, resend.StatusCode);

            using var wrongIdentity = await ConfirmAsync(second, firstCode);
            Assert.Equal(HttpStatusCode.BadRequest, wrongIdentity.StatusCode);

            var firstAttempts = await Task.WhenAll(ConfirmAsync(first, firstCode), ConfirmAsync(first, firstCode));
            using var firstAttempt = firstAttempts[0];
            using var simultaneousAttempt = firstAttempts[1];
            Assert.Equal(
                new[] { HttpStatusCode.Created, HttpStatusCode.NoContent },
                firstAttempts.Select(response => response.StatusCode).Order().ToArray());

            using var secondEmail = await second.PostAsync(RequestCode, null);
            Assert.Equal(HttpStatusCode.Accepted, secondEmail.StatusCode);
            var secondCode = mail.TakeCode();
            var incorrectCode = secondCode == "00000000" ? "00000001" : "00000000";
            for (var attempt = 1; attempt <= 5; attempt++)
            {
                using var wrongCode = await ConfirmAsync(second, incorrectCode);
                Assert.Equal(attempt < 5 ? HttpStatusCode.BadRequest : HttpStatusCode.TooManyRequests, wrongCode.StatusCode);
            }
            using var lockedCode = await ConfirmAsync(second, secondCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, lockedCode.StatusCode);

            clock.Advance(TimeSpan.FromMinutes(1));
            using var secondResend = await second.PostAsync(RequestCode, null);
            Assert.Equal(HttpStatusCode.Accepted, secondResend.StatusCode);
            using var secondCreated = await ConfirmAsync(second, mail.TakeCode());
            Assert.Equal(HttpStatusCode.Created, secondCreated.StatusCode);

            using var atCapacity = await third.PostAsync(RequestCode, null);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, atCapacity.StatusCode);
            Assert.Null(mail.PendingCode);

            // Returning admins continue to use the same workspace and stored role.
            using var firstRepeat = await ConfirmAsync(first, firstCode);
            Assert.Equal(HttpStatusCode.NoContent, firstRepeat.StatusCode);
            using var profile = await first.GetAsync("/api/me");
            profile.EnsureSuccessStatusCode();
            var account = await profile.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(account.GetProperty("isDemoAdministrator").GetBoolean());
            Assert.True(account.GetProperty("canCreateReferrals").GetBoolean());

            var firstQueue = await ReadQueueAsync(first);
            var secondQueue = await ReadQueueAsync(second);
            Assert.Equal(5, firstQueue.GetArrayLength());
            Assert.Equal(5, secondQueue.GetArrayLength());
            Assert.All(firstQueue.EnumerateArray(), referral =>
                Assert.Equal("Northside Demo Clinic", referral.GetProperty("clinicName").GetString()));
            Assert.Equal(
                firstQueue.EnumerateArray().Select(referral => referral.GetProperty("stage").GetString()).Order(),
                secondQueue.EnumerateArray().Select(referral => referral.GetProperty("stage").GetString()).Order());
            Assert.Empty(firstQueue.EnumerateArray().Select(referral => referral.GetProperty("publicId").GetGuid())
                .Intersect(secondQueue.EnumerateArray().Select(referral => referral.GetProperty("publicId").GetGuid())));

            var workspaceAccounts = await db.AppAccounts.IgnoreQueryFilters().AsNoTracking().ToListAsync();
            Assert.Equal(3, workspaceAccounts.Count);
            Assert.False(Assert.Single(workspaceAccounts, account => account.WorkspaceId == Guid.Empty).IsAdministrator);
            Assert.Equal(2, await db.Appointments.IgnoreQueryFilters().CountAsync());
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static Task<HttpResponseMessage> ConfirmAsync(HttpClient client, string code) =>
        client.PostAsJsonAsync(Provision, new { code });

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan interval) => _now += interval;
    }

    private sealed class TestConfirmationEmail : IDemoConfirmationEmail
    {
        public string? PendingCode { get; private set; }

        public Task<bool> SendAsync(string accessToken, string subject, string code, CancellationToken cancellationToken)
        {
            Assert.False(string.IsNullOrEmpty(accessToken));
            Assert.Null(PendingCode);
            PendingCode = code;
            return Task.FromResult(true);
        }

        public string TakeCode()
        {
            var code = Assert.IsType<string>(PendingCode);
            PendingCode = null;
            Assert.Matches("^[0-9]{8}$", code);
            return code;
        }
    }

    private static async Task<JsonElement> ReadQueueAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/referrals");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
