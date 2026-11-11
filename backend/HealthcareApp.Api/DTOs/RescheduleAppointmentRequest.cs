namespace HealthcareApp.Api.Dtos;

// Include the original start so a stale screen cannot overwrite another change.
public record RescheduleAppointmentRequest(string? ExpectedStartUtc, string? StartUtc);
