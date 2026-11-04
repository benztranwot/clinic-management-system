namespace HealthcareApp.Api.Dtos;

// Define the provider to assign to an existing referral.
public record AssignReferralProviderRequest(Guid ProviderPublicId);
