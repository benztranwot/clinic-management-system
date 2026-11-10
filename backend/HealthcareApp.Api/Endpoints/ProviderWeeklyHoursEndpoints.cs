using System.Security.Claims;
using HealthcareApp.Api.Authorization;
using HealthcareApp.Api.Data;
using HealthcareApp.Api.Dtos;
using HealthcareApp.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HealthcareApp.Api.Endpoints;

public static class ProviderWeeklyHoursEndpoints
{
    private const int MinutesPerDay = 24 * 60;
    private const int SlotMinutes = 30;

    public static void MapProviderWeeklyHoursEndpoints(this IEndpointRouteBuilder app)
    {
        // Resolve the provider from the caller's linked account.
        app.MapGet("/api/me/weekly-hours", GetWeeklyHoursAsync).RequireAuthorization();
        app.MapPut("/api/me/weekly-hours", UpdateWeeklyHoursAsync).RequireAuthorization();
    }

    private static async Task<IResult> GetWeeklyHoursAsync(
        ClaimsPrincipal user,
        HttpContext http,
        HealthcareDbContext db,
        CancellationToken cancellationToken
    )
    {
        var identity = AccountIdentity.FromPrincipal(user);
        if (identity is null)
        {
            return Results.Challenge();
        }

        var schedule = await identity.QueryOwnProviders(db)
            .Select(provider => new
            {
                TimeZoneId = db.Clinics.Where(clinic => clinic.Id == provider.ClinicId)
                    .Select(clinic => clinic.TimeZoneId)
                    .FirstOrDefault(),
                Days = db.ProviderWeeklyHours.Where(hours => hours.ProviderId == provider.Id)
                    .Select(hours => new { hours.DayOfWeek, hours.StartMinute, hours.EndMinute })
                    .ToList(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (schedule is null)
        {
            return Results.Forbid();
        }

        // Providers without a saved day use the default for that day.
        var savedDays = schedule.Days.ToDictionary(day => day.DayOfWeek);
        var days = Enumerable.Range(0, 7).Select(dayOfWeek =>
        {
            if (savedDays.TryGetValue(dayOfWeek, out var day))
            {
                return new WeeklyHoursDay(dayOfWeek, day.StartMinute, day.EndMinute);
            }

            return dayOfWeek is >= (int)DayOfWeek.Monday and <= (int)DayOfWeek.Friday
                ? new WeeklyHoursDay(dayOfWeek, 9 * 60, 17 * 60)
                : new WeeklyHoursDay(dayOfWeek, null, null);
        });

        http.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new { schedule.TimeZoneId, Days = days });
    }

    private static async Task<IResult> UpdateWeeklyHoursAsync(
        ClaimsPrincipal user,
        UpdateProviderWeeklyHoursRequest request,
        HealthcareDbContext db,
        CancellationToken cancellationToken
    )
    {
        var identity = AccountIdentity.FromPrincipal(user);
        if (identity is null)
        {
            return Results.Challenge();
        }

        var providers = identity.QueryOwnProviders(db);
        var providerId = await providers.Select(provider => (int?)provider.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (providerId is null)
        {
            return Results.Forbid();
        }

        if (request.Days is not { Length: 7 } days)
        {
            return Results.BadRequest(new { message = "Provide one entry for each day of the week." });
        }

        var schedule = new List<ProviderWeeklyHours>(7);
        var seenDays = new HashSet<int>();

        foreach (var day in days)
        {
            if (day?.DayOfWeek is not int dayOfWeek || dayOfWeek is < 0 or > 6 || !seenDays.Add(dayOfWeek))
            {
                return Results.BadRequest(new { message = "Use each day number from 0 to 6 once." });
            }

            if (!IsValidWindow(day.StartMinute, day.EndMinute))
            {
                return Results.BadRequest(new
                {
                    message = "Use a 30-minute-aligned window within one day, or set both times to null.",
                });
            }

            schedule.Add(new ProviderWeeklyHours
            {
                ProviderId = providerId.Value,
                DayOfWeek = dayOfWeek,
                StartMinute = day.StartMinute,
                EndMinute = day.EndMinute,
            });
        }

        // Keep the whole week consistent across simultaneous saves.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await ProviderCalendarLock.TryAcquireAsync(db, providerId.Value, cancellationToken))
        {
            return Results.Conflict(new { message = "The provider calendar is busy. Try again." });
        }

        if (!await providers.AnyAsync(provider => provider.Id == providerId.Value, cancellationToken))
        {
            return Results.Forbid();
        }

        await db.ProviderWeeklyHours.Where(hours => hours.ProviderId == providerId.Value)
            .ExecuteDeleteAsync(cancellationToken);
        db.ProviderWeeklyHours.AddRange(schedule);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    private static bool IsValidWindow(int? start, int? end)
    {
        return (start is null && end is null)
            || (start is >= 0 && end is <= MinutesPerDay && start < end
                && start % SlotMinutes == 0 && end % SlotMinutes == 0);
    }
}
