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
            new CreateReferredAppointmentRequest(Guid.NewGuid(), StartUtc)
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task BookReferred_SavesOneBookingAndProtectsItsReferral()
    {
        const string subject = "auth0|referred-booking-front-desk";
        var now = new DateTimeOffset(2030, 1, 7, 13, 45, 0, TimeSpan.Zero);

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
            var firstRequest = new CreateReferredAppointmentRequest(firstProvider.PublicId, StartUtc);
            var secondRequest = new CreateReferredAppointmentRequest(
                secondProvider.PublicId, "2030-01-07T14:30:00Z"
            );

            tokens.AuthorizeSubject(client, "auth0|unlinked-booker", "manage:referrals");
            using var hidden = await client.PostAsJsonAsync(url, firstRequest);
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);

            tokens.AuthorizeSubject(client, subject, "read:referrals", "manage:referrals");
            using var notReady = await client.PostAsJsonAsync(url, firstRequest);
            await AssertConflictAsync(notReady, "referral_not_ready");

            referral.Status = ReferralStatus.ReadyToSchedule;
            await db.SaveChangesAsync();

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
            var winningProvider = body.GetProperty("providerPublicId").GetGuid();

            var appointmentUrl = created.Headers.Location?.OriginalString;
            Assert.NotNull(appointmentUrl);
            var appointment = await client.GetFromJsonAsync<JsonElement>(appointmentUrl);
            Assert.Equal(referral.PublicId, appointment.GetProperty("referralPublicId").GetGuid());

            var detail = await client.GetFromJsonAsync<JsonElement>(referralUrl);
            Assert.Equal("ReadyToSchedule", detail.GetProperty("status").GetString());
            Assert.Equal("Scheduled", detail.GetProperty("stage").GetString());
            Assert.Equal(winningProvider, detail.GetProperty("assignedProviderPublicId").GetGuid());

            var list = await client.GetFromJsonAsync<JsonElement>(
                $"/api/clinics/{clinic.PublicId}/referrals"
            );
            Assert.Equal("Scheduled", Assert.Single(list.EnumerateArray()).GetProperty("stage").GetString());

            using var assign = await client.PutAsJsonAsync(
                $"{referralUrl}/provider",
                new { providerPublicId = winningProvider == firstProvider.PublicId
                    ? secondProvider.PublicId : firstProvider.PublicId }
            );
            Assert.Equal(HttpStatusCode.Conflict, assign.StatusCode);

            using var close = await client.PutAsJsonAsync($"{referralUrl}/status", new { status = "Closed" });
            Assert.Equal(HttpStatusCode.Conflict, close.StatusCode);

            var saved = Assert.Single(await db.Appointments.AsNoTracking().ToListAsync());
            Assert.Equal(referral.Id, saved.ReferralId);
            Assert.Equal(account.Id, saved.BookedByAppAccountId);
            Assert.Equal(winningProvider == firstProvider.PublicId ? firstProvider.Id : secondProvider.Id, saved.ProviderId);
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
