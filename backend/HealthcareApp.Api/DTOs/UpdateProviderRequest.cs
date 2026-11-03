namespace HealthcareApp.Api.Dtos;

// Define the fields a caller can update on an existing provider.
public record UpdateProviderRequest(string? FirstName, string? LastName);
