using HealthcareApp.Api.Models;

namespace HealthcareApp.Application.Interfaces;

public interface IAppointmentAvailabilityReader
{
    Task<IReadOnlyDictionary<int, ProviderWeeklyHours>> GetWeeklyHoursAsync(
        int providerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TimeOffInterval>> GetTimeOffAsync(
        int providerId, DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken cancellationToken);

    Task<IReadOnlyList<DateTimeOffset>> GetConfirmedStartsAsync(
        int providerId, DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken cancellationToken);
}

public sealed record TimeOffInterval(DateTimeOffset StartUtc, DateTimeOffset EndUtc);
