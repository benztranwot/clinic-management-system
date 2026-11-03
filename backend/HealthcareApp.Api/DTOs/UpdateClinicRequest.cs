namespace HealthcareApp.Api.Dtos;

// Define the fields a caller can change on an existing clinic.
public record UpdateClinicRequest(string? Name);
