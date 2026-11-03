namespace HealthcareApp.Api.Dtos;

// Define the JSON fields accepted when registering a patient.
public record CreatePatientRequest(string? FirstName, string? LastName, DateOnly? DateOfBirth);
