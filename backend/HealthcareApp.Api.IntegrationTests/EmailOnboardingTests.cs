using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HealthcareApp.Api.Authorization;
using HealthcareApp.Api.Data;
using HealthcareApp.Api.Models;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace HealthcareApp.Api.IntegrationTests;

public class EmailOnboardingTests
{
    [Fact]
    public async Task Onboarding_WithoutToken_ReturnsUnauthorized()
    {
        await using var factory = new HealthcareApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/me/onboarding");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Onboarding_WithoutSubject_ReturnsUnauthorized()
    {
        using var tokens = new TestJwtIssuer();
        await using var factory = new HealthcareApiFactory();
        await using var configured = tokens.CreateFactory(factory);
        using var client = configured.CreateClient();
        tokens.AuthorizeSubject(client, null);
        using var response = await client.GetAsync("/api/me/onboarding");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("auth0|email-user", "alex@example.invalid", true, true)]
    [InlineData("auth0|email-user", "alex@example.invalid", false, true)]
    [InlineData("auth0|email-user", "alex@example.invalid", null, true)]
    [InlineData("auth0|another-user", "alex@example.invalid", true, false)]
    [InlineData(null, "alex@example.invalid", true, false)]
    [InlineData("auth0|email-user", null, true, false)]
    [InlineData("auth0|email-user", "not-an-email", true, false)]
    [InlineData("auth0|email-user", "Alex <alex@example.invalid>", true, false)]
    public async Task UserInfo_RequiresMatchingSubjectAndUsableEmail(
        string? subject, string? email, bool? verified, bool usable)
    {
        using var handler = new UserInfoHandler
        {
            Body = new { sub = subject, email, email_verified = verified },
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri(TestJwtIssuer.Issuer) };
        var profiles = new Auth0EmailProfileClient(http);
        var profile = await profiles.GetAsync("test-access-token", "auth0|email-user", CancellationToken.None);

        Assert.Equal(usable, profile is not null);
        if (usable)
        {
            Assert.Equal(email, profile!.Email);
            Assert.Equal(verified == true, profile.EmailVerified);
        }
        Assert.Equal("/userinfo", handler.LastPath);
        Assert.Equal("Bearer", handler.LastAuthorization?.Scheme);
        Assert.Equal("test-access-token", handler.LastAuthorization?.Parameter);
    }

    [Fact]
    public async Task Onboarding_UsesServerProfileAndLeavesAccountLinksUnchanged()
    {
        const string linkedSubject = "auth0|linked-email-user";
        const string newSubject = "auth0|new-email-user";
        using var tokens = new TestJwtIssuer();
        using var handler = new UserInfoHandler();
        await using var factory = new HealthcareApiFactory();
        await using var signed = tokens.CreateFactory(factory);
        await using var configured = signed.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddHttpClient<Auth0EmailProfileClient>()
                .ConfigurePrimaryHttpMessageHandler(() => handler)));
        using var client = configured.CreateClient();
        await using var scope = configured.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthcareDbContext>();
        Assert.Equal(@"(localdb)\MSSQLLocalDB", db.Database.GetDbConnection().DataSource);
        Assert.Equal(factory.DatabaseName, db.Database.GetDbConnection().Database);

        try
        {
            await db.Database.GetService<IMigrator>().MigrateAsync("20261124001737_AddEmailConfirmationChallenges");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO dbo.AppAccounts (Issuer, Subject, IsAdministrator) VALUES ({TestJwtIssuer.Issuer}, {linkedSubject}, {true})");
            await db.Database.MigrateAsync();
            Assert.Equal(0, await db.Database.SqlQuery<int>(
                $"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name = N'EmailConfirmationChallenges'")
                .SingleAsync());
            db.ChangeTracker.Clear();

            tokens.AuthorizeSubject(client, linkedSubject);
            using var ready = await client.GetAsync("/api/me/onboarding");
            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
            var readyBody = await ready.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("ready", readyBody.GetProperty("status").GetString());
            Assert.Equal(0, handler.Requests);

            tokens.AuthorizeSubject(client, newSubject);
            foreach (var verified in new[] { false, true })
            {
                handler.Body = new { sub = newSubject, email = "alex@example.invalid", email_verified = verified };
                var path = verified ? "/api/me/onboarding"
                    : "/api/me/onboarding?email=attacker%40example.invalid&email_verified=true";
                using var response = await client.GetAsync(path);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.True(response.Headers.CacheControl?.NoStore == true);
                var body = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(verified ? "workspace_pending" : "email_confirmation_required", body.GetProperty("status").GetString());
                Assert.Equal("alex@example.invalid", body.GetProperty("email").GetString());
            }

            handler.Body = new { sub = "auth0|different-user", email = "other@example.invalid", email_verified = true };
            using var wrongProfile = await client.GetAsync("/api/me/onboarding");
            Assert.Equal(HttpStatusCode.UnprocessableEntity, wrongProfile.StatusCode);

            handler.Status = HttpStatusCode.ServiceUnavailable;
            using var unavailable = await client.GetAsync("/api/me/onboarding");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);

            handler.Status = HttpStatusCode.Unauthorized;
            using var expired = await client.GetAsync("/api/me/onboarding");
            Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
            Assert.Contains(expired.Headers.WwwAuthenticate, challenge => challenge.Scheme == "Bearer");

            // Onboarding must never grant access or provision an account.
            var account = Assert.Single(await db.AppAccounts.AsNoTracking().ToListAsync());
            Assert.Equal(linkedSubject, account.Subject);
            Assert.True(account.IsAdministrator);
            using var stillUnlinked = await client.GetAsync("/api/me");
            Assert.Equal(HttpStatusCode.Forbidden, stillUnlinked.StatusCode);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private sealed class UserInfoHandler : HttpMessageHandler
    {
        public object Body { get; set; } = new { };
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public int Requests { get; private set; }
        public string? LastPath { get; private set; }
        public AuthenticationHeaderValue? LastAuthorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            LastPath = request.RequestUri!.AbsolutePath;
            LastAuthorization = request.Headers.Authorization;
            return Task.FromResult(new HttpResponseMessage(Status) { Content = JsonContent.Create(Body) });
        }
    }
}
