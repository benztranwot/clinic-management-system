namespace HealthcareApp.Api.Dtos;

// Define the fields a caller can change on an existing clinic.
public record UpdateClinicRequest(
    string? Name,
    string? Address,
    string? TimeZoneId,
    string? ExpectedAddress = null,
    string? ExpectedTimeZoneId = null
);
