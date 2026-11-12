namespace HealthcareApp.Api.Dtos;

public sealed record SearchUpcomingAppointmentsRequest(
    string? Clinic,
    string? Service,
    string? Provider,
    string? Patient,
    DateTimeOffset? FromUtc,
    DateTimeOffset? UntilUtc,
    bool LatestFirst = false,
    int Page = 1
);
