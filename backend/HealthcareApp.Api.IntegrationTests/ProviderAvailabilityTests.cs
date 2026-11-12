using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HealthcareApp.Api.Data;
using HealthcareApp.Api.Models;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HealthcareApp.Api.IntegrationTests;

public class ProviderAvailabilityTests
{
    [Fact]
    public async Task GetAvailability_WithoutToken_ReturnsUnauthorized()
    {
        await using var factory = new HealthcareApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            $"/api/providers/{Guid.NewGuid()}/availability?date=2030-01-07"
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAvailability_FiltersLunchTimeOffBookingsAndWeekends()
    {
        const string subject = "auth0|availability-front-desk";
        var now = new DateTimeOffset(2030, 1, 7, 5, 30, 0, TimeSpan.Zero);
        var firstStart = new DateTimeOffset(2030, 1, 7, 14, 0, 0, TimeSpan.Zero);

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

            var clinic = new Clinic
            {
                Name = "Calendar Test Clinic", TimeZoneId = "America/Chicago",
                Address = "100 Test Avenue",
            };
            var otherClinic = new Clinic
            {
                Name = "Other Calendar Test Clinic", TimeZoneId = "America/Chicago",
                Address = "100 Test Avenue",
            };
            var staff = new AppAccount { Issuer = TestJwtIssuer.Issuer, Subject = subject };
            var patient = new Patient
            {
                FirstName = "Alex", LastName = "Demo", DateOfBirth = new DateOnly(1990, 3, 5),
            };
            db.AddRange(clinic, otherClinic, staff, patient);
            await db.SaveChangesAsync();

            var provider = new Provider
            {
                ClinicId = clinic.Id, FirstName = "Beth", LastName = "Demo",
            };
            var otherProvider = new Provider
            {
                ClinicId = otherClinic.Id, FirstName = "John", LastName = "Demo",
            };
            var service = new ClinicalService { ClinicId = clinic.Id, Name = "Physical Exam" };
            db.AddRange(
                provider, otherProvider, service,
                new ClinicMembership
                {
                    AppAccountId = staff.Id, ClinicId = clinic.Id, Role = ClinicStaffRole.FrontDesk,
                }
            );
            await db.SaveChangesAsync();

            // A saved weekend work window still cannot produce bookable appointments.
            db.ProviderWeeklyHours.Add(new ProviderWeeklyHours
            {
                ProviderId = provider.Id,
                DayOfWeek = (int)DayOfWeek.Saturday,
                StartMinute = 480,
                EndMinute = 1020,
            });
            await db.SaveChangesAsync();

            tokens.AuthorizeSubject(client, subject);
            var url = $"/api/providers/{provider.PublicId}/availability";
            var weekdayUrl = $"{url}?date=2030-01-07";

            // It is January 7 in UTC, but still January 6 at this clinic.
            using var sameDay = await client.GetAsync($"{url}?date=2030-01-06");
            Assert.Equal(HttpStatusCode.BadRequest, sameDay.StatusCode);

            using var invalidDate = await client.GetAsync($"{url}?date=2030-02-07");
            Assert.Equal(HttpStatusCode.BadRequest, invalidDate.StatusCode);

            var result = await client.GetFromJsonAsync<JsonElement>(weekdayUrl);
            Assert.Equal("2030-01-07", result.GetProperty("date").GetString());
            Assert.Equal("America/Chicago", result.GetProperty("timeZoneId").GetString());
            var slots = result.GetProperty("slots");
            Assert.Equal(16, slots.GetArrayLength());
            Assert.Equal(firstStart, slots[0].GetProperty("startUtc").GetDateTimeOffset());
            Assert.Equal(firstStart.AddMinutes(30), slots[0].GetProperty("endUtc").GetDateTimeOffset());
            Assert.Equal(firstStart.AddHours(9), slots[15].GetProperty("endUtc").GetDateTimeOffset());
            var defaultStarts = GetStarts(result);
            Assert.DoesNotContain(firstStart.AddHours(4), defaultStarts);
            Assert.DoesNotContain(firstStart.AddHours(4.5), defaultStarts);
            Assert.Contains(firstStart.AddHours(5), defaultStarts);

            var saturday = await client.GetFromJsonAsync<JsonElement>($"{url}?date=2030-01-12");
            var sunday = await client.GetFromJsonAsync<JsonElement>($"{url}?date=2030-01-13");
            Assert.Empty(saturday.GetProperty("slots").EnumerateArray());
            Assert.Empty(sunday.GetProperty("slots").EnumerateArray());

            db.AddRange(
                new ProviderTimeOff
                {
                    ProviderId = provider.Id,
                    StartUtc = firstStart.AddMinutes(70),
                    EndUtc = firstStart.AddMinutes(90),
                },
                new Appointment
                {
                    PatientId = patient.Id,
                    ProviderId = provider.Id,
                    ClinicalServiceId = service.Id,
                    StartUtc = firstStart.AddHours(5),
                    BookedByAppAccountId = staff.Id,
                },
                new Appointment
                {
                    PatientId = patient.Id,
                    ProviderId = provider.Id,
                    ClinicalServiceId = service.Id,
                    StartUtc = firstStart.AddHours(6),
                    Status = AppointmentStatus.Cancelled,
                    BookedByAppAccountId = staff.Id,
                }
            );
            await db.SaveChangesAsync();

            var updated = await client.GetFromJsonAsync<JsonElement>(weekdayUrl);
            var starts = GetStarts(updated);
            Assert.Equal(14, starts.Length);
            Assert.DoesNotContain(firstStart.AddHours(1), starts);
            Assert.Contains(firstStart.AddMinutes(90), starts);
            Assert.DoesNotContain(firstStart.AddHours(5), starts);
            Assert.Contains(firstStart.AddHours(6), starts);

            using var crossClinic = await client.GetAsync(
                $"/api/providers/{otherProvider.PublicId}/availability?date=2030-01-07"
            );
            Assert.Equal(HttpStatusCode.NotFound, crossClinic.StatusCode);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static DateTimeOffset[] GetStarts(JsonElement result)
    {
        return result.GetProperty("slots").EnumerateArray()
            .Select(slot => slot.GetProperty("startUtc").GetDateTimeOffset())
            .ToArray();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
