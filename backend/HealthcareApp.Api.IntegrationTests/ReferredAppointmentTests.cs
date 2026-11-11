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

public class ReferredAppointmentTests
{
    private const string StartUtc = "2030-01-07T14:00:00Z";

    [Fact]
    public async Task BookReferred_WithoutToken_ReturnsUnauthorized()
    {
        await using var factory = new HealthcareApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/referrals/{Guid.NewGuid()}/appointments",
            new CreateReferredAppointmentRequest(StartUtc)
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task BookReferred_SavesOneBookingAndProtectsItsReferral()
    {
        const string subject = "auth0|referred-booking-front-desk";
        const string clinicianSubject = "auth0|referred-booking-clinician";
        var clock = new TestClock(new DateTimeOffset(2030, 1, 7, 13, 45, 0, TimeSpan.Zero));

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

            var clinic = new Clinic { Name = "Referral Booking Clinic", TimeZoneId = "America/Chicago" };
            var account = new AppAccount { Issuer = TestJwtIssuer.Issuer, Subject = subject };
            var patient = new Patient
            {
                FirstName = "Alex", LastName = "Demo", DateOfBirth = new DateOnly(1990, 3, 5),
            };
            db.AddRange(clinic, account, patient);
            await db.SaveChangesAsync();

            var firstProvider = new Provider
            {
                ClinicId = clinic.Id, FirstName = "Beth", LastName = "Demo",
            };
            var secondProvider = new Provider
            {
                ClinicId = clinic.Id, FirstName = "John", LastName = "Demo",
            };
            var service = new ClinicalService { ClinicId = clinic.Id, Name = "Therapy" };
            db.AddRange(
                firstProvider, secondProvider, service,
                new ClinicMembership
                {
                    AppAccountId = account.Id, ClinicId = clinic.Id, Role = ClinicStaffRole.FrontDesk,
                }
            );
            await db.SaveChangesAsync();

            var clinician = new AppAccount
            {
                Issuer = TestJwtIssuer.Issuer,
                Subject = clinicianSubject,
                ProviderId = firstProvider.Id,
            };
            db.AppAccounts.Add(clinician);
            await db.SaveChangesAsync();
            db.ClinicMemberships.Add(new ClinicMembership
            {
                AppAccountId = clinician.Id, ClinicId = clinic.Id, Role = ClinicStaffRole.Clinician,
            });

            db.ProviderServices.AddRange(
                new ProviderService
                {
                    ClinicId = clinic.Id, ProviderId = firstProvider.Id, ClinicalServiceId = service.Id,
                },
                new ProviderService
                {
                    ClinicId = clinic.Id, ProviderId = secondProvider.Id, ClinicalServiceId = service.Id,
                }
            );
            await db.SaveChangesAsync();

            var referral = new Referral
            {
                PatientId = patient.Id,
                ClinicalServiceId = service.Id,
                AssignedProviderId = firstProvider.Id,
                ReferringProviderName = "Dr. Demo",
                Reason = "Fictional referral for booking test",
            };
            db.Referrals.Add(referral);
            await db.SaveChangesAsync();

            var referralUrl = $"/api/referrals/{referral.PublicId}";
            var url = $"{referralUrl}/appointments";
            var firstRequest = new CreateReferredAppointmentRequest(StartUtc);
            var secondRequest = new CreateReferredAppointmentRequest("2030-01-07T14:30:00Z");

            tokens.AuthorizeSubject(client, "auth0|unlinked-booker", "manage:referrals");
            using var hidden = await client.PostAsJsonAsync(url, firstRequest);
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);

            tokens.AuthorizeSubject(client, subject, "read:referrals", "manage:referrals");
            using var notReady = await client.PostAsJsonAsync(url, firstRequest);
            await AssertConflictAsync(notReady, "referral_not_ready");

            using var frontDeskReview = await client.PutAsJsonAsync(
                $"{referralUrl}/status", new { status = "ReadyToSchedule" }
            );
            Assert.Equal(HttpStatusCode.Forbidden, frontDeskReview.StatusCode);

            tokens.AuthorizeSubject(client, clinicianSubject, "read:referrals", "manage:referrals");
            using var missing = await client.PutAsJsonAsync($"{referralUrl}/status", new
            {
                status = "MissingInformation", missingInformationNote = "Please include the assessment.",
            });
            Assert.Equal(HttpStatusCode.OK, missing.StatusCode);
            var pending = await client.GetFromJsonAsync<JsonElement>(referralUrl);
            Assert.Equal("PendingReview", pending.GetProperty("stage").GetString());
            Assert.Equal("MissingInformation", pending.GetProperty("status").GetString());

            using var ready = await client.PutAsJsonAsync(
                $"{referralUrl}/status", new { status = "ReadyToSchedule" }
            );
            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
            var readyDetail = await client.GetFromJsonAsync<JsonElement>(referralUrl);
            Assert.Equal("ReadyToSchedule", readyDetail.GetProperty("stage").GetString());
            Assert.Equal(JsonValueKind.Null, readyDetail.GetProperty("missingInformationNote").ValueKind);
            Assert.False(readyDetail.GetProperty("canSchedule").GetBoolean());

            tokens.AuthorizeSubject(client, subject, "read:referrals", "manage:referrals");
            using var wrongProvider = await client.PostAsJsonAsync(
                url, new CreateReferredAppointmentRequest(StartUtc, secondProvider.PublicId)
            );
            await AssertConflictAsync(wrongProvider, "referral_provider_changed");

            // The two slots differ, but only one booking may use this referral.
            var responses = await Task.WhenAll(
                client.PostAsJsonAsync(url, firstRequest),
                client.PostAsJsonAsync(url, secondRequest)
            );
            using var created = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            using var duplicate = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            await AssertConflictAsync(duplicate, "referral_already_booked");

            var body = await created.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(referral.PublicId, body.GetProperty("referralPublicId").GetGuid());
            Assert.Equal(patient.PublicId, body.GetProperty("patientPublicId").GetGuid());
            Assert.Equal(service.PublicId, body.GetProperty("clinicalServicePublicId").GetGuid());
            var bookedProvider = body.GetProperty("providerPublicId").GetGuid();
            Assert.Equal(firstProvider.PublicId, bookedProvider);

            var appointmentUrl = created.Headers.Location?.OriginalString;
            Assert.NotNull(appointmentUrl);
            var appointment = await client.GetFromJsonAsync<JsonElement>(appointmentUrl);
            Assert.Equal(referral.PublicId, appointment.GetProperty("referralPublicId").GetGuid());

            var detail = await client.GetFromJsonAsync<JsonElement>(referralUrl);
            Assert.Equal("ReadyToSchedule", detail.GetProperty("status").GetString());
            Assert.Equal("Scheduled", detail.GetProperty("stage").GetString());
            Assert.Equal(bookedProvider, detail.GetProperty("assignedProviderPublicId").GetGuid());
            Assert.True(detail.GetProperty("canSchedule").GetBoolean());
            Assert.Equal("America/Chicago", detail.GetProperty("clinicTimeZoneId").GetString());
            var confirmed = detail.GetProperty("confirmedAppointment");
            Assert.Equal(body.GetProperty("publicId").GetGuid(), confirmed.GetProperty("publicId").GetGuid());
            Assert.Equal(body.GetProperty("startUtc").GetDateTimeOffset(), confirmed.GetProperty("startUtc").GetDateTimeOffset());
            Assert.Equal(body.GetProperty("endUtc").GetDateTimeOffset(), confirmed.GetProperty("endUtc").GetDateTimeOffset());

            // Broad referral-read permission does not expose a clinic's booked appointment details.
            tokens.AuthorizeSubject(client, "auth0|unlinked-booker", "read:referrals");
            var unlinkedDetail = await client.GetFromJsonAsync<JsonElement>(referralUrl);
            Assert.Equal("Scheduled", unlinkedDetail.GetProperty("stage").GetString());
            Assert.False(unlinkedDetail.GetProperty("canSchedule").GetBoolean());
            Assert.Equal(JsonValueKind.Null, unlinkedDetail.GetProperty("confirmedAppointment").ValueKind);

            tokens.AuthorizeSubject(client, subject, "read:referrals", "manage:referrals");

            var list = await client.GetFromJsonAsync<JsonElement>(
                $"/api/clinics/{clinic.PublicId}/referrals"
            );
            Assert.Equal("Scheduled", Assert.Single(list.EnumerateArray()).GetProperty("stage").GetString());

            using var assign = await client.PutAsJsonAsync(
                $"{referralUrl}/provider",
                new { providerPublicId = secondProvider.PublicId }
            );
            Assert.Equal(HttpStatusCode.Conflict, assign.StatusCode);

            tokens.AuthorizeSubject(client, clinicianSubject, "read:referrals", "manage:referrals");
            using var close = await client.PutAsJsonAsync($"{referralUrl}/status", new { status = "Closed" });
            Assert.Equal(HttpStatusCode.Conflict, close.StatusCode);

            var saved = Assert.Single(await db.Appointments.AsNoTracking().ToListAsync());
            Assert.Equal(referral.Id, saved.ReferralId);
            Assert.Equal(account.Id, saved.BookedByAppAccountId);
            Assert.Equal(firstProvider.Id, saved.ProviderId);

            using var earlyCompletion = await client.PostAsync($"{appointmentUrl}/complete", null);
            await AssertConflictAsync(earlyCompletion, "appointment_not_started");

            tokens.AuthorizeSubject(client, subject, "read:referrals", "manage:referrals");
            using var cancellation = await client.PostAsync($"{appointmentUrl}/cancel", null);
            Assert.Equal(HttpStatusCode.OK, cancellation.StatusCode);
            var afterCancellation = await client.GetFromJsonAsync<JsonElement>(referralUrl);
            Assert.Equal("ReadyToSchedule", afterCancellation.GetProperty("stage").GetString());
            Assert.Equal(JsonValueKind.Null, afterCancellation.GetProperty("confirmedAppointment").ValueKind);
            Assert.Equal(firstProvider.PublicId, afterCancellation.GetProperty("assignedProviderPublicId").GetGuid());

            var originalStart = body.GetProperty("startUtc").GetString();
            using var rebooked = await client.PostAsJsonAsync(url, new CreateReferredAppointmentRequest(originalStart));
            Assert.Equal(HttpStatusCode.Created, rebooked.StatusCode);
            var rebookedUrl = rebooked.Headers.Location?.OriginalString;
            Assert.NotNull(rebookedUrl);

            clock.UtcNow = body.GetProperty("endUtc").GetDateTimeOffset();
            tokens.AuthorizeSubject(client, clinicianSubject, "read:referrals", "manage:referrals");
            using var completion = await client.PostAsync($"{rebookedUrl}/complete", null);
            Assert.Equal(HttpStatusCode.OK, completion.StatusCode);
            var afterCompletion = await client.GetFromJsonAsync<JsonElement>(referralUrl);
            Assert.Equal("Closed", afterCompletion.GetProperty("stage").GetString());

            var history = await db.Appointments.AsNoTracking().ToListAsync();
            Assert.Equal(2, history.Count);
            Assert.Contains(history, item => item.Id == saved.Id && item.Status == AppointmentStatus.Cancelled);
            Assert.Single(history, item => item.Status == AppointmentStatus.Completed);
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
        public DateTimeOffset UtcNow { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
