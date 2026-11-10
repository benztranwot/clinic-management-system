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
        var now = new DateTimeOffset(2030, 1, 7, 13, 45, 0, TimeSpan.Zero);
        var start = new DateTimeOffset(2030, 1, 7, 14, 0, 0, TimeSpan.Zero);

        using var tokens = new TestJwtIssuer();
        await using var factory = new HealthcareApiFactory();
        await using var signedFactory = tokens.CreateFactory(factory);
        await using var configuredFactory = signedFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<TimeProvider>(new FixedClock(now))
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

            var clinic = new Clinic { Name = "Booking Test Clinic", TimeZoneId = "America/Chicago" };
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

            tokens.AuthorizeSubject(client, "auth0|unlinked-booking-account");
            using var hidden = await client.GetAsync(bookingUrl);
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);

            var saved = Assert.Single(await db.Appointments.AsNoTracking().ToListAsync());
            Assert.Equal(staff.Id, saved.BookedByAppAccountId);
            Assert.Null(saved.ReferralId);
            Assert.Equal(start, saved.StartUtc);
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

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
