using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HealthcareApp.Api.Data;
using HealthcareApp.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HealthcareApp.Api.IntegrationTests;

public class ProviderCalendarTests
{
    [Fact]
    public async Task Calendar_DayOffPreservesAppointmentsAndExistingTimeOff()
    {
        const string url = "/api/me/calendar";
        const string queueUrl = "/api/appointments/needs-attention";
        const string subject = "auth0|calendar-provider";
        const string frontDeskSubject = "auth0|calendar-front-desk";
        const string otherFrontDeskSubject = "auth0|calendar-other-front-desk";
        var clock = new CalendarClock(new DateTimeOffset(2030, 1, 7, 5, 0, 0, TimeSpan.Zero));

        using var tokens = new TestJwtIssuer();
        await using var factory = new HealthcareApiFactory();
        await using var authFactory = tokens.CreateFactory(factory);
        await using var configured = authFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(clock)));
        using var client = configured.CreateClient();
        await using var scope = configured.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthcareDbContext>();
        var connection = db.Database.GetDbConnection();
        Assert.Equal(@"(localdb)\MSSQLLocalDB", connection.DataSource);
        Assert.Equal(factory.DatabaseName, connection.Database);

        try
        {
            await db.Database.MigrateAsync();
            var clinic = new Clinic
            {
                Name = "Calendar Clinic", TimeZoneId = "America/Chicago",
                Address = "100 Test Avenue",
            };
            var patient = new Patient { FirstName = "Taylor", LastName = "Demo", DateOfBirth = new DateOnly(1990, 1, 1) };
            var otherClinic = new Clinic
            {
                Name = "Other Calendar Clinic", TimeZoneId = "America/Chicago",
                Address = "100 Test Avenue",
            };
            db.AddRange(clinic, otherClinic, patient);
            await db.SaveChangesAsync();

            var service = new ClinicalService { Name = "Physical Exam", ClinicId = clinic.Id };
            db.ClinicalServices.Add(service);
            await db.SaveChangesAsync();
            var provider = await ReferralTestData.AddEligibleProviderAsync(db, service);

            var account = new AppAccount { Issuer = TestJwtIssuer.Issuer, Subject = subject, ProviderId = provider.Id };
            var frontDesk = new AppAccount { Issuer = TestJwtIssuer.Issuer, Subject = frontDeskSubject };
            var otherFrontDesk = new AppAccount { Issuer = TestJwtIssuer.Issuer, Subject = otherFrontDeskSubject };
            db.AppAccounts.AddRange(account, frontDesk, otherFrontDesk);
            await db.SaveChangesAsync();
            db.ClinicMemberships.AddRange(
                new ClinicMembership { AppAccountId = frontDesk.Id, ClinicId = clinic.Id, Role = ClinicStaffRole.FrontDesk },
                new ClinicMembership { AppAccountId = otherFrontDesk.Id, ClinicId = otherClinic.Id, Role = ClinicStaffRole.FrontDesk }
            );

            var appointment = new Appointment
            {
                ProviderId = provider.Id, PatientId = patient.Id, ClinicalServiceId = service.Id,
                StartUtc = new DateTimeOffset(2030, 1, 7, 15, 0, 0, TimeSpan.Zero),
                Status = AppointmentStatus.Confirmed, BookedByAppAccountId = account.Id,
            };
            var partial = new ProviderTimeOff
            {
                ProviderId = provider.Id,
                StartUtc = appointment.StartUtc.AddHours(1),
                EndUtc = appointment.StartUtc.AddMinutes(90),
            };
            db.AddRange(appointment, partial, new ClinicMembership
            {
                AppAccountId = account.Id, ClinicId = clinic.Id, Role = ClinicStaffRole.Clinician,
            });
            await db.SaveChangesAsync();

            using var anonymous = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

            tokens.AuthorizeSubject(client, "auth0|unlinked-calendar-user", "read:referrals");
            using var denied = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using var deniedWrite = await client.PostAsync(url + "/2030-01-07/time-off", null);
            Assert.Equal(HttpStatusCode.Forbidden, deniedWrite.StatusCode);

            tokens.AuthorizeSubject(client, subject);
            using var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            Assert.True(response.Headers.CacheControl?.NoStore);
            var calendar = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("2030-01-06", calendar.GetProperty("today").GetString());
            Assert.Equal(90, calendar.GetProperty("days").GetArrayLength());
            var openDay = GetDay(calendar, "2030-01-07");
            Assert.Equal(14, openDay.GetProperty("openSlots").GetInt32());
            var slots = openDay.GetProperty("availableSlots");
            Assert.Equal(14, slots.GetArrayLength());
            Assert.DoesNotContain(slots.EnumerateArray(), slot =>
                slot.GetProperty("startUtc").GetDateTimeOffset() == appointment.StartUtc);
            var scheduled = Assert.Single(openDay.GetProperty("appointments").EnumerateArray());
            Assert.Equal(appointment.PublicId, scheduled.GetProperty("publicId").GetGuid());
            Assert.Equal("Taylor Demo", scheduled.GetProperty("patientName").GetString());

            foreach (var weekend in new[] { "2030-01-06", "2030-01-12" })
            {
                var weekendDay = GetDay(calendar, weekend);
                Assert.True(weekendDay.GetProperty("canMarkUnavailable").GetBoolean());
                Assert.Empty(weekendDay.GetProperty("availableSlots").EnumerateArray());

                using var weekendOff = await client.PostAsync($"{url}/{weekend}/time-off", null);
                Assert.Equal(HttpStatusCode.NoContent, weekendOff.StatusCode);
                var updatedWeekend = GetDay(await client.GetFromJsonAsync<JsonElement>(url), weekend);
                Assert.True(updatedWeekend.GetProperty("dayOff").GetBoolean());
                var weekendBlock = updatedWeekend.GetProperty("dayOffPublicId").GetGuid();
                using var removed = await client.DeleteAsync($"/api/me/time-off/{weekendBlock}");
                Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
            }

            using var invalid = await client.PostAsync(url + "/2030-01-05/time-off", null);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            using var blocked = await client.PostAsync(url + "/2030-01-07/time-off", null);
            Assert.Equal(HttpStatusCode.NoContent, blocked.StatusCode);
            using var repeated = await client.PostAsync(url + "/2030-01-07/time-off", null);
            Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
            Assert.Equal(2, await db.ProviderTimeOff.CountAsync());

            var blockedDay = GetDay(await client.GetFromJsonAsync<JsonElement>(url), "2030-01-07");
            Assert.True(blockedDay.GetProperty("dayOff").GetBoolean());
            Assert.Equal(0, blockedDay.GetProperty("openSlots").GetInt32());
            Assert.True(blockedDay.GetProperty("appointments")[0].GetProperty("duringTimeOff").GetBoolean());

            using var clinicianQueue = await client.GetAsync(queueUrl);
            Assert.Equal(HttpStatusCode.Forbidden, clinicianQueue.StatusCode);

            tokens.AuthorizeSubject(client, otherFrontDeskSubject);
            Assert.Empty((await client.GetFromJsonAsync<JsonElement>(queueUrl)).EnumerateArray());

            tokens.AuthorizeSubject(client, frontDeskSubject);
            using var conflictResponse = await client.GetAsync(queueUrl);
            conflictResponse.EnsureSuccessStatusCode();
            Assert.True(conflictResponse.Headers.CacheControl?.NoStore);
            var conflicts = await conflictResponse.Content.ReadFromJsonAsync<JsonElement>();
            var conflict = Assert.Single(conflicts.EnumerateArray());
            Assert.Equal(appointment.PublicId, conflict.GetProperty("publicId").GetGuid());
            Assert.Equal(clinic.PublicId, conflict.GetProperty("clinicPublicId").GetGuid());

            tokens.AuthorizeSubject(client, subject);
            var blockId = blockedDay.GetProperty("dayOffPublicId").GetGuid();
            using var reopened = await client.DeleteAsync($"/api/me/time-off/{blockId}");
            Assert.Equal(HttpStatusCode.NoContent, reopened.StatusCode);
            var restored = GetDay(await client.GetFromJsonAsync<JsonElement>(url), "2030-01-07");
            Assert.Equal(14, restored.GetProperty("openSlots").GetInt32());
            Assert.Equal(appointment.PublicId, restored.GetProperty("appointments")[0].GetProperty("publicId").GetGuid());
            Assert.Equal(partial.PublicId, await db.ProviderTimeOff.Select(block => block.PublicId).SingleAsync());

            tokens.AuthorizeSubject(client, frontDeskSubject);
            Assert.Empty((await client.GetFromJsonAsync<JsonElement>(queueUrl)).EnumerateArray());
            tokens.AuthorizeSubject(client, subject);

            // At 1:21 p.m. clinic time, the first afternoon slot has started.
            clock.Now = new DateTimeOffset(2030, 1, 7, 19, 21, 0, TimeSpan.Zero);
            var today = GetDay(await client.GetFromJsonAsync<JsonElement>(url), "2030-01-07");
            Assert.Equal(7, today.GetProperty("openSlots").GetInt32());

            var started = today.GetProperty("startedSlots");
            Assert.Equal(7, started.GetArrayLength());
            Assert.True(started[0].GetProperty("hasEnded").GetBoolean());
            var currentSlot = started[started.GetArrayLength() - 1];
            Assert.Equal(new DateTimeOffset(2030, 1, 7, 19, 0, 0, TimeSpan.Zero),
                currentSlot.GetProperty("startUtc").GetDateTimeOffset());
            Assert.False(currentSlot.GetProperty("hasEnded").GetBoolean());
            Assert.DoesNotContain(started.EnumerateArray(), slot =>
                slot.GetProperty("startUtc").GetDateTimeOffset() == appointment.StartUtc);
            Assert.Equal(appointment.PublicId, today.GetProperty("appointments")[0].GetProperty("publicId").GetGuid());

            // Today's open time still appears in the provider calendar, but cannot be booked.
            using var availability = await client.GetAsync(
                $"/api/providers/{provider.PublicId}/availability?date=2030-01-07");
            Assert.Equal(HttpStatusCode.BadRequest, availability.StatusCode);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static JsonElement GetDay(JsonElement calendar, string date) =>
        Assert.Single(calendar.GetProperty("days").EnumerateArray(), day => day.GetProperty("date").GetString() == date);

    private sealed class CalendarClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
