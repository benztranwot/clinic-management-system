using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HealthcareApp.Api.Authorization;
using HealthcareApp.Api.Data;
using HealthcareApp.Api.Models;
using HealthcareApp.Api.Workspaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace HealthcareApp.Api.IntegrationTests;

public class VisitorWorkspaceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_RequiresTokenAndSubject(bool sendToken)
    {
        using var tokens = new TestJwtIssuer();
        await using var original = new HealthcareApiFactory();
        await using var factory = tokens.CreateFactory(original);
        using var client = factory.CreateClient();
        if (sendToken) tokens.AuthorizeSubject(client, null);
        using var response = await client.PostAsync("/api/me/workspace", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(false, false, HttpStatusCode.Forbidden)]
    [InlineData(true, true, HttpStatusCode.UnprocessableEntity)]
    public async Task Create_UsesTrustedVerificationAndIdentity(bool verified, bool wrongSubject, HttpStatusCode expected)
    {
        await using var state = await State.CreateAsync();
        state.Profiles.Verified = verified;
        state.Profiles.WrongSubject = wrongSubject;
        using var client = state.Client("auth0|new-visitor");
        using var response = await client.PostAsJsonAsync("/api/me/workspace?email_verified=true", new
        {
            email = "attacker@example.invalid", email_verified = true, subject = "auth0|operator",
            issuer = TestJwtIssuer.Issuer, workspaceId = Guid.Empty, isAdministrator = true,
        });
        Assert.Equal(expected, response.StatusCode);
        await state.AssertOriginalOnlyAsync();
    }

    [Fact]
    public async Task EnrollmentDisabled_PreservesExistingLinksAndDoesNotGrantOriginalOwnerPermissions()
    {
        await using var state = await State.CreateAsync(enabled: false);
        using var visitor = state.Client("auth0|new-visitor");
        using var unavailable = await visitor.PostAsync("/api/me/workspace", null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        using var owner = state.Client("auth0|operator");
        using var ready = await owner.PostAsync("/api/me/workspace", null);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        using var denied = await owner.GetAsync("/api/patients");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(0, state.Profiles.Requests);
        await state.AssertOriginalOnlyAsync();
    }

    [Fact]
    public async Task VerifiedEnrollment_CopiesRelationshipsAndKeepsVisitorsAndOriginalDataIndependent()
    {
        await using var state = await State.CreateAsync();
        using var first = state.Client("auth0|first-visitor");
        using var second = state.Client("auth0|second-visitor");
        using var onboarding = await first.GetAsync("/api/me/onboarding");
        var pending = await onboarding.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(pending.GetProperty("canCreateWorkspace").GetBoolean());
        Assert.Equal("workspace_pending", pending.GetProperty("status").GetString());
        using var created = await first.PostAsync("/api/me/workspace", null);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("/api/me", created.Headers.Location?.OriginalString);
        Assert.True(created.Headers.CacheControl?.NoStore);
        using var next = await second.PostAsync("/api/me/workspace", null);
        Assert.Equal(HttpStatusCode.Created, next.StatusCode);

        var firstAccount = await first.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.True(firstAccount.GetProperty("isVisitorWorkspace").GetBoolean());
        Assert.True(firstAccount.GetProperty("canCreateReferrals").GetBoolean());
        Assert.Equal(WorkspacePermissionHandler.OwnerPermissions.Count, firstAccount.GetProperty("workspacePermissions").GetArrayLength());
        Assert.Equal(JsonValueKind.String, firstAccount.GetProperty("providerPublicId").ValueKind);
        Assert.Single(firstAccount.GetProperty("memberships").EnumerateArray());

        await using var scope = state.Factory.Services.CreateAsyncScope();
        var original = scope.ServiceProvider.GetRequiredService<HealthcareDbContext>();
        var accounts = await original.AppAccounts.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        Assert.Equal(3, accounts.Count); // Never copy the template's identity links.
        var firstOwner = accounts.Single(row => row.Subject == "auth0|first-visitor");
        var secondOwner = accounts.Single(row => row.Subject == "auth0|second-visitor");
        Assert.NotEqual(Guid.Empty, firstOwner.WorkspaceId);
        Assert.NotEqual(firstOwner.WorkspaceId, secondOwner.WorkspaceId);
        await using var copy = state.Context(firstOwner.WorkspaceId);
        Assert.Equal(2, await copy.Patients.CountAsync());
        Assert.Equal(2, await copy.Clinics.CountAsync());
        Assert.Equal(2, await copy.Providers.CountAsync());
        Assert.Equal(2, await copy.ClinicalServices.CountAsync());
        Assert.Equal(2, await copy.ProviderServices.CountAsync());
        Assert.Equal(14, await copy.ProviderWeeklyHours.CountAsync());
        Assert.Equal(1, await copy.ProviderTimeOff.CountAsync());
        Assert.Equal(2, await copy.Referrals.CountAsync());
        Assert.Equal(2, await copy.Appointments.CountAsync());
        Assert.Single(await copy.AppAccounts.ToListAsync());
        Assert.Single(await copy.ClinicMemberships.ToListAsync());
        var appointments = await copy.Appointments.AsNoTracking().ToListAsync();
        Assert.All(appointments, row => Assert.Equal(firstOwner.Id, row.BookedByAppAccountId));
        var confirmed = appointments.Single(row => row.Status == AppointmentStatus.Confirmed);
        Assert.InRange(confirmed.StartUtc, State.Now.AddDays(14), State.Now.AddDays(22));
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
        var local = TimeZoneInfo.ConvertTime(confirmed.StartUtc, zone);
        Assert.Equal(DayOfWeek.Monday, local.DayOfWeek);
        Assert.Equal(8, local.Hour);
        var copiedReferral = await copy.Referrals.SingleAsync(row => row.Id == confirmed.ReferralId);
        Assert.Equal(confirmed.PatientId, copiedReferral.PatientId);
        Assert.Equal(confirmed.ClinicalServiceId, copiedReferral.ClinicalServiceId);
        Assert.Equal(confirmed.ProviderId, copiedReferral.AssignedProviderId);
        var copiedTimeOff = await copy.ProviderTimeOff.SingleAsync();
        Assert.Equal(confirmed.StartUtc, copiedTimeOff.StartUtc);
        Assert.Equal(confirmed.StartUtc.AddMinutes(30), copiedTimeOff.EndUtc);
        Assert.All(appointments, row => Assert.True(row.CreatedAtUtc <= State.Now));

        var sourcePatients = await original.Patients.AsNoTracking().ToListAsync();
        var copiedPatients = await copy.Patients.AsNoTracking().ToListAsync();
        Assert.Empty(sourcePatients.Select(row => row.PublicId).Intersect(copiedPatients.Select(row => row.PublicId)));
        Assert.Empty(sourcePatients.Select(row => row.Id).Intersect(copiedPatients.Select(row => row.Id)));
        foreach (var path in new[] { "/api/clinics", "/api/patients", "/api/referrals", "/api/referrals/receiving-options" })
        {
            using var response = await first.GetAsync(path); // No global Auth0 permissions on this token.
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        foreach (var patient in sourcePatients)
        {
            using var hidden = await first.GetAsync("/api/patients/" + patient.PublicId);
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        }
        var removable = copiedPatients.Single(row => row.FirstName == "Jamie");
        using var foreign = await second.DeleteAsync("/api/patients/" + removable.PublicId);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        using var deleted = await first.DeleteAsync("/api/patients/" + removable.PublicId);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(2, await original.Patients.CountAsync());
        await using var other = state.Context(secondOwner.WorkspaceId);
        Assert.Equal(2, await other.Patients.CountAsync());
        Assert.Equal(1, await copy.Patients.CountAsync());
        Assert.Equal("Alex", (await original.Patients.SingleAsync(row => row.FirstName == "Alex")).FirstName);
        var search = await first.PostAsJsonAsync("/api/patients/search", new { page = 1, workspaceId = secondOwner.WorkspaceId });
        using (search) Assert.Equal(1, (await search.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("totalCount").GetInt32());

        var profileReads = state.Profiles.Requests;
        using var retry = await first.PostAsync("/api/me/workspace", null);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(profileReads, state.Profiles.Requests);
        Assert.Equal(3, await original.Workspaces.IgnoreQueryFilters().CountAsync());
        using var root = state.Client("auth0|operator");
        using var rootDenied = await root.GetAsync("/api/patients");
        Assert.Equal(HttpStatusCode.Forbidden, rootDenied.StatusCode);
    }

    [Fact]
    [Trait("Category", "Scale")]
    public async Task ScaleTemplate_CopiesHundredsOfRecordsWithoutReusingIdentitiesOrPublicIds()
    {
        await using var state = await State.CreateAsync();
        await using var scope = state.Factory.Services.CreateAsyncScope();
        var source = scope.ServiceProvider.GetRequiredService<HealthcareDbContext>();
        var seed = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "SeedScaleData.sql"));
        await source.Database.ExecuteSqlRawAsync(seed);
        var sourceAppointments = await source.Appointments.AsNoTracking().ToListAsync();
        var sourcePatients = await source.Patients.AsNoTracking().ToListAsync();
        var sourceReferrals = await source.Referrals.AsNoTracking().ToListAsync();
        var sourceAccounts = await source.AppAccounts.CountAsync();
        Assert.True(sourceAppointments.Count >= 1000);
        Assert.True(sourcePatients.Count >= 250);
        Assert.True(sourceReferrals.Count >= 300);
        using var client = state.Client("auth0|scale-visitor");
        using var response = await client.PostAsync("/api/me/workspace", null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var account = await source.AppAccounts.IgnoreQueryFilters().SingleAsync(row => row.Subject == "auth0|scale-visitor");
        await using var copy = state.Context(account.WorkspaceId);
        var copiedAppointments = await copy.Appointments.AsNoTracking().ToListAsync();
        Assert.Equal(sourceAppointments.Count, copiedAppointments.Count);
        Assert.Equal(sourcePatients.Count, await copy.Patients.CountAsync());
        Assert.Equal(sourceReferrals.Count, await copy.Referrals.CountAsync());
        Assert.Equal(await source.Providers.CountAsync(), await copy.Providers.CountAsync());
        Assert.Equal(await source.ClinicalServices.CountAsync(), await copy.ClinicalServices.CountAsync());
        Assert.Empty(copiedAppointments.Select(row => row.PublicId).Intersect(sourceAppointments.Select(row => row.PublicId)));
        Assert.All(copiedAppointments, row => Assert.Equal(account.Id, row.BookedByAppAccountId));
        Assert.Equal(sourceAccounts, await source.AppAccounts.CountAsync());
        Assert.Single(await copy.AppAccounts.ToListAsync());
        Assert.Equal(sourceAppointments.Count, await source.Appointments.CountAsync());
        Assert.Equal(sourceReferrals.Count, await source.Referrals.CountAsync());
    }

    [Fact]
    public async Task ConcurrentEnrollment_ForOneIdentity_CreatesOneCompleteWorkspace()
    {
        await using var state = await State.CreateAsync();
        using var first = state.Client("auth0|same-visitor");
        using var second = state.Client("auth0|same-visitor");
        var responses = await Task.WhenAll(first.PostAsync("/api/me/workspace", null), second.PostAsync("/api/me/workspace", null));
        try
        {
            Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.Created);
            Assert.All(responses, response => Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK }));
            await using var scope = state.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HealthcareDbContext>();
            Assert.Equal(2, await db.Workspaces.IgnoreQueryFilters().CountAsync());
            Assert.Equal(2, await db.AppAccounts.IgnoreQueryFilters().CountAsync());
            Assert.Equal(4, await db.Appointments.IgnoreQueryFilters().CountAsync());
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Theory]
    [InlineData(false, HttpStatusCode.ServiceUnavailable, "workspace_limit")]
    [InlineData(true, HttpStatusCode.TooManyRequests, "workspace_daily_limit")]
    public async Task Enrollment_EnforcesSharedWorkspaceLimits(bool daily, HttpStatusCode expected, string code)
    {
        await using var state = await State.CreateAsync(maximum: daily ? 10 : 1, daily: daily ? 1 : 10);
        using var first = state.Client("auth0|first-visitor");
        using var second = state.Client("auth0|second-visitor");
        using var created = await first.PostAsync("/api/me/workspace", null);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var denied = await second.PostAsync("/api/me/workspace", null);
        Assert.Equal(expected, denied.StatusCode);
        Assert.Equal(code, (await denied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        if (daily) Assert.True(denied.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        await using var scope = state.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthcareDbContext>();
        Assert.Equal(2, await db.Workspaces.IgnoreQueryFilters().CountAsync());
        Assert.Equal(2, await db.AppAccounts.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task CopyFailure_RollsBackWorkspaceOwnerAndClinicalRecords_ThenAllowsRetry()
    {
        await using var state = await State.CreateAsync();
        await using var scope = state.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthcareDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER dbo.RejectVisitorAppointments ON dbo.Appointments AFTER INSERT AS
            IF EXISTS (SELECT 1 FROM inserted WHERE WorkspaceId <> '00000000-0000-0000-0000-000000000000')
                THROW 51061, 'Simulated copy failure.', 1;
            """);
        using var client = state.Client("auth0|retry-visitor");
        using var failure = await client.PostAsync("/api/me/workspace", null);
        Assert.Equal(HttpStatusCode.InternalServerError, failure.StatusCode);
        Assert.DoesNotContain("Simulated copy failure", await failure.Content.ReadAsStringAsync());
        await state.AssertOriginalOnlyAsync();
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER dbo.RejectVisitorAppointments;");
        using var retry = await client.PostAsync("/api/me/workspace", null);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
    }

    [Fact]
    public async Task EmptyTemplate_ReturnsSafeFailureWithoutProvisioning()
    {
        await using var state = await State.CreateAsync(seed: false);
        using var client = state.Client("auth0|new-visitor");
        using var response = await client.PostAsync("/api/me/workspace", null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("workspace_template_unavailable", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        await using var scope = state.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthcareDbContext>();
        Assert.Single(await db.Workspaces.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.AppAccounts.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task NarrowEnrollmentOperation_WorksWithoutAccountWrites_AndRejectsOriginalWorkspace()
    {
        await using var state = await State.CreateAsync();
        await using var scope = state.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthcareDbContext>();
        var workspace = new Workspace { CreatedAtUtc = State.Now };
        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync();
        await using var copy = state.Context(workspace.Id);
        var clinic = new Clinic { Name = "Private", Address = "42 Cedar Lane", TimeZoneId = "America/Chicago" };
        copy.Clinics.Add(clinic);
        await copy.SaveChangesAsync();
        var provider = new Provider { ClinicId = clinic.Id, FirstName = "Beth", LastName = "Hart" };
        copy.Providers.Add(provider);
        await copy.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE USER [visitor_runtime_test] WITHOUT LOGIN;
            GRANT SELECT ON dbo.AppAccounts TO [visitor_runtime_test];
            GRANT SELECT ON dbo.ClinicMemberships TO [visitor_runtime_test];
            GRANT EXECUTE ON dbo.CreateVisitorWorkspaceAccount TO [visitor_runtime_test];
            """);
        // The stored procedure's ownership chain is the only permitted account write.
        var result = await db.Database.SqlQuery<int>($"""
            EXECUTE AS USER = 'visitor_runtime_test';
            BEGIN TRY
                IF HAS_PERMS_BY_NAME('dbo.AppAccounts', 'OBJECT', 'INSERT') <> 0
                    OR HAS_PERMS_BY_NAME('dbo.AppAccounts', 'OBJECT', 'UPDATE') <> 0
                    OR HAS_PERMS_BY_NAME('dbo.AppAccounts', 'OBJECT', 'DELETE') <> 0
                    OR HAS_PERMS_BY_NAME('dbo.ClinicMemberships', 'OBJECT', 'INSERT') <> 0
                    OR HAS_PERMS_BY_NAME('dbo.ClinicMemberships', 'OBJECT', 'UPDATE') <> 0
                    OR HAS_PERMS_BY_NAME('dbo.ClinicMemberships', 'OBJECT', 'DELETE') <> 0
                    THROW 51062, 'Unexpected account write permission.', 1;
                BEGIN TRANSACTION;
                DECLARE @account int;
                EXEC dbo.CreateVisitorWorkspaceAccount @WorkspaceId={workspace.Id}, @Issuer={TestJwtIssuer.Issuer},
                    @Subject=N'auth0|restricted-visitor', @ProviderId={provider.Id}, @AccountId=@account OUTPUT;
                IF NOT EXISTS (SELECT 1 FROM dbo.AppAccounts WHERE Id=@account AND WorkspaceId={workspace.Id})
                    THROW 51062, 'Visitor owner was not inserted.', 1;
                ROLLBACK;
                REVERT;
            END TRY
            BEGIN CATCH
                IF XACT_STATE() <> 0 ROLLBACK;
                REVERT;
                THROW;
            END CATCH;
            SELECT 1 AS [Value];
            """).ToListAsync();
        Assert.Equal(1, Assert.Single(result));
        var denied = await db.Database.SqlQuery<int>($"""
            DECLARE @error int = 0;
            BEGIN TRY
                BEGIN TRANSACTION;
                DECLARE @account int;
                EXEC dbo.CreateVisitorWorkspaceAccount @WorkspaceId={Guid.Empty}, @Issuer={TestJwtIssuer.Issuer},
                    @Subject=N'auth0|forbidden-owner', @ProviderId={provider.Id}, @AccountId=@account OUTPUT;
                ROLLBACK;
            END TRY
            BEGIN CATCH
                SET @error = ERROR_NUMBER();
                IF XACT_STATE() <> 0 ROLLBACK;
            END CATCH;
            SELECT @error AS [Value];
            """).ToListAsync();
        Assert.Equal(51060, Assert.Single(denied));
        Assert.Equal(1, await db.AppAccounts.IgnoreQueryFilters().CountAsync());
    }

    private sealed class Profiles : HttpMessageHandler
    {
        public bool Verified { get; set; } = true;
        public bool WrongSubject { get; set; }
        private int requests;
        public int Requests => requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref requests);
            var token = new JsonWebToken(request.Headers.Authorization!.Parameter!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new
            {
                sub = WrongSubject ? "auth0|different-user" : token.Subject,
                email = "visitor@example.invalid", email_verified = Verified,
            }) });
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => State.Now;
    }

    private sealed class State : IAsyncDisposable
    {
        public static readonly DateTimeOffset Now = new(2030, 6, 12, 12, 0, 0, TimeSpan.Zero);
        private readonly HealthcareApiFactory original = new();
        private readonly TestJwtIssuer tokens = new();
        public Profiles Profiles { get; } = new();
        public WebApplicationFactory<Program> Factory { get; private set; } = null!;
        private bool verifiedDatabase;

        public static async Task<State> CreateAsync(bool enabled = true, int maximum = 100, int daily = 20, bool seed = true)
        {
            var state = new State();
            try
            {
                state.Factory = state.original.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                {
                    state.tokens.ConfigureServices(services);
                    services.Configure<VisitorWorkspaceOptions>(options =>
                    {
                        options.Enabled = enabled; options.MaximumWorkspaces = maximum; options.MaximumDailyWorkspaces = daily;
                    });
                    services.AddSingleton<TimeProvider>(new FixedClock());
                    services.AddHttpClient<Auth0EmailProfileClient>().ConfigurePrimaryHttpMessageHandler(() => state.Profiles);
                }));
                await using var scope = state.Factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<HealthcareDbContext>();
                Assert.Equal(@"(localdb)\MSSQLLocalDB", db.Database.GetDbConnection().DataSource);
                Assert.Equal(state.original.DatabaseName, db.Database.GetDbConnection().Database);
                state.verifiedDatabase = true;
                await db.Database.MigrateAsync();
                if (seed) await SeedAsync(db);
                return state;
            }
            catch { await state.DisposeAsync(); throw; }
        }

        public HttpClient Client(string subject)
        {
            var client = Factory.CreateClient();
            tokens.AuthorizeSubject(client, subject);
            return client;
        }

        public HealthcareDbContext Context(Guid workspaceId)
        {
            var options = new DbContextOptionsBuilder<HealthcareDbContext>().UseSqlServer(
                $@"Server=(localdb)\MSSQLLocalDB;Database={original.DatabaseName};Integrated Security=True;Encrypt=True;TrustServerCertificate=True").Options;
            var db = new HealthcareDbContext(options);
            db.SelectWorkspace(workspaceId);
            return db;
        }

        public async Task AssertOriginalOnlyAsync()
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HealthcareDbContext>();
            Assert.Single(await db.Workspaces.IgnoreQueryFilters().ToListAsync());
            Assert.Single(await db.AppAccounts.IgnoreQueryFilters().ToListAsync());
            Assert.Equal(2, await db.Patients.IgnoreQueryFilters().CountAsync());
            Assert.Equal(2, await db.Appointments.IgnoreQueryFilters().CountAsync());
            Assert.Equal(2, await db.Clinics.IgnoreQueryFilters().CountAsync());
            Assert.Equal(2, await db.Referrals.IgnoreQueryFilters().CountAsync());
        }

        private static async Task SeedAsync(HealthcareDbContext db)
        {
            var clinic = new Clinic { Name = "Cedar Family Health", Address = "42 Cedar Lane", TimeZoneId = "America/Chicago" };
            var otherClinic = new Clinic { Name = "Willow Therapy", Address = "8 Willow Road", TimeZoneId = "America/Chicago" };
            var patient = new Patient { FirstName = "Alex", LastName = "Morgan", DateOfBirth = new DateOnly(1990, 6, 1) };
            var unused = new Patient { FirstName = "Jamie", LastName = "Reed", DateOfBirth = new DateOnly(1985, 1, 1) };
            db.AddRange(clinic, otherClinic, patient, unused);
            await db.SaveChangesAsync();
            var provider = new Provider { ClinicId = clinic.Id, FirstName = "Beth", LastName = "Hart" };
            var otherProvider = new Provider { ClinicId = otherClinic.Id, FirstName = "James", LastName = "Carter" };
            var service = new ClinicalService { ClinicId = clinic.Id, Name = "Physical exam", AllowsDirectBooking = true };
            var otherService = new ClinicalService { ClinicId = otherClinic.Id, Name = "Therapy", AllowsDirectBooking = false };
            db.AddRange(provider, otherProvider, service, otherService);
            await db.SaveChangesAsync();
            db.ProviderServices.AddRange(
                new ProviderService { ClinicId = clinic.Id, ProviderId = provider.Id, ClinicalServiceId = service.Id },
                new ProviderService { ClinicId = otherClinic.Id, ProviderId = otherProvider.Id, ClinicalServiceId = otherService.Id });
            foreach (var person in new[] { provider, otherProvider })
                for (var day = 0; day < 7; day++) db.ProviderWeeklyHours.Add(ProviderWeeklyHours.CreateDefault(person.Id, day));
            var owner = new AppAccount { Issuer = TestJwtIssuer.Issuer, Subject = "auth0|operator", IsAdministrator = true, ProviderId = provider.Id };
            db.AppAccounts.Add(owner);
            await db.SaveChangesAsync();
            db.ClinicMemberships.Add(new ClinicMembership { AppAccountId = owner.Id, ClinicId = clinic.Id, Role = ClinicStaffRole.Clinician });
            var referral = new Referral
            {
                PatientId = patient.Id, ClinicalServiceId = service.Id, AssignedProviderId = provider.Id,
                ReferringProviderName = "Beth Hart", Reason = "Annual health review", Status = ReferralStatus.ReadyToSchedule,
                CreatedAtUtc = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero),
            };
            db.Referrals.Add(referral);
            db.Referrals.Add(new Referral
            {
                PatientId = patient.Id, ClinicalServiceId = otherService.Id, AssignedProviderId = otherProvider.Id,
                ReferringProviderName = "Beth Hart", Reason = "Review care plan", Status = ReferralStatus.MissingInformation,
                MissingInformationNote = "Please supply the previous treatment plan.", CreatedAtUtc = referral.CreatedAtUtc,
            });
            await db.SaveChangesAsync();
            var start = new DateTimeOffset(2025, 1, 6, 14, 0, 0, TimeSpan.Zero); // Monday, 08:00 in Chicago.
            db.Appointments.AddRange(
                new Appointment { PatientId = patient.Id, ProviderId = provider.Id, ClinicalServiceId = service.Id,
                    ReferralId = referral.Id, StartUtc = start, BookedByAppAccountId = owner.Id, CreatedAtUtc = referral.CreatedAtUtc },
                new Appointment { PatientId = patient.Id, ProviderId = provider.Id, ClinicalServiceId = service.Id,
                    StartUtc = start.AddDays(-28), Status = AppointmentStatus.Completed, BookedByAppAccountId = owner.Id, CreatedAtUtc = referral.CreatedAtUtc.AddDays(-30) });
            db.ProviderTimeOff.Add(new ProviderTimeOff { ProviderId = provider.Id, StartUtc = start, EndUtc = start.AddMinutes(30) });
            await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (verifiedDatabase && Factory is not null)
                {
                    await using var scope = Factory.Services.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<HealthcareDbContext>().Database.EnsureDeletedAsync();
                }
            }
            finally
            {
                if (Factory is not null) await Factory.DisposeAsync();
                await original.DisposeAsync();
                tokens.Dispose();
                Profiles.Dispose();
            }
        }
    }
}
