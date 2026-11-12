namespace HealthcareApp.Api.Dtos;

// Define the JSON fields accepted when creating a clinic.
public record CreateClinicRequest(string? Name, string? Address, string? TimeZoneId);
