using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HealthcareApp.Api.Data;
using HealthcareApp.Api.Dtos;
using HealthcareApp.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HealthcareApp.Api.IntegrationTests;

public class ProviderWeeklyHoursTests
{
    private const string HoursUrl = "/api/me/weekly-hours";

    [Fact]
    public async Task GetWeeklyHours_WithoutToken_ReturnsUnauthorized()
    {
        await using var factory = new HealthcareApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(HoursUrl);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WeeklyHours_ProviderCanReplaceOnlyTheirOwnWeek()
    {
        const string doctorSubject = "auth0|weekly-hours-doctor";
        const string otherSubject = "auth0|weekly-hours-other-doctor";
        const string staffSubject = "auth0|weekly-hours-staff";

        using var tokens = new TestJwtIssuer();
        await using var factory = new HealthcareApiFactory();
        await using var configuredFactory = tokens.CreateFactory(factory);
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
                Name = "Schedule Test Clinic", TimeZoneId = "America/Chicago",
                Address = "100 Test Avenue",
            };
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();

            var doctor = new Provider { ClinicId = clinic.Id, FirstName = "Beth", LastName = "Demo" };
            var otherDoctor = new Provider { ClinicId = clinic.Id, FirstName = "John", LastName = "Demo" };
            db.Providers.AddRange(doctor, otherDoctor);
            await db.SaveChangesAsync();

            var doctorAccount = new AppAccount
            {
                Issuer = TestJwtIssuer.Issuer,
                Subject = doctorSubject,
                ProviderId = doctor.Id,
            };
            var otherAccount = new AppAccount
            {
                Issuer = TestJwtIssuer.Issuer,
                Subject = otherSubject,
                ProviderId = otherDoctor.Id,
            };
            var staff = new AppAccount { Issuer = TestJwtIssuer.Issuer, Subject = staffSubject };
            db.AppAccounts.AddRange(doctorAccount, otherAccount, staff);
            await db.SaveChangesAsync();

            db.ClinicMemberships.AddRange(
                new ClinicMembership
                {
                    AppAccountId = doctorAccount.Id,
                    ClinicId = clinic.Id,
                    Role = ClinicStaffRole.Clinician,
                },
                new ClinicMembership
                {
                    AppAccountId = otherAccount.Id,
                    ClinicId = clinic.Id,
                    Role = ClinicStaffRole.Clinician,
                },
                new ClinicMembership
                {
                    AppAccountId = staff.Id,
                    ClinicId = clinic.Id,
                    Role = ClinicStaffRole.FrontDesk,
                }
            );
            await db.SaveChangesAsync();

            // Start with one working day. Mark all other days as closed.
            var days = Enumerable.Range(0, 7)
                .Select(day => day == 1 ? new WeeklyHoursDay(day, 600, 960, 720, 780) : new WeeklyHoursDay(day, null, null))
                .ToArray();
            var request = new UpdateProviderWeeklyHoursRequest(days);

            tokens.AuthorizeSubject(client, staffSubject, "manage:providers");
            using var denied = await client.PutAsJsonAsync(HoursUrl, request);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

            tokens.AuthorizeSubject(client, doctorSubject);
            var before = await client.GetFromJsonAsync<JsonElement>(HoursUrl);
            Assert.Equal("America/Chicago", before.GetProperty("timeZoneId").GetString());
            Assert.Equal(7, before.GetProperty("days").GetArrayLength());
            Assert.Equal(480, GetDay(before, 1).GetProperty("startMinute").GetInt32());
            Assert.Equal(1020, GetDay(before, 1).GetProperty("endMinute").GetInt32());
            Assert.Equal(720, GetDay(before, 1).GetProperty("breakStartMinute").GetInt32());
            Assert.Equal(780, GetDay(before, 1).GetProperty("breakEndMinute").GetInt32());
            Assert.Equal(JsonValueKind.Null, GetDay(before, 0).GetProperty("startMinute").ValueKind);

            var invalid = days.ToArray();
            invalid[1] = new WeeklyHoursDay(1, 601, 960);
            using var rejected = await client.PutAsJsonAsync(
                HoursUrl,
                new UpdateProviderWeeklyHoursRequest(invalid)
            );
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            invalid[1] = days[1] with { BreakStartMinute = 735 };
            using var offGridBreak = await client.PutAsJsonAsync(
                HoursUrl,
                new UpdateProviderWeeklyHoursRequest(invalid)
            );
            Assert.Equal(HttpStatusCode.BadRequest, offGridBreak.StatusCode);

            invalid[1] = days[1] with { BreakEndMinute = null };
            using var incompleteBreak = await client.PutAsJsonAsync(
                HoursUrl,
                new UpdateProviderWeeklyHoursRequest(invalid)
            );
            Assert.Equal(HttpStatusCode.BadRequest, incompleteBreak.StatusCode);
            Assert.Empty(await db.ProviderWeeklyHours.ToListAsync());

            using var updated = await client.PutAsJsonAsync(
                $"{HoursUrl}?providerPublicId={otherDoctor.PublicId}",
                request
            );
            Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);

            var after = await client.GetFromJsonAsync<JsonElement>(HoursUrl);
            Assert.Equal(600, GetDay(after, 1).GetProperty("startMinute").GetInt32());
            Assert.Equal(960, GetDay(after, 1).GetProperty("endMinute").GetInt32());
            Assert.Equal(720, GetDay(after, 1).GetProperty("breakStartMinute").GetInt32());
            Assert.Equal(780, GetDay(after, 1).GetProperty("breakEndMinute").GetInt32());
            Assert.Equal(JsonValueKind.Null, GetDay(after, 2).GetProperty("startMinute").ValueKind);

            // A second full save replaces the previous week.
            days[1] = new WeeklyHoursDay(1, 630, 960);
            using var replaced = await client.PutAsJsonAsync(HoursUrl, request);
            Assert.Equal(HttpStatusCode.NoContent, replaced.StatusCode);
            var afterReplacement = await client.GetFromJsonAsync<JsonElement>(HoursUrl);
            Assert.Equal(630, GetDay(afterReplacement, 1).GetProperty("startMinute").GetInt32());
            Assert.Equal(JsonValueKind.Null, GetDay(afterReplacement, 1).GetProperty("breakStartMinute").ValueKind);

            var stored = await db.ProviderWeeklyHours.AsNoTracking().ToListAsync();
            Assert.Equal(7, stored.Count);
            Assert.All(stored, day => Assert.Equal(doctor.Id, day.ProviderId));

            tokens.AuthorizeSubject(client, otherSubject);
            var other = await client.GetFromJsonAsync<JsonElement>(HoursUrl);
            Assert.Equal(480, GetDay(other, 1).GetProperty("startMinute").GetInt32());
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static JsonElement GetDay(JsonElement schedule, int dayOfWeek)
    {
        return schedule.GetProperty("days").EnumerateArray()
            .Single(day => day.GetProperty("dayOfWeek").GetInt32() == dayOfWeek);
    }
}
