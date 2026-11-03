namespace HealthcareApp.Api.Dtos;

// Define the fields a caller can update on an existing clinical service.
public record UpdateClinicalServiceRequest(string? Name);
