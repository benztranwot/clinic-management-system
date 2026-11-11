using System.Globalization;
using System.Security.Claims;
using HealthcareApp.Api.Authorization;
using HealthcareApp.Api.Data;
using HealthcareApp.Api.Models;
using HealthcareApp.Api.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace HealthcareApp.Api.Endpoints;

public static class ProviderCalendarEndpoints
{
    private const int CalendarDays = 90;

    public static void MapProviderCalendarEndpoints(this IEndpointRouteBuilder app)
    {
        var routes = app.MapGroup("/api/me/calendar").RequireAuthorization();
        routes.MapGet("", GetCalendarAsync);
        routes.MapPost("/{date}/time-off", MarkDayUnavailableAsync);
    }

    private static async Task<IResult> GetCalendarAsync(
        ClaimsPrincipal user,
        HttpContext http,
        HealthcareDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var identity = AccountIdentity.FromPrincipal(user);
        if (identity is null) return Results.Challenge();

        var provider = await FindProviderAsync(identity, db, cancellationToken);
        if (provider is null) return Results.Forbid();
        if (!TryGetTimeZone(provider.TimeZoneId, out var timeZone)) return MissingTimeZone();

        var now = clock.GetUtcNow();
        var today = LocalDate(now, timeZone);
        var windowStart = DayStartUtc(today, timeZone);
        var windowEnd = DayStartUtc(today.AddDays(CalendarDays), timeZone);

        var weeklyHours = await db.ProviderWeeklyHours.AsNoTracking()
            .Where(hours => hours.ProviderId == provider.Id)
            .ToDictionaryAsync(hours => hours.DayOfWeek, cancellationToken);
        var timeOff = await db.ProviderTimeOff.AsNoTracking()
            .Where(block => block.ProviderId == provider.Id
                && block.StartUtc < windowEnd && windowStart < block.EndUtc)
            .Select(block => new { block.PublicId, block.StartUtc, block.EndUtc })
            .ToListAsync(cancellationToken);
        var appointments = await (
            from appointment in db.Appointments
            join service in db.ClinicalServices on appointment.ClinicalServiceId equals service.Id
            join patient in db.Patients on appointment.PatientId equals patient.Id
            where appointment.ProviderId == provider.Id
                && appointment.Status == AppointmentStatus.Confirmed
                && appointment.StartUtc >= windowStart && appointment.StartUtc < windowEnd
            orderby appointment.StartUtc
            select new
            {
                appointment.PublicId,
                appointment.StartUtc,
                ServiceName = service.Name,
                PatientName = patient.FirstName + " " + patient.LastName,
            }
        ).ToListAsync(cancellationToken);
        var bookingsByDate = appointments.ToLookup(item => LocalDate(item.StartUtc, timeZone));

        // Build every day from three bounded reads instead of querying for each cell.
        var days = Enumerable.Range(0, CalendarDays).Select(offset =>
        {
            var date = today.AddDays(offset);
            var dayStart = DayStartUtc(date, timeZone);
            var dayEnd = DayStartUtc(date.AddDays(1), timeZone);
            var blocks = timeOff.Where(block => block.StartUtc < dayEnd && dayStart < block.EndUtc).ToArray();
            var fullDay = blocks.FirstOrDefault(block => block.StartUtc == dayStart && block.EndUtc == dayEnd);
            var dayOff = blocks.Any(block => block.StartUtc <= dayStart && block.EndUtc >= dayEnd);
            var bookings = bookingsByDate[date].ToArray();
            var bookedStarts = bookings.Select(item => item.StartUtc).ToHashSet();
            var weekday = (int)date.DayOfWeek;
            var hours = weeklyHours.GetValueOrDefault(weekday) ?? ProviderWeeklyHours.CreateDefault(provider.Id, weekday);
            var unbookedSlots = ProviderWeeklyHours.IsWeekday(weekday)
                ? AppointmentAvailability.GetCandidates(date, hours, timeZone)
                    .Where(slot => !bookedStarts.Contains(slot.StartUtc)
                        && !blocks.Any(block => block.StartUtc < slot.EndUtc && slot.StartUtc < block.EndUtc))
                    .ToArray()
                : [];
            var availableSlots = unbookedSlots.Where(slot => slot.StartUtc > now).ToArray();

            return new
            {
                Date = date,
                OpenSlots = availableSlots.Length,
                AvailableSlots = availableSlots,
                StartedSlots = unbookedSlots.Where(slot => slot.StartUtc <= now).Select(slot => new
                {
                    slot.StartUtc,
                    slot.EndUtc,
                    HasEnded = slot.EndUtc <= now,
                }),
                DayOff = dayOff,
                DayOffPublicId = fullDay?.PublicId,
                CanMarkUnavailable = !dayOff && dayEnd > dayStart && dayEnd > now,
                Appointments = bookings.Select(item => new
                {
                    item.PublicId,
                    item.StartUtc,
                    EndUtc = item.StartUtc.AddMinutes(Appointment.DurationMinutes),
                    item.ServiceName,
                    item.PatientName,
                    DuringTimeOff = blocks.Any(block => block.StartUtc < item.StartUtc.AddMinutes(Appointment.DurationMinutes)
                        && item.StartUtc < block.EndUtc),
                }),
            };
        });

        http.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new { provider.TimeZoneId, Today = today, Days = days });
    }

    private static async Task<IResult> MarkDayUnavailableAsync(
        string date,
        ClaimsPrincipal user,
        HealthcareDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var identity = AccountIdentity.FromPrincipal(user);
        if (identity is null) return Results.Challenge();

        var provider = await FindProviderAsync(identity, db, cancellationToken);
        if (provider is null) return Results.Forbid();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await ProviderCalendarLock.TryAcquireAsync(db, provider.Id, cancellationToken))
        {
            return Results.Conflict(new { message = "The provider calendar is busy. Try again." });
        }

        // Recheck the linked provider and use the clinic time zone on the server.
        var currentProvider = await FindProviderAsync(identity, db, cancellationToken);
        if (currentProvider?.Id != provider.Id) return Results.Forbid();
        if (!TryGetTimeZone(currentProvider.TimeZoneId, out var timeZone)) return MissingTimeZone();

        var today = LocalDate(clock.GetUtcNow(), timeZone);
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var selected)
            || selected < today || selected >= today.AddDays(CalendarDays))
        {
            return Results.BadRequest(new { message = "Choose a date within the next 90 calendar days." });
        }

        var start = DayStartUtc(selected, timeZone);
        var end = DayStartUtc(selected.AddDays(1), timeZone);
        if (start >= end) return Results.BadRequest(new { message = "This date is unavailable in the clinic’s time zone." });

        // Preserve existing partial blocks so removing the full day restores the previous availability.
        if (await db.ProviderTimeOff.AnyAsync(block => block.ProviderId == provider.Id
            && block.StartUtc <= start && block.EndUtc >= end, cancellationToken))
        {
            return Results.NoContent();
        }

        db.ProviderTimeOff.Add(new ProviderTimeOff { ProviderId = provider.Id, StartUtc = start, EndUtc = end });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    private static Task<ProviderCalendarOwner?> FindProviderAsync(
        AccountIdentity identity, HealthcareDbContext db, CancellationToken cancellationToken) =>
        (from provider in identity.QueryOwnProviders(db)
         join clinic in db.Clinics on provider.ClinicId equals clinic.Id
         select new ProviderCalendarOwner(provider.Id, clinic.TimeZoneId))
        .SingleOrDefaultAsync(cancellationToken);

    private static bool TryGetTimeZone(string? id, out TimeZoneInfo timeZone)
    {
        if (id is not null && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var resolved))
        {
            timeZone = resolved;
            return true;
        }

        timeZone = TimeZoneInfo.Utc;
        return false;
    }

    private static IResult MissingTimeZone() =>
        Results.Conflict(new { message = "Set the clinic’s time zone before using the calendar." });

    private static DateOnly LocalDate(DateTimeOffset instant, TimeZoneInfo timeZone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, timeZone).DateTime);

    private static DateTimeOffset DayStartUtc(DateOnly date, TimeZoneInfo timeZone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue);
        // Use the first valid instant if a time-zone transition skips local midnight.
        while (timeZone.IsInvalidTime(local)) local = local.AddMinutes(1);
        var offset = timeZone.IsAmbiguousTime(local)
            ? timeZone.GetAmbiguousTimeOffsets(local).Max()
            : timeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    private sealed record ProviderCalendarOwner(int Id, string? TimeZoneId);
}
