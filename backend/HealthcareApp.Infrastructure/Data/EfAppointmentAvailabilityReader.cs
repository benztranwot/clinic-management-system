using HealthcareApp.Api.Data;
using HealthcareApp.Api.Models;
using HealthcareApp.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HealthcareApp.Infrastructure.Data;

public sealed class EfAppointmentAvailabilityReader(HealthcareDbContext db) : IAppointmentAvailabilityReader
{
    public async Task<IReadOnlyDictionary<int, ProviderWeeklyHours>> GetWeeklyHoursAsync(
        int providerId, CancellationToken cancellationToken
    ) => await db.ProviderWeeklyHours.AsNoTracking()
        .Where(day => day.ProviderId == providerId)
        .ToDictionaryAsync(day => day.DayOfWeek, cancellationToken);

    public async Task<IReadOnlyList<TimeOffInterval>> GetTimeOffAsync(
        int providerId, DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken cancellationToken
    ) => await db.ProviderTimeOff
        .Where(block => block.ProviderId == providerId && block.StartUtc < endUtc && startUtc < block.EndUtc)
        .Select(block => new TimeOffInterval(block.StartUtc, block.EndUtc))
        .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DateTimeOffset>> GetConfirmedStartsAsync(
        int providerId, DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken cancellationToken
    ) => await db.Appointments
        .Where(appointment =>
            appointment.ProviderId == providerId
            && appointment.Status == AppointmentStatus.Confirmed
            && appointment.StartUtc >= startUtc
            && appointment.StartUtc < endUtc)
        .Select(appointment => appointment.StartUtc)
        .ToListAsync(cancellationToken);
}
