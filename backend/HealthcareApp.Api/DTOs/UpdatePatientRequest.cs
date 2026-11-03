namespace HealthcareApp.Api.Dtos;

// Define the fields a caller can update on an existing patient.
public record UpdatePatientRequest(string? FirstName, string? LastName, DateOnly? DateOfBirth);
