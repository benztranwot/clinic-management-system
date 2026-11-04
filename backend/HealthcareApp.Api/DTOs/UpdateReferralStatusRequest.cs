namespace HealthcareApp.Api.Dtos;

// Define the requested status and an optional note explaining what is missing.
public record UpdateReferralStatusRequest(string? Status, string? MissingInformationNote = null);
