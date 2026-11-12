using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HealthcareApp.Api.Data;
using HealthcareApp.Api.Dtos;
using HealthcareApp.Api.Models;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HealthcareApp.Api.IntegrationTests;

public class DirectAppointmentTests
{
    private const string AppointmentsUrl = "/api/appointments";
    private const string StartUtc = "2030-01-07T14:00:00Z";

    [Fact]
    public async Task BookDirect_WithoutToken_ReturnsUnauthorized()
    {
        await using var factory = new HealthcareApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            AppointmentsUrl,
            new CreateDirectAppointmentRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), StartUtc)
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task BookDirect_EnforcesPolicyAndSavesOneBookingForAContestedSlot()
    {
        const string subject = "auth0|booking-front-desk";
        const string clinicianSubject = "auth0|booking-clinician";
        var now = new DateTimeOffset(2030, 1, 7, 5, 30, 0, TimeSpan.Zero);
        var start = new DateTimeOffset(2030, 1, 7, 14, 0, 0, TimeSpan.Zero);

        var clock = new TestClock(now);
        using var tokens = new TestJwtIssuer();
        await using var factory = new HealthcareApiFactory();
        await using var signedFactory = tokens.CreateFactory(factory);
        await using var configuredFactory = signedFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<TimeProvider>(clock)
            )
        );
        using var client = configuredFactory.CreateClient();
        await using var scope = configuredFactory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthcareDbContext>();

        var connection = db.Database.GetDbConnection();
        Assert.Equal(@"(localdb)\MSSQLLocalDB", connection.DataSource);
        Assert.Equal(factory.DatabaseName, connection.Database);

        try
        {
            await db.Database.MigrateAsync();

            var clinic = new Clinic
            {
                Name = "Booking Test Clinic", TimeZoneId = "America/Chicago",
                Address = "100 Test Avenue",
            };
            var staff = new AppAccount { Issuer = TestJwtIssuer.Issuer, Subject = subject };
            var clinician = new AppAccount { Issuer = TestJwtIssuer.Issuer, Subject = clinicianSubject };
            var firstPatient = new Patient
            {
                FirstName = "Alex", LastName = "Demo", DateOfBirth = new DateOnly(1990, 3, 5),
            };
            var secondPatient = new Patient
            {
                FirstName = "Jamie", LastName = "Demo", DateOfBirth = new DateOnly(1992, 4, 7),
            };
            db.AddRange(clinic, staff, clinician, firstPatient, secondPatient);
            await db.SaveChangesAsync();

            var provider = new Provider { ClinicId = clinic.Id, FirstName = "Beth", LastName = "Demo" };
            var otherProvider = new Provider
            {
                ClinicId = clinic.Id, FirstName = "John", LastName = "Demo",
            };
            var physical = new ClinicalService
            {
                ClinicId = clinic.Id, Name = "Annual Physical", AllowsDirectBooking = true,
            };
            var referralOnly = new ClinicalService { ClinicId = clinic.Id, Name = "Therapy" };
            var unavailableService = new ClinicalService
            {
                ClinicId = clinic.Id, Name = "Vision Exam", AllowsDirectBooking = true,
            };
            db.AddRange(
                provider, otherProvider, physical, referralOnly, unavailableService,
                new ClinicMembership
                {
                    AppAccountId = staff.Id, ClinicId = clinic.Id, Role = ClinicStaffRole.FrontDesk,
                },
                new ClinicMembership
                {
                    AppAccountId = clinician.Id, ClinicId = clinic.Id, Role = ClinicStaffRole.Clinician,
                }
            );
            await db.SaveChangesAsync();

            db.AddRange(
                new ProviderService
                {
                    ClinicId = clinic.Id, ProviderId = provider.Id, ClinicalServiceId = physical.Id,
                },
                new ProviderService
                {
                    ClinicId = clinic.Id, ProviderId = otherProvider.Id, ClinicalServiceId = physical.Id,
                },
                new ProviderTimeOff
                {
                    ProviderId = provider.Id, StartUtc = start.AddMinutes(35), EndUtc = start.AddMinutes(45),
                }
            );
            await db.SaveChangesAsync();

            var request = new CreateDirectAppointmentRequest(
                firstPatient.PublicId, physical.PublicId, provider.PublicId, StartUtc
            );

            tokens.AuthorizeSubject(client, clinicianSubject);
            using var denied = await client.PostAsJsonAsync(AppointmentsUrl, request);
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);

            tokens.AuthorizeSubject(client, subject);
            clock.EnqueueReads(start.AddMinutes(-15));
            using var sameDay = await client.PostAsJsonAsync(AppointmentsUrl, request);
            Assert.Equal(HttpStatusCode.BadRequest, sameDay.StatusCode);

            using var referralRequired = await client.PostAsJsonAsync(
                AppointmentsUrl, request with { ClinicalServicePublicId = referralOnly.PublicId }
            );
            await AssertConflictAsync(referralRequired, "referral_required");

            using var ineligible = await client.PostAsJsonAsync(
                AppointmentsUrl, request with { ClinicalServicePublicId = unavailableService.PublicId }
            );
            Assert.Equal(HttpStatusCode.BadRequest, ineligible.StatusCode);

            using var offGrid = await client.PostAsJsonAsync(
                AppointmentsUrl, request with { StartUtc = "2030-01-07T14:15:00Z" }
            );
            Assert.Equal(HttpStatusCode.BadRequest, offGrid.StatusCode);

            using var timeOff = await client.PostAsJsonAsync(
                AppointmentsUrl, request with { StartUtc = "2030-01-07T14:30:00Z" }
            );
            await AssertConflictAsync(timeOff, "slot_unavailable");

            using var lunch = await client.PostAsJsonAsync(
                AppointmentsUrl, request with { StartUtc = "2030-01-07T18:00:00Z" }
            );
            await AssertConflictAsync(lunch, "slot_unavailable");

            using var weekend = await client.PostAsJsonAsync(
                AppointmentsUrl, request with { StartUtc = "2030-01-12T14:00:00Z" }
            );
            await AssertConflictAsync(weekend, "slot_unavailable");

            var responses = await Task.WhenAll(
                client.PostAsJsonAsync(AppointmentsUrl, request),
                client.PostAsJsonAsync(
                    AppointmentsUrl, request with { PatientPublicId = secondPatient.PublicId }
                )
            );
            using var created = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            using var conflict = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            await AssertConflictAsync(conflict, "slot_unavailable");

            var body = await created.Content.ReadFromJsonAsync<JsonElement>();
            var bookingUrl = $"{AppointmentsUrl}/{body.GetProperty("publicId").GetGuid()}";
            Assert.Equal(bookingUrl, created.Headers.Location?.OriginalString);
            Assert.Equal("Confirmed", body.GetProperty("status").GetString());
            Assert.Equal(start.AddMinutes(30), body.GetProperty("endUtc").GetDateTimeOffset());
            var winningPatient = body.GetProperty("patientPublicId").GetGuid();

            using var patientConflict = await client.PostAsJsonAsync(
                AppointmentsUrl,
                request with { PatientPublicId = winningPatient, ProviderPublicId = otherProvider.PublicId }
            );
            await AssertConflictAsync(patientConflict, "patient_already_booked");

            var detail = await client.GetFromJsonAsync<JsonElement>(bookingUrl);
            Assert.Equal(winningPatient, detail.GetProperty("patientPublicId").GetGuid());
            Assert.True(detail.GetProperty("canCancel").GetBoolean());
            Assert.False(detail.GetProperty("overlapsProviderTimeOff").GetBoolean());
            var upcoming = await client.GetFromJsonAsync<JsonElement>(AppointmentsUrl + "/upcoming");
            Assert.Equal(body.GetProperty("publicId").GetGuid(),
                Assert.Single(upcoming.EnumerateArray()).GetProperty("publicId").GetGuid());

            var searchUrl = AppointmentsUrl + "/upcoming/search";
            using var match = await client.PostAsJsonAsync(searchUrl, new
            {
                Clinic = detail.GetProperty("clinicName").GetString(),
                Service = detail.GetProperty("serviceName").GetString(),
                Provider = detail.GetProperty("providerName").GetString(),
                Patient = winningPatient == firstPatient.PublicId ? firstPatient.FirstName : secondPatient.FirstName,
                FromUtc = start,
                UntilUtc = start.AddMinutes(30),
                LatestFirst = true,
            });
            Assert.Equal(HttpStatusCode.OK, match.StatusCode);
            var matchingPage = await match.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(matchingPage.GetProperty("hasMore").GetBoolean());
            Assert.Equal(body.GetProperty("publicId").GetGuid(),
                Assert.Single(matchingPage.GetProperty("items").EnumerateArray()).GetProperty("publicId").GetGuid());

            using var noMatch = await client.PostAsJsonAsync(searchUrl, new { UntilUtc = start });
            Assert.Equal(HttpStatusCode.OK, noMatch.StatusCode);
            var emptyPage = await noMatch.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Empty(emptyPage.GetProperty("items").EnumerateArray());

            using var invalidPage = await client.PostAsJsonAsync(searchUrl, new { Page = 0 });
            Assert.Equal(HttpStatusCode.BadRequest, invalidPage.StatusCode);

            tokens.AuthorizeSubject(client, "auth0|unlinked-booking-account");
            using var hidden = await client.GetAsync(bookingUrl);
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);

            var saved = Assert.Single(await db.Appointments.AsNoTracking().ToListAsync());
            Assert.Equal(staff.Id, saved.BookedByAppAccountId);
            Assert.Null(saved.ReferralId);
            Assert.Equal(start, saved.StartUtc);

            var rescheduleUrl = bookingUrl + "/reschedule";
            var move = new RescheduleAppointmentRequest(StartUtc, "2030-01-07T15:00:00Z");

            tokens.AuthorizeSubject(client, clinicianSubject);
            var clinicianDetail = await client.GetFromJsonAsync<JsonElement>(bookingUrl);
            Assert.False(clinicianDetail.GetProperty("canCancel").GetBoolean());
            using var deniedUpcoming = await client.GetAsync(AppointmentsUrl + "/upcoming");
            Assert.Equal(HttpStatusCode.Forbidden, deniedUpcoming.StatusCode);
            using var deniedSearch = await client.PostAsJsonAsync(searchUrl, new { });
            Assert.Equal(HttpStatusCode.Forbidden, deniedSearch.StatusCode);
            using var deniedMove = await client.PutAsJsonAsync(rescheduleUrl, move);
            Assert.Equal(HttpStatusCode.NotFound, deniedMove.StatusCode);

            db.ProviderTimeOff.Add(new ProviderTimeOff
            {
                ProviderId = provider.Id, StartUtc = start, EndUtc = start.AddMinutes(30),
            });
            await db.SaveChangesAsync();

            tokens.AuthorizeSubject(client, subject);
            clock.EnqueueReads(start.AddMinutes(-15), start.AddMinutes(-15));
            using var sameDayMove = await client.PutAsJsonAsync(rescheduleUrl, move);
            Assert.Equal(HttpStatusCode.BadRequest, sameDayMove.StatusCode);

            using var blockedMove = await client.PutAsJsonAsync(
                rescheduleUrl, move with { StartUtc = "2030-01-07T14:30:00Z" }
            );
            await AssertConflictAsync(blockedMove, "slot_unavailable");
            var afterConflict = await client.GetFromJsonAsync<JsonElement>(bookingUrl);
            Assert.Equal(start, afterConflict.GetProperty("startUtc").GetDateTimeOffset());
            Assert.True(afterConflict.GetProperty("canReschedule").GetBoolean());
            Assert.True(afterConflict.GetProperty("overlapsProviderTimeOff").GetBoolean());
            upcoming = await client.GetFromJsonAsync<JsonElement>(AppointmentsUrl + "/upcoming");
            Assert.Empty(upcoming.EnumerateArray());
            var beforeMove = await client.GetFromJsonAsync<JsonElement>(AppointmentsUrl + "/needs-attention");
            Assert.Equal(saved.PublicId, Assert.Single(beforeMove.EnumerateArray()).GetProperty("publicId").GetGuid());

            // The original visit starts after availability is checked and before the update.
            clock.EnqueueReads(now, now, start);
            using var startedDuringReschedule = await client.PutAsJsonAsync(rescheduleUrl, move);
            Assert.Equal(HttpStatusCode.Conflict, startedDuringReschedule.StatusCode);
            var afterStartedConflict = await client.GetFromJsonAsync<JsonElement>(bookingUrl);
            Assert.Equal(start, afterStartedConflict.GetProperty("startUtc").GetDateTimeOffset());

            using var moved = await client.PutAsJsonAsync(rescheduleUrl, move);
            Assert.Equal(HttpStatusCode.NoContent, moved.StatusCode);
            using var retry = await client.PutAsJsonAsync(rescheduleUrl, move);
            Assert.Equal(HttpStatusCode.NoContent, retry.StatusCode);

            using var stale = await client.PutAsJsonAsync(
                rescheduleUrl, move with { StartUtc = "2030-01-07T15:30:00Z" }
            );
            await AssertConflictAsync(stale, "appointment_changed");

            var updated = Assert.Single(await db.Appointments.AsNoTracking().ToListAsync());
            Assert.Equal(saved.PublicId, updated.PublicId);
            Assert.Equal(saved.ProviderId, updated.ProviderId);
            Assert.Equal(start.AddHours(1), updated.StartUtc);
            var afterMove = await client.GetFromJsonAsync<JsonElement>(AppointmentsUrl + "/needs-attention");
            Assert.Empty(afterMove.EnumerateArray());

            upcoming = await client.GetFromJsonAsync<JsonElement>(AppointmentsUrl + "/upcoming");
            Assert.Equal(saved.PublicId, Assert.Single(upcoming.EnumerateArray()).GetProperty("publicId").GetGuid());
            using var cancelled = await client.PostAsync(bookingUrl + "/cancel", null);
            Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
            var cancelledDetail = await client.GetFromJsonAsync<JsonElement>(bookingUrl);
            Assert.Equal("Cancelled", cancelledDetail.GetProperty("status").GetString());
            Assert.False(cancelledDetail.GetProperty("canCancel").GetBoolean());
            upcoming = await client.GetFromJsonAsync<JsonElement>(AppointmentsUrl + "/upcoming");
            Assert.Empty(upcoming.EnumerateArray());
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static async Task AssertConflictAsync(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, body.GetProperty("code").GetString());
    }

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        private readonly ConcurrentQueue<DateTimeOffset> reads = new();

        public void EnqueueReads(params DateTimeOffset[] values)
        {
            foreach (var value in values)
            {
                reads.Enqueue(value);
            }
        }

        public override DateTimeOffset GetUtcNow() => reads.TryDequeue(out var value) ? value : now;
    }
}
