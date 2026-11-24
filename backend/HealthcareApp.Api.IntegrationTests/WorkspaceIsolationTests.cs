using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HealthcareApp.Api.Data;
using HealthcareApp.Api.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HealthcareApp.Api.IntegrationTests;

public class WorkspaceIsolationTests
{
    [Fact]
    public async Task ProtectedDirectory_WithoutSubject_CannotSelectAnyWorkspace()
    {
        using var tokens = new TestJwtIssuer();
        await using var original = new HealthcareApiFactory();
        await using var factory = tokens.CreateFactory(original);
        using var client = factory.CreateClient();
        tokens.AuthorizeSubject(client, null, "read:patients");
        using var response = await client.GetAsync("/api/patients");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void WorkspaceModel_FiltersRelationshipsAndWriteGuardsAreConsistent()
    {
        var options = new DbContextOptionsBuilder<HealthcareDbContext>()
            .UseSqlServer("Server=localhost;Database=WorkspaceModelCheck;Integrated Security=True;TrustServerCertificate=True")
            .Options;
        using var first = new HealthcareDbContext(options);
        using var second = new HealthcareDbContext(options);
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        first.SelectWorkspace(firstId);
        second.SelectWorkspace(secondId);

        var records = first.Model.GetEntityTypes()
            .Where(entity => typeof(IWorkspaceRecord).IsAssignableFrom(entity.ClrType)).ToArray();
        Assert.Equal(11, records.Length);
        foreach (var record in records)
        {
            Assert.NotEmpty(record.GetDeclaredQueryFilters());
            foreach (var foreignKey in record.GetForeignKeys())
            {
                Assert.Contains(foreignKey.Properties, property => property.Name == nameof(IWorkspaceRecord.WorkspaceId));
                if (foreignKey.PrincipalEntityType.ClrType != typeof(Workspace))
                    Assert.Contains(foreignKey.PrincipalKey.Properties, property => property.Name == nameof(IWorkspaceRecord.WorkspaceId));
            }
        }

        // SQL generation uses each context's workspace, rather than caching the first one.
        Assert.Contains(firstId.ToString(), first.Patients.ToQueryString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(secondId.ToString(), second.Patients.ToQueryString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(firstId.ToString(), second.Patients.ToQueryString(), StringComparison.OrdinalIgnoreCase);
        Assert.Throws<InvalidOperationException>(() => first.SelectWorkspace(secondId));
        first.Patients.Add(new Patient { WorkspaceId = secondId, FirstName = "Wrong", LastName = "Workspace" });
        Assert.Throws<InvalidOperationException>(() => first.SaveChanges());
    }

    [Fact]
    public async Task DirectoriesSearchesAndCounts_IgnoreClientWorkspaceOverrides()
    {
        await using var state = await TestState.CreateAsync();
        using var client = state.Client(state.First);
        client.DefaultRequestHeaders.Add("X-Workspace-Id", state.Second.WorkspaceId.ToString());
        using var patients = await client.GetAsync("/api/patients?workspaceId=" + state.Second.WorkspaceId);
        Assert.Equal(HttpStatusCode.OK, patients.StatusCode);
        var list = await patients.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(state.First.Patient.PublicId, Assert.Single(list.EnumerateArray()).GetProperty("publicId").GetGuid());

        foreach (var path in new[] { "/api/clinics/search", "/api/patients/search", "/api/referrals/search",
            "/api/appointments/upcoming/search", "/api/appointments/needs-attention/search" })
        {
            using var response = await client.PostAsJsonAsync(path, new { page = 1, workspaceId = state.Second.WorkspaceId });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(body.GetProperty("totalCount").GetInt32() == 1,
                $"Expected one workspace-owned result from {path}: {body.GetRawText()}");
            Assert.Single(body.GetProperty("items").EnumerateArray());
            Assert.DoesNotContain(state.Second.Patient.FirstName, body.GetRawText());
        }

        using var count = await client.GetAsync("/api/appointments/needs-attention/count");
        Assert.Equal(1, (await count.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("count").GetInt32());

        using var created = await client.PostAsJsonAsync("/api/patients", new
        {
            firstName = "New", lastName = "Patient", dateOfBirth = "1990-01-01", workspaceId = state.Second.WorkspaceId,
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        await using var scope = state.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthcareDbContext>();
        db.SelectWorkspace(state.First.WorkspaceId);
        Assert.Equal(2, await db.Patients.CountAsync());
        Assert.Equal(1, await db.Patients.IgnoreQueryFilters().CountAsync(row => row.WorkspaceId == state.Second.WorkspaceId));
        Assert.Equal(1, await db.Patients.IgnoreQueryFilters().CountAsync(row => row.WorkspaceId == Guid.Empty));

        // Existing global grants stay confined to the original dataset as well.
        using var legacy = state.Client();
        var original = await legacy.GetFromJsonAsync<JsonElement>("/api/patients");
        Assert.Equal("Original", Assert.Single(original.EnumerateArray()).GetProperty("firstName").GetString());
    }

    [Fact]
    public async Task ForeignPublicIds_CannotBeReadChangedDeletedAssignedOrBooked()
    {
        await using var state = await TestState.CreateAsync();
        using var client = state.Client(state.First);
        foreach (var path in new[] {
            "/api/patients/" + state.Second.Patient.PublicId,
            "/api/clinics/" + state.Second.Clinic.PublicId,
            "/api/providers/" + state.Second.Provider.PublicId,
            "/api/services/" + state.Second.Service.PublicId,
            "/api/referrals/" + state.Second.Referral.PublicId,
            "/api/appointments/" + state.Second.Appointment.PublicId,
            "/api/providers/" + state.Second.Provider.PublicId + "/services",
        })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        using var update = await client.PutAsJsonAsync("/api/patients/" + state.Second.Patient.PublicId,
            new { firstName = "Changed", lastName = "Person", dateOfBirth = "1991-02-01" });
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        using var delete = await client.DeleteAsync("/api/patients/" + state.Second.Patient.PublicId);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        using var assign = await client.PostAsJsonAsync("/api/providers/" + state.First.Provider.PublicId + "/services",
            new { clinicalServicePublicId = state.Second.Service.PublicId });
        Assert.Equal(HttpStatusCode.NotFound, assign.StatusCode);
        using var booking = await client.PostAsJsonAsync("/api/appointments", new
        {
            patientPublicId = state.First.Patient.PublicId, clinicalServicePublicId = state.Second.Service.PublicId,
            providerPublicId = state.Second.Provider.PublicId, startUtc = state.First.Appointment.StartUtc.AddHours(2),
        });
        Assert.Equal(HttpStatusCode.NotFound, booking.StatusCode);

        using var owner = state.Client(state.Second);
        var untouched = await owner.GetFromJsonAsync<JsonElement>("/api/patients/" + state.Second.Patient.PublicId);
        Assert.Equal(state.Second.Patient.FirstName, untouched.GetProperty("firstName").GetString());
    }

    [Fact]
    public async Task PersonalListsCalendarAndAvailability_StayInTheAccountWorkspace()
    {
        await using var state = await TestState.CreateAsync();
        using var client = state.Client(state.First);
        var referrals = await client.GetFromJsonAsync<JsonElement>("/api/me/referrals");
        Assert.Equal(state.First.Referral.PublicId, Assert.Single(referrals.EnumerateArray()).GetProperty("publicId").GetGuid());
        var appointments = await client.GetFromJsonAsync<JsonElement>("/api/me/appointments");
        var expectedVisits = new HashSet<Guid> { state.First.Appointment.PublicId, state.First.RegularAppointment.PublicId };
        Assert.Equal(2, appointments.GetArrayLength());
        Assert.True(expectedVisits.SetEquals(appointments.EnumerateArray().Select(item => item.GetProperty("publicId").GetGuid())));
        var calendar = await client.GetFromJsonAsync<JsonElement>("/api/me/calendar");
        var visits = calendar.GetProperty("days").EnumerateArray()
            .SelectMany(day => day.GetProperty("appointments").EnumerateArray()).ToArray();
        Assert.Equal(2, visits.Length);
        Assert.True(expectedVisits.SetEquals(visits.Select(item => item.GetProperty("publicId").GetGuid())));
        Assert.DoesNotContain(state.Second.Patient.FirstName, calendar.GetRawText());

        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(state.First.Appointment.StartUtc, zone).DateTime);
        using var ownAvailability = await client.GetAsync($"/api/providers/{state.First.Provider.PublicId}/availability?date={date:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, ownAvailability.StatusCode);
        using var foreignAvailability = await client.GetAsync($"/api/providers/{state.Second.Provider.PublicId}/availability?date={date:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.NotFound, foreignAvailability.StatusCode);
    }

    [Fact]
    public async Task CrossWorkspaceRelationshipsAndOwnershipChanges_AreRejected()
    {
        await using var state = await TestState.CreateAsync();
        await using (var scope = state.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthcareDbContext>();
            db.SelectWorkspace(state.First.WorkspaceId);
            db.Referrals.Add(new Referral
            {
                PatientId = state.Second.Patient.Id, ClinicalServiceId = state.First.Service.Id,
                AssignedProviderId = state.First.Provider.Id, ReferringProviderName = "Receiving clinic",
                Reason = "Assessment requested.",
            });
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(547, Assert.IsType<SqlException>(error.InnerException).Number);
        }

        await using (var scope = state.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthcareDbContext>();
            db.SelectWorkspace(state.First.WorkspaceId);
            var patient = await db.Patients.SingleAsync();
            patient.WorkspaceId = state.Second.WorkspaceId;
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
    }

    private sealed record Dataset(Guid WorkspaceId, string Subject, Clinic Clinic, Patient Patient,
        Provider Provider, ClinicalService Service, Referral Referral, Appointment Appointment, Appointment RegularAppointment);

    private sealed class TestState : IAsyncDisposable
    {
        private readonly TestJwtIssuer tokens = new();
        private readonly HealthcareApiFactory original = new();
        private bool verifiedDatabase;
        public WebApplicationFactory<Program> Factory { get; }
        public Dataset First { get; private set; } = null!;
        public Dataset Second { get; private set; } = null!;
        private TestState() { Factory = tokens.CreateFactory(original); }

        public static async Task<TestState> CreateAsync()
        {
            var state = new TestState();
            try
            {
                using var startup = state.Factory.CreateClient();
                await using var scope = state.Factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<HealthcareDbContext>();
                Assert.Equal(@"(localdb)\MSSQLLocalDB", db.Database.GetDbConnection().DataSource);
                Assert.Equal(state.original.DatabaseName, db.Database.GetDbConnection().Database);
                state.verifiedDatabase = true;
                await db.Database.MigrateAsync();
                var first = Guid.NewGuid();
                var second = Guid.NewGuid();
                db.Workspaces.AddRange(new Workspace { Id = first }, new Workspace { Id = second });
                db.Patients.Add(new Patient { FirstName = "Original", LastName = "Dataset", DateOfBirth = new(1990, 1, 1) });
                await db.SaveChangesAsync();
                state.First = await state.SeedAsync(first, "A");
                state.Second = await state.SeedAsync(second, "B");
                return state;
            }
            catch { await state.DisposeAsync(); throw; }
        }

        private async Task<Dataset> SeedAsync(Guid workspaceId, string suffix)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HealthcareDbContext>();
            db.SelectWorkspace(workspaceId);
            var clinic = new Clinic { Name = "Clinic " + suffix, Address = "100 Cedar Lane", TimeZoneId = "America/Chicago" };
            var patient = new Patient { FirstName = "Patient" + suffix, LastName = "Morgan", DateOfBirth = new(1990, 1, 1) };
            db.AddRange(clinic, patient);
            await db.SaveChangesAsync();
            var provider = new Provider { ClinicId = clinic.Id, FirstName = "Provider", LastName = suffix };
            var service = new ClinicalService { ClinicId = clinic.Id, Name = "Physical exam", AllowsDirectBooking = true };
            db.AddRange(provider, service);
            await db.SaveChangesAsync();
            var account = new AppAccount
            {
                Issuer = TestJwtIssuer.Issuer, Subject = "auth0|workspace-" + suffix, IsAdministrator = true,
                ProviderId = provider.Id, PatientId = patient.Id,
            };
            db.AppAccounts.Add(account);
            db.ProviderServices.Add(new ProviderService { ClinicId = clinic.Id, ProviderId = provider.Id, ClinicalServiceId = service.Id });
            await db.SaveChangesAsync();
            db.ClinicMemberships.Add(new ClinicMembership { AppAccountId = account.Id, ClinicId = clinic.Id, Role = ClinicStaffRole.Clinician });
            db.ProviderWeeklyHours.Add(ProviderWeeklyHours.CreateDefault(provider.Id, 1));
            var referral = new Referral { PatientId = patient.Id, ClinicalServiceId = service.Id,
                AssignedProviderId = provider.Id, ReferringProviderName = "Clinic intake", Reason = "Assessment requested.",
                Status = ReferralStatus.ReadyToSchedule };
            db.Referrals.Add(referral);
            await db.SaveChangesAsync();
            var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
            var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime).AddDays(1);
            while (!ProviderWeeklyHours.IsWeekday((int)date.DayOfWeek)) date = date.AddDays(1);
            var local = DateTime.SpecifyKind(date.ToDateTime(new TimeOnly(8, 0)), DateTimeKind.Unspecified);
            var start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone));
            var appointment = new Appointment { PatientId = patient.Id, ClinicalServiceId = service.Id,
                ProviderId = provider.Id, ReferralId = referral.Id, BookedByAppAccountId = account.Id, StartUtc = start };
            // Upcoming visits exclude conflicts, so keep a regular visit outside time off as well.
            var regularAppointment = new Appointment { PatientId = patient.Id, ClinicalServiceId = service.Id,
                ProviderId = provider.Id, BookedByAppAccountId = account.Id, StartUtc = start.AddHours(1) };
            db.Appointments.AddRange(appointment, regularAppointment);
            db.ProviderTimeOff.Add(new ProviderTimeOff { ProviderId = provider.Id, StartUtc = start, EndUtc = start.AddMinutes(30) });
            await db.SaveChangesAsync();
            return new(workspaceId, account.Subject, clinic, patient, provider, service, referral, appointment, regularAppointment);
        }

        public HttpClient Client(Dataset? dataset = null)
        {
            var client = Factory.CreateClient();
            tokens.AuthorizeSubject(client, dataset?.Subject ?? "auth0|legacy-global", "read:clinics", "manage:clinics", "read:patients", "manage:patients",
                "read:providers", "manage:providers", "read:services", "manage:services", "read:provider-services",
                "manage:provider-services", "read:referrals", "manage:referrals", "read:appointments");
            return client;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (verifiedDatabase)
                {
                    await using var scope = Factory.Services.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<HealthcareDbContext>().Database.EnsureDeletedAsync();
                }
            }
            finally { await Factory.DisposeAsync(); await original.DisposeAsync(); tokens.Dispose(); }
        }
    }
}
