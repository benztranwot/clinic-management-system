namespace HealthcareApp.Api.Dtos;

// Define the JSON fields accepted when creating a provider.
public record CreateProviderRequest(string? FirstName, string? LastName);
