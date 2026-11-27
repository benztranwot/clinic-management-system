using HealthcareApp.Api.Models;
using HealthcareApp.Application.Interfaces;

namespace HealthcareApp.Application.Scheduling;

public sealed class AppointmentAvailability(IAppointmentAvailabilityReader reader)
{
    private const int DaysAhead = 30;

    public static bool IsInBookingWindow(DateOnly date, TimeZoneInfo timeZone, DateTimeOffset now)
    {
        var (first, last) = GetBookingWindow(timeZone, now);
        return date >= first && date <= last;
    }

    public static (DateOnly First, DateOnly Last) GetBookingWindow(TimeZoneInfo timeZone, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime);
        return (today.AddDays(1), today.AddDays(DaysAhead));
    }

    public async Task<AppointmentSlot[]> FindAsync(
        int providerId,
        DateOnly date,
        TimeZoneInfo timeZone,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        var days = await FindDaysAsync(providerId, date, date, timeZone, now, cancellationToken);
        return days[0].Slots;
    }

    public async Task<AppointmentAvailabilityDay[]> FindDaysAsync(
        int providerId,
        DateOnly first,
        DateOnly last,
        TimeZoneInfo timeZone,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        var dates = Enumerable.Range(0, last.DayNumber - first.DayNumber + 1)
            .Select(first.AddDays).ToArray();
        if (dates.All(date => !ProviderWeeklyHours.IsWeekday((int)date.DayOfWeek)))
        {
            return dates.Select(date => new AppointmentAvailabilityDay(date, "OutsideWorkingHours", [])).ToArray();
        }

        var weeklyHours = await reader.GetWeeklyHoursAsync(providerId, cancellationToken);
        var candidatesByDate = dates.Select(date =>
        {
            var weekday = (int)date.DayOfWeek;
            var hours = weeklyHours.GetValueOrDefault(weekday) ?? ProviderWeeklyHours.CreateDefault(providerId, weekday);
            var slots = ProviderWeeklyHours.IsWeekday(weekday)
                ? GetCandidates(date, hours, timeZone).Where(slot => slot.StartUtc > now).ToArray()
                : [];
            return new { Date = date, Slots = slots };
        }).ToArray();

        var candidates = candidatesByDate.SelectMany(day => day.Slots).ToArray();
        if (candidates.Length == 0)
        {
            return dates.Select(date => new AppointmentAvailabilityDay(date, "OutsideWorkingHours", [])).ToArray();
        }

        var firstStart = candidates.Min(slot => slot.StartUtc);
        var lastEnd = candidates.Max(slot => slot.EndUtc);
        var timeOff = await reader.GetTimeOffAsync(providerId, firstStart, lastEnd, cancellationToken);
        var bookings = await reader.GetConfirmedStartsAsync(providerId, firstStart, lastEnd, cancellationToken);
        var bookedStarts = bookings.ToHashSet();

        return candidatesByDate.Select(day =>
        {
            if (day.Slots.Length == 0) return new AppointmentAvailabilityDay(day.Date, "OutsideWorkingHours", []);
            var workingSlots = day.Slots.Where(slot =>
                !timeOff.Any(block => block.StartUtc < slot.EndUtc && slot.StartUtc < block.EndUtc)).ToArray();
            if (workingSlots.Length == 0) return new AppointmentAvailabilityDay(day.Date, "DayOff", []);

            var openSlots = workingSlots.Where(slot => !bookedStarts.Contains(slot.StartUtc)).ToArray();
            return new AppointmentAvailabilityDay(day.Date, openSlots.Length == 0 ? "FullyBooked" : "Available", openSlots);
        }).ToArray();
    }

    public static AppointmentSlot[] GetCandidates(
        DateOnly date,
        ProviderWeeklyHours hours,
        TimeZoneInfo timeZone
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

        // A UTC slot boundary can fall between local half hours, and its offset can change during the day.
        for (var minute = start; minute + duration <= end; minute++)
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
                if (startUtc.Ticks % slotTicks == 0
                    && TimeZoneInfo.ConvertTime(endUtc, timeZone).DateTime == localStart.AddMinutes(duration))
                {
                    slots.Add(new AppointmentSlot(startUtc, endUtc));
                }
            }
        }

        return slots.OrderBy(slot => slot.StartUtc).ToArray();
    }
}

public sealed record AppointmentSlot(DateTimeOffset StartUtc, DateTimeOffset EndUtc);
public sealed record AppointmentAvailabilityDay(DateOnly Date, string Status, AppointmentSlot[] Slots);
