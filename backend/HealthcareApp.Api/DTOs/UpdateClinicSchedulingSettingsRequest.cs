namespace HealthcareApp.Api.Dtos;

// Define the location and local time zone used for scheduling.
public record UpdateClinicSchedulingSettingsRequest(string? Address, string? TimeZoneId);
