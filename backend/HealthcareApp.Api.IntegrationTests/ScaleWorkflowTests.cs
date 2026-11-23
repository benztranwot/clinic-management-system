using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HealthcareApp.Api.Data;
using HealthcareApp.Api.Models;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace HealthcareApp.Api.IntegrationTests;

[Collection(ScaleMeasurementCollection.Name)]
public class ScaleWorkflowTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "Scale")]
    public async Task ScaleDataset_SeedIsRepeatableAndQueuesSearchAndCalendarStayCorrect()
    {
        var now = DateTimeOffset.UtcNow;
        using var tokens = new TestJwtIssuer();
        await using var factory = new HealthcareApiFactory();
        await using var signedFactory = tokens.CreateFactory(factory);
        await using var configured = signedFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<TimeProvider>(new FixedClock(now));

                // Repeated scale measurements deliberately exceed normal user quotas.
                // Override only this host; RateLimitingTests verify production limits.
                services.PostConfigure<RateLimiterOptions>(options =>
                {
                    options.GlobalLimiter?.Dispose();
                    options.GlobalLimiter = null;
                });
            }));
        using var client = configured.CreateClient();
        await using var scope = configured.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthcareDbContext>();
        Assert.Equal(factory.DatabaseName, db.Database.GetDbConnection().Database);
        var seed = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "SeedScaleData.sql"));

        try
        {
            await db.Database.MigrateAsync();
            var existingPatient = new Patient
            {
                FirstName = "Existing", LastName = "Demo", DateOfBirth = new DateOnly(1990, 1, 1),
            };
            var existingAccount = new AppAccount
            {
                Issuer = "https://existing.example.invalid/", Subject = "existing-admin", IsAdministrator = true,
            };
            db.AddRange(existingPatient, existingAccount);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync(seed);
            var seededClinic = await db.Clinics.SingleAsync(item =>
                item.PublicId == Guid.Parse("55000001-0000-4000-8000-000000000001"));
            Assert.Equal("Maple Grove Family Health", seededClinic.Name);
            seededClinic.Name = "Scale Clinic 1";
            seededClinic.Address = "1 Fictional Avenue";
            var seededPatient = await db.Patients.SingleAsync(item =>
                item.PublicId == Guid.Parse("55000004-0000-4000-8000-000000000001"));
            Assert.Equal("Emma", seededPatient.FirstName);
            Assert.Equal("Carter", seededPatient.LastName);
            seededPatient.FirstName = "Fictional";
            seededPatient.LastName = "Patient 001";
            var seededService = await db.ClinicalServices.SingleAsync(item =>
                item.PublicId == Guid.Parse("55000003-0000-4000-8000-000000000001"));
            Assert.Equal("Primary Care Consultation", seededService.Name);
            seededService.Name = "Demo Service 01";
            var legacyProvider = await db.Providers.SingleAsync(item =>
                item.PublicId == Guid.Parse("55000002-0000-4000-8000-000000000002"));
            legacyProvider.FirstName = "Fictional";
            legacyProvider.LastName = "Provider 02";
            var legacyReferral = await db.Referrals.SingleAsync(item =>
                item.PublicId == Guid.Parse("55000005-0000-4000-8000-000000000121"));
            legacyReferral.ReferringProviderName = "Fictional Referring Provider";
            legacyReferral.Reason = "Fictional demo referral 121";
            legacyReferral.MissingInformationNote = "Fictional supporting information needed.";
            var editedProvider = await db.Providers.SingleAsync(item =>
                item.PublicId == Guid.Parse("55000002-0000-4000-8000-000000000001"));
            editedProvider.FirstName = "Edited";
            await db.SaveChangesAsync();
            var firstStarts = await db.Appointments.AsNoTracking().OrderBy(item => item.PublicId)
                .Select(item => new { item.PublicId, item.StartUtc }).ToArrayAsync();
            await db.Database.ExecuteSqlRawAsync(seed);

            await db.Entry(seededClinic).ReloadAsync();
            Assert.Equal("Maple Grove Family Health", seededClinic.Name);
            Assert.Equal("1420 Willow Lane", seededClinic.Address);
            await db.Entry(seededPatient).ReloadAsync();
            Assert.Equal("Emma", seededPatient.FirstName);
            Assert.Equal("Carter", seededPatient.LastName);
            await db.Entry(seededService).ReloadAsync();
            Assert.Equal("Primary Care Consultation", seededService.Name);
            await db.Entry(legacyProvider).ReloadAsync();
            Assert.Equal("Daniel", legacyProvider.FirstName);
            Assert.Equal("Brooks", legacyProvider.LastName);
            await db.Entry(legacyReferral).ReloadAsync();
            Assert.Equal("Dr. Benjamin Ellis", legacyReferral.ReferringProviderName);
            Assert.Equal("Assess recent mood changes and their impact on daily functioning.", legacyReferral.Reason);
            Assert.Equal("Please include the referral summary and relevant treatment history.", legacyReferral.MissingInformationNote);
            var preservedPatient = await db.Patients.AsNoTracking().SingleAsync(item => item.Id == existingPatient.Id);
            Assert.Equal("Existing", preservedPatient.FirstName);
            Assert.Equal("Demo", preservedPatient.LastName);
            var preservedAccount = await db.AppAccounts.AsNoTracking().SingleAsync(item => item.Id == existingAccount.Id);
            Assert.Equal(existingAccount.Issuer, preservedAccount.Issuer);
            Assert.Equal(existingAccount.Subject, preservedAccount.Subject);
            Assert.True(preservedAccount.IsAdministrator);
            Assert.Equal("Edited", await db.Providers.Where(item => item.Id == editedProvider.Id)
                .Select(item => item.FirstName).SingleAsync());
            db.Patients.Remove(existingPatient);
            await db.SaveChangesAsync();

            Assert.Equal(5, await db.Clinics.CountAsync());
            Assert.Equal(25, await db.Providers.CountAsync());
            Assert.Equal(20, await db.ClinicalServices.CountAsync());
            Assert.Equal(250, await db.Patients.CountAsync());
            Assert.Equal(300, await db.Referrals.CountAsync());
            Assert.Equal(1000, await db.Appointments.CountAsync());
            Assert.Equal(100, await db.ProviderServices.CountAsync());
            Assert.Equal(firstStarts, await db.Appointments.AsNoTracking().OrderBy(item => item.PublicId)
                .Select(item => new { item.PublicId, item.StartUtc }).ToArrayAsync());

            tokens.AuthorizeSubject(client, "auth0|scale-admin", "read:referrals");
            var queue = await MeasureAsync("All-clinic referral queue", () => client.GetAsync("/api/referrals"));
            Assert.Equal(300, queue.GetArrayLength());
            Assert.Equal(300, queue.EnumerateArray().Select(item => item.GetProperty("publicId").GetGuid()).Distinct().Count());
            Assert.Equal(60, queue.EnumerateArray().Count(item => item.GetProperty("stage").GetString() == "Scheduled"));
            Assert.Equal(60, queue.EnumerateArray().Count(item => item.GetProperty("stage").GetString() == "Closed"));
            Assert.Equal(60, queue.EnumerateArray().Count(item => item.GetProperty("status").GetString() == "MissingInformation"));

            var clinicId = Guid.Parse("55000001-0000-4000-8000-000000000001");
            var clinicQueue = await MeasureAsync("Single-clinic referral queue", () =>
                client.GetAsync($"/api/clinics/{clinicId}/referrals"));
            var expectedClinicReferrals = await (
                from referral in db.Referrals
                join service in db.ClinicalServices on referral.ClinicalServiceId equals service.Id
                join clinic in db.Clinics on service.ClinicId equals clinic.Id
                where clinic.PublicId == clinicId
                orderby referral.CreatedAtUtc descending, referral.Id descending
                select referral.PublicId
            ).ToArrayAsync();
            Assert.Equal(expectedClinicReferrals, clinicQueue.EnumerateArray()
                .Select(item => item.GetProperty("publicId").GetGuid()));

            tokens.AuthorizeSubject(client, "auth0|scale-admin", "read:clinics", "read:patients", "read:referrals");
            var patientIds = await db.Patients.OrderBy(item => item.LastName).ThenBy(item => item.FirstName)
                .ThenBy(item => item.Id).Select(item => item.PublicId).ToArrayAsync();
            var patientPages = new List<Guid>();
            for (var page = 1; page <= 16; page++)
            {
                using var response = await client.PostAsJsonAsync("/api/patients/search", new { page });
                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(16, result.GetProperty("pageSize").GetInt32());
                Assert.Equal(page < 16 ? 16 : 10, PageIds(result).Length);
                Assert.Equal(250, result.GetProperty("totalCount").GetInt32());
                Assert.Equal(page < 16, result.GetProperty("hasMore").GetBoolean());
                patientPages.AddRange(PageIds(result));
            }
            Assert.Equal(patientIds, patientPages);
            using var exactPatient = await client.PostAsJsonAsync("/api/patients/search", new
            {
                seededPatient.FirstName, seededPatient.LastName, seededPatient.DateOfBirth,
            });
            exactPatient.EnsureSuccessStatusCode();
            var exactResult = await exactPatient.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(seededPatient.PublicId, Assert.Single(PageIds(exactResult)));

            using var clinicSearch = await client.PostAsJsonAsync("/api/clinics/search", new
            {
                name = "Maple Grove", address = "Willow", timeZone = seededClinic.TimeZoneId,
            });
            clinicSearch.EnsureSuccessStatusCode();
            var clinicResult = await clinicSearch.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(seededClinic.PublicId, Assert.Single(PageIds(clinicResult)));

            var referralPages = new List<Guid>();
            for (var page = 1; page <= 30; page++)
            {
                using var response = await client.PostAsJsonAsync("/api/referrals/search", new { page });
                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(300, result.GetProperty("totalCount").GetInt32());
                Assert.Equal(10, PageIds(result).Length);
                referralPages.AddRange(PageIds(result));
            }
            Assert.Equal(queue.EnumerateArray().Select(item => item.GetProperty("publicId").GetGuid()), referralPages);
            foreach (var stage in new[] { "PendingReview", "MissingInformation", "ReadyToSchedule", "Scheduled", "Closed" })
            {
                var matching = queue.EnumerateArray().Where(item => item.GetProperty(stage == "MissingInformation" ? "status" : "stage").GetString() == stage).ToArray();
                using var response = await client.PostAsJsonAsync("/api/referrals/search", new { stage });
                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(matching.Length, result.GetProperty("totalCount").GetInt32());
                Assert.Equal(matching.Take(10).Select(item => item.GetProperty("publicId").GetGuid()), PageIds(result));
            }
            using var clinicPage = await client.PostAsJsonAsync($"/api/clinics/{clinicId}/referrals/search", new { page = 2 });
            clinicPage.EnsureSuccessStatusCode();
            var clinicPageResult = await clinicPage.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(expectedClinicReferrals.Length, clinicPageResult.GetProperty("totalCount").GetInt32());
            Assert.Equal(expectedClinicReferrals.Skip(10).Take(10), PageIds(clinicPageResult));
            foreach (var path in new[] { "/api/clinics/search", "/api/patients/search", "/api/referrals/search" })
            {
                using var invalid = await client.PostAsJsonAsync(path, new { page = 0 });
                Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
                using var overflow = await client.PostAsJsonAsync(path, new { page = int.MaxValue });
                Assert.Equal(HttpStatusCode.BadRequest, overflow.StatusCode);
            }
            tokens.AuthorizeSubject(client, "auth0|scale-provider");
            using var deniedClinic = await client.PostAsJsonAsync("/api/clinics/55000001-0000-4000-8000-000000000005/referrals/search", new { });
            Assert.Equal(HttpStatusCode.Forbidden, deniedClinic.StatusCode);
            using var deniedDirectory = await client.PostAsJsonAsync("/api/patients/search", new { });
            Assert.Equal(HttpStatusCode.Forbidden, deniedDirectory.StatusCode);

            // Match the route's confirmed, upcoming, non-conflicting appointment contract.
            var expected = await (
                from appointment in db.Appointments
                join patient in db.Patients on appointment.PatientId equals patient.Id
                join service in db.ClinicalServices on appointment.ClinicalServiceId equals service.Id
                join clinic in db.Clinics on service.ClinicId equals clinic.Id
                join provider in db.Providers on appointment.ProviderId equals provider.Id
                where appointment.Status == AppointmentStatus.Confirmed && appointment.StartUtc > now
                    && !db.ProviderTimeOff.Any(block => block.ProviderId == appointment.ProviderId
                        && block.StartUtc < appointment.StartUtc.AddMinutes(Appointment.DurationMinutes)
                        && appointment.StartUtc < block.EndUtc)
                orderby appointment.StartUtc, appointment.Id
                select new
                {
                    appointment.PublicId,
                    appointment.StartUtc,
                    Clinic = clinic.Name,
                    Service = service.Name,
                    Provider = provider.FirstName + " " + provider.LastName,
                    Patient = patient.FirstName + " " + patient.LastName,
                }
            ).ToArrayAsync();
            Assert.True(expected.Length > 40, "The scale dataset must exercise several search pages.");
            tokens.AuthorizeSubject(client, "auth0|scale-front-desk");
            const string searchUrl = "/api/appointments/upcoming/search";
            var firstPage = await MeasureAsync("Appointment search page 1", () =>
                client.PostAsJsonAsync(searchUrl, new { page = 1 }));
            Assert.Equal(expected.Take(8).Select(item => item.PublicId), PageIds(firstPage));

            var secondPage = await MeasureAsync("Appointment search page 2", () =>
                client.PostAsJsonAsync(searchUrl, new { page = 2 }));
            Assert.Equal(expected.Skip(8).Take(8).Select(item => item.PublicId), PageIds(secondPage));

            var sample = expected[0];
            var until = sample.StartUtc.AddDays(1);
            var filtered = await MeasureAsync("Appointment search with all filters", () =>
                client.PostAsJsonAsync(searchUrl, new
                {
                    sample.Clinic, sample.Service, sample.Provider, sample.Patient,
                    fromUtc = sample.StartUtc, untilUtc = until, page = 1,
                }));
            var filteredIds = expected.Where(item => item.Clinic == sample.Clinic && item.Service == sample.Service
                    && item.Provider == sample.Provider && item.Patient == sample.Patient
                    && item.StartUtc >= sample.StartUtc && item.StartUtc < until)
                .Select(item => item.PublicId).ToArray();
            Assert.NotEmpty(filteredIds);
            Assert.Equal(filteredIds.Take(8), PageIds(filtered));
            Assert.Equal(filteredIds.Length > 8, filtered.GetProperty("hasMore").GetBoolean());

            var descending = await MeasureAsync("Appointment search newest first", () =>
                client.PostAsJsonAsync(searchUrl, new { latestFirst = true, page = 1 }));
            Assert.Equal(expected.Reverse().Take(8).Select(item => item.PublicId), PageIds(descending));

            var allPageIds = new List<Guid>();
            for (var page = 1; allPageIds.Count < expected.Length; page++)
            {
                using var response = await client.PostAsJsonAsync(searchUrl, new { page });
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var result = await response.Content.ReadFromJsonAsync<JsonElement>();
                var ids = PageIds(result);
                Assert.InRange(ids.Length, 1, 8);
                allPageIds.AddRange(ids);
                Assert.Equal(allPageIds.Count < expected.Length, result.GetProperty("hasMore").GetBoolean());
            }
            Assert.Equal(expected.Select(item => item.PublicId), allPageIds);
            using var pastLastPage = await client.PostAsJsonAsync(searchUrl, new { page = (expected.Length + 7) / 8 + 1 });
            Assert.Equal(HttpStatusCode.OK, pastLastPage.StatusCode);
            var emptyPage = await pastLastPage.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Empty(PageIds(emptyPage));
            Assert.False(emptyPage.GetProperty("hasMore").GetBoolean());

            var conflicts = await client.GetFromJsonAsync<JsonElement>("/api/appointments/needs-attention");
            var count = await client.GetFromJsonAsync<JsonElement>("/api/appointments/needs-attention/count");
            Assert.Equal(conflicts.GetArrayLength(), count.GetProperty("count").GetInt32());
            using var conflictResponse = await client.PostAsJsonAsync("/api/appointments/needs-attention/search", new { page = 1 });
            conflictResponse.EnsureSuccessStatusCode();
            var conflictPage = await conflictResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(conflicts.GetArrayLength(), conflictPage.GetProperty("totalCount").GetInt32());
            Assert.Equal(conflicts.EnumerateArray().Take(8).Select(item => item.GetProperty("publicId").GetGuid()), PageIds(conflictPage));
            using var noConflictMatch = await client.PostAsJsonAsync("/api/appointments/needs-attention/search", new { patient = "No such patient" });
            noConflictMatch.EnsureSuccessStatusCode();
            Assert.Empty(PageIds(await noConflictMatch.Content.ReadFromJsonAsync<JsonElement>()));

            tokens.AuthorizeSubject(client, "auth0|scale-provider");
            var calendar = await MeasureAsync("Provider 90-day calendar", () => client.GetAsync("/api/me/calendar"));
            var days = calendar.GetProperty("days");
            Assert.Equal(90, days.GetArrayLength());
            var providerId = await db.AppAccounts.Where(account => account.Subject == "auth0|scale-provider")
                .Select(account => account.ProviderId).SingleAsync();
            var expectedCalendarIds = await db.Appointments.Where(appointment => appointment.ProviderId == providerId
                    && appointment.Status == AppointmentStatus.Confirmed && appointment.StartUtc > now)
                .Select(appointment => appointment.PublicId).ToArrayAsync();
            var calendarIds = days.EnumerateArray().SelectMany(day => day.GetProperty("appointments").EnumerateArray())
                .Select(appointment => appointment.GetProperty("publicId").GetGuid()).ToArray();
            Assert.Equal(expectedCalendarIds.Order(), calendarIds.Order());
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private async Task<JsonElement> MeasureAsync(string name, Func<Task<HttpResponseMessage>> request)
    {
        const int repetitions = 10;
        var repeated = new List<double>();
        var first = 0d;
        var bytes = 0;
        var result = default(JsonElement);
        for (var iteration = 0; iteration <= repetitions; iteration++)
        {
            var stopwatch = Stopwatch.StartNew();
            using var response = await request();
            var body = await response.Content.ReadAsByteArrayAsync();
            stopwatch.Stop();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            if (iteration == 0) first = stopwatch.Elapsed.TotalMilliseconds;
            else repeated.Add(stopwatch.Elapsed.TotalMilliseconds);
            bytes = body.Length;
            using var document = JsonDocument.Parse(body);
            result = document.RootElement.Clone();
        }

        repeated.Sort();
        var median = (repeated[4] + repeated[5]) / 2;
        var p95 = repeated[(int)Math.Ceiling(repetitions * 0.95) - 1];
        output.WriteLine($"{name}: first {first:F2} ms; repeat median {median:F2} ms; repeat p95 {p95:F2} ms; {bytes} bytes; {repetitions} repeats.");
        return result;
    }

    private static Guid[] PageIds(JsonElement result) => result.GetProperty("items").EnumerateArray()
        .Select(item => item.GetProperty("publicId").GetGuid()).ToArray();

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
