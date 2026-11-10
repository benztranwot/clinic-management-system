using HealthcareApp.Api.Data;
using HealthcareApp.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HealthcareApp.Api.Scheduling;

internal static class AppointmentAvailability
{
    private const int DaysAhead = 30;

    public static bool IsInBookingWindow(DateOnly date, TimeZoneInfo timeZone, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime);
        return date >= today && date <= today.AddDays(DaysAhead);
    }

    public static async Task<AppointmentSlot[]> FindAsync(
        HealthcareDbContext db,
        int providerId,
        DateOnly date,
        TimeZoneInfo timeZone,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        var dayOfWeek = (int)date.DayOfWeek;
        if (!ProviderWeeklyHours.IsWeekday(dayOfWeek))
        {
            return [];
        }

        var hours = await db.ProviderWeeklyHours.AsNoTracking()
            .SingleOrDefaultAsync(
                day => day.ProviderId == providerId && day.DayOfWeek == dayOfWeek,
                cancellationToken
            ) ?? ProviderWeeklyHours.CreateDefault(providerId, dayOfWeek);

        var candidates = GetCandidates(date, hours, timeZone, now);
        if (candidates.Length == 0)
        {
            return [];
        }

        var firstStart = candidates[0].StartUtc;
        var lastEnd = candidates[^1].EndUtc;
        var timeOff = await db.ProviderTimeOff
            .Where(block =>
                block.ProviderId == providerId && block.StartUtc < lastEnd && firstStart < block.EndUtc
            )
            .Select(block => new { block.StartUtc, block.EndUtc })
            .ToListAsync(cancellationToken);
        var bookings = await db.Appointments
            .Where(appointment =>
                appointment.ProviderId == providerId
                && appointment.Status == AppointmentStatus.Confirmed
                && appointment.StartUtc >= firstStart
                && appointment.StartUtc < lastEnd
            )
            .Select(appointment => appointment.StartUtc)
            .ToListAsync(cancellationToken);
        var bookedStarts = bookings.ToHashSet();

        return candidates.Where(slot =>
                !bookedStarts.Contains(slot.StartUtc)
                && !timeOff.Any(block => block.StartUtc < slot.EndUtc && slot.StartUtc < block.EndUtc)
            )
            .ToArray();
    }

    private static AppointmentSlot[] GetCandidates(
        DateOnly date,
        ProviderWeeklyHours hours,
        TimeZoneInfo timeZone,
        DateTimeOffset now
    )
    {
        if (hours.StartMinute is not int start || hours.EndMinute is not int end)
        {
            return [];
        }

        const int duration = Appointment.DurationMinutes;
        const long slotTicks = duration * TimeSpan.TicksPerMinute;
        var localMidnight = date.ToDateTime(TimeOnly.MinValue);
        var slots = new List<AppointmentSlot>();

        for (var minute = start; minute + duration <= end; minute += duration)
        {
            if (hours.BreakStartMinute is int breakStart
                && hours.BreakEndMinute is int breakEnd
                && breakStart < minute + duration
                && minute < breakEnd)
            {
                continue;
            }

            var localStart = localMidnight.AddMinutes(minute);
            if (timeZone.IsInvalidTime(localStart))
            {
                continue;
            }

            // A repeated local time can represent two distinct starts in UTC.
            var offsets = timeZone.IsAmbiguousTime(localStart)
                ? timeZone.GetAmbiguousTimeOffsets(localStart)
                : [timeZone.GetUtcOffset(localStart)];

            foreach (var offset in offsets)
            {
                var startUtc = new DateTimeOffset(localStart, offset).ToUniversalTime();
                var endUtc = startUtc.AddMinutes(duration);
                if (startUtc > now
                    && startUtc.Ticks % slotTicks == 0
                    && TimeZoneInfo.ConvertTime(endUtc, timeZone).DateTime == localStart.AddMinutes(duration))
                {
                    slots.Add(new AppointmentSlot(startUtc, endUtc));
                }
            }
        }

        return slots.OrderBy(slot => slot.StartUtc).ToArray();
    }
}

internal sealed record AppointmentSlot(DateTimeOffset StartUtc, DateTimeOffset EndUtc);
