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
        using var options = await client.GetAsync(AppointmentsUrl + "/booking-options");
        Assert.Equal(HttpStatusCode.Unauthorized, options.StatusCode);
    }

    [Fact]
    public async Task BookDirect_EnforcesPolicyAndSavesOneBookingForAContestedSlot()
    {
        const string subject = "auth0|booking-front-desk";
        const string clinicianSubject = "auth0|booking-clinician";
        const string assignedClinicianSubject = "auth0|booking-assigned-clinician";
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

            clinician.ProviderId = otherProvider.Id;
            var assignedClinician = new AppAccount
            {
                Issuer = TestJwtIssuer.Issuer, Subject = assignedClinicianSubject, ProviderId = provider.Id,
            };
            db.AppAccounts.Add(assignedClinician);
            await db.SaveChangesAsync();
            db.ClinicMemberships.Add(new ClinicMembership
            {
                AppAccountId = assignedClinician.Id, ClinicId = clinic.Id, Role = ClinicStaffRole.Clinician,
            });
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

            var outsideClinic = new Clinic { Name = "Other booking clinic", Address = "Other street", TimeZoneId = "America/Chicago" };
            db.Clinics.Add(outsideClinic);
            await db.SaveChangesAsync();
            var outsideProvider = new Provider { ClinicId = outsideClinic.Id, FirstName = "Outside", LastName = "Provider" };
            var outsideService = new ClinicalService { ClinicId = outsideClinic.Id, Name = "Outside service", AllowsDirectBooking = true };
            db.AddRange(outsideProvider, outsideService);
            await db.SaveChangesAsync();
            db.AddRange(new ProviderService { ClinicId = outsideClinic.Id, ProviderId = outsideProvider.Id, ClinicalServiceId = outsideService.Id },
                new ProviderService { ClinicId = clinic.Id, ProviderId = provider.Id, ClinicalServiceId = referralOnly.Id });
            await db.SaveChangesAsync();
            tokens.AuthorizeSubject(client, subject);
            using var optionsResponse = await client.GetAsync(AppointmentsUrl + "/booking-options");
            optionsResponse.EnsureSuccessStatusCode();
            Assert.Equal("no-store", optionsResponse.Headers.CacheControl?.ToString());
            var options = await optionsResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(2, options.GetArrayLength());
            Assert.All(options.EnumerateArray(), option =>
            {
                Assert.Equal(clinic.PublicId, option.GetProperty("clinicPublicId").GetGuid());
                Assert.Equal(physical.PublicId, option.GetProperty("clinicalServicePublicId").GetGuid());
                Assert.Contains(option.GetProperty("providerPublicId").GetGuid(), new[] { provider.PublicId, otherProvider.PublicId });
            });
            var bookingDatesUrl = $"{AppointmentsUrl}/booking-availability";
            var selectedDates = $"{bookingDatesUrl}?providerPublicId={provider.PublicId}&clinicalServicePublicId={physical.PublicId}";
            using var datesResponse = await client.GetAsync(selectedDates);
            datesResponse.EnsureSuccessStatusCode();
            Assert.True(datesResponse.Headers.CacheControl?.NoStore);
            var days = (await datesResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("days");
            Assert.Equal(30, days.GetArrayLength());
            Assert.Equal("2030-01-07", days[0].GetProperty("date").GetString());
            Assert.Equal("Available", days[0].GetProperty("status").GetString());
            Assert.Equal("OutsideWorkingHours", days[5].GetProperty("status").GetString());
            Assert.Empty(days[5].GetProperty("slots").EnumerateArray());
            using var referralOnlyDates = await client.GetAsync(
                $"{bookingDatesUrl}?providerPublicId={provider.PublicId}&clinicalServicePublicId={referralOnly.PublicId}");
            Assert.Equal(HttpStatusCode.NotFound, referralOnlyDates.StatusCode);
            using var outsideDates = await client.GetAsync(
                $"{bookingDatesUrl}?providerPublicId={outsideProvider.PublicId}&clinicalServicePublicId={outsideService.PublicId}");
            Assert.Equal(HttpStatusCode.NotFound, outsideDates.StatusCode);
            tokens.AuthorizeSubject(client, clinicianSubject);
            using var deniedDates = await client.GetAsync(selectedDates);
            Assert.Equal(HttpStatusCode.NotFound, deniedDates.StatusCode);
            using var deniedOptions = await client.GetAsync(AppointmentsUrl + "/booking-options");
            Assert.Equal(HttpStatusCode.Forbidden, deniedOptions.StatusCode);
            tokens.AuthorizeSubject(client, "auth0|unknown-scheduler");
            using var unlinkedOptions = await client.GetAsync(AppointmentsUrl + "/booking-options");
            Assert.Equal(HttpStatusCode.Forbidden, unlinkedOptions.StatusCode);

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

            // Another clinic's booking must not appear in either search rows or totals.
            var outsideBooking = new Appointment
            {
                PatientId = firstPatient.Id,
                ClinicalServiceId = outsideService.Id,
                ProviderId = outsideProvider.Id,
                BookedByAppAccountId = staff.Id,
                StartUtc = start.AddHours(2),
                CreatedAtUtc = now,
            };
            db.Appointments.Add(outsideBooking);
            await db.SaveChangesAsync();

            using var scopedSearch = await client.PostAsJsonAsync(searchUrl, new { page = 1 });
            Assert.Equal(HttpStatusCode.OK, scopedSearch.StatusCode);
            var scopedPage = await scopedSearch.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(1, scopedPage.GetProperty("totalCount").GetInt32());
            Assert.Equal(body.GetProperty("publicId").GetGuid(),
                Assert.Single(scopedPage.GetProperty("items").EnumerateArray()).GetProperty("publicId").GetGuid());

            using var outsideSearch = await client.PostAsJsonAsync(searchUrl, new { clinic = outsideClinic.Name });
            Assert.Equal(HttpStatusCode.OK, outsideSearch.StatusCode);
            var outsidePage = await outsideSearch.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(0, outsidePage.GetProperty("totalCount").GetInt32());
            Assert.Empty(outsidePage.GetProperty("items").EnumerateArray());

            // An inaccessible clinic's conflict must not leak through the badge count.
            var outsideBlock = new ProviderTimeOff
            {
                ProviderId = outsideProvider.Id,
                StartUtc = outsideBooking.StartUtc,
                EndUtc = outsideBooking.StartUtc.AddMinutes(30),
            };
            db.ProviderTimeOff.Add(outsideBlock);
            await db.SaveChangesAsync();

            using var scopedCount = await client.GetAsync(AppointmentsUrl + "/needs-attention/count");
            Assert.Equal(HttpStatusCode.OK, scopedCount.StatusCode);
            var countBody = await scopedCount.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(0, countBody.GetProperty("count").GetInt32());
            using var scopedConflicts = await client.PostAsJsonAsync(AppointmentsUrl + "/needs-attention/search", new { page = 1 });
            Assert.Equal(HttpStatusCode.OK, scopedConflicts.StatusCode);
            var conflictResult = await scopedConflicts.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(0, conflictResult.GetProperty("totalCount").GetInt32());
            Assert.Empty(conflictResult.GetProperty("items").EnumerateArray());

            db.Appointments.Remove(outsideBooking);
            db.ProviderTimeOff.Remove(outsideBlock);
            await db.SaveChangesAsync();
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
            using var deniedCancel = await client.PostAsync(bookingUrl + "/cancel", null);
            Assert.Equal(HttpStatusCode.NotFound, deniedCancel.StatusCode);
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

            using var ownConflicts = await client.PostAsJsonAsync(AppointmentsUrl + "/needs-attention/search", new { page = 1 });
            ownConflicts.EnsureSuccessStatusCode();
            var ownConflictPage = await ownConflicts.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Empty(ownConflictPage.GetProperty("items").EnumerateArray());
            Assert.Equal(0, (await client.GetFromJsonAsync<JsonElement>(AppointmentsUrl + "/needs-attention/count"))
                .GetProperty("count").GetInt32());
            tokens.AuthorizeSubject(client, assignedClinicianSubject);
            Assert.Equal(1, (await client.GetFromJsonAsync<JsonElement>(AppointmentsUrl + "/needs-attention/count"))
                .GetProperty("count").GetInt32());

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

            using var conflictSearch = await client.PostAsJsonAsync(AppointmentsUrl + "/needs-attention/search", new
            {
                patient = afterConflict.GetProperty("patientName").GetString(),
                clinic = afterConflict.GetProperty("clinicName").GetString(),
                service = afterConflict.GetProperty("serviceName").GetString(),
                provider = afterConflict.GetProperty("providerName").GetString(),
                fromUtc = start, untilUtc = start.AddMinutes(30),
            });
            conflictSearch.EnsureSuccessStatusCode();
            var conflictPage = await conflictSearch.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(1, conflictPage.GetProperty("totalCount").GetInt32());
            Assert.Equal(saved.PublicId, Assert.Single(conflictPage.GetProperty("items").EnumerateArray()).GetProperty("publicId").GetGuid());
            var attentionCount = await client.GetFromJsonAsync<JsonElement>(AppointmentsUrl + "/needs-attention/count");
            Assert.Equal(1, attentionCount.GetProperty("count").GetInt32());

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
            tokens.AuthorizeSubject(client, assignedClinicianSubject);
            var assignedDetail = await client.GetFromJsonAsync<JsonElement>(bookingUrl);
            Assert.True(assignedDetail.GetProperty("canCancel").GetBoolean());
            Assert.False(assignedDetail.GetProperty("canReschedule").GetBoolean());
            Assert.False(assignedDetail.GetProperty("canComplete").GetBoolean());
            using var cancelled = await client.PostAsync(bookingUrl + "/cancel", null);
            Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
            var cancelledDetail = await client.GetFromJsonAsync<JsonElement>(bookingUrl);
            Assert.Equal("Cancelled", cancelledDetail.GetProperty("status").GetString());
            Assert.False(cancelledDetail.GetProperty("canCancel").GetBoolean());
            tokens.AuthorizeSubject(client, subject);
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
