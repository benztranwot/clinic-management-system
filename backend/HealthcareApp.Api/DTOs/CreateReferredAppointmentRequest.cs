namespace HealthcareApp.Api.Dtos;

// The referral supplies the patient, service, and provider. Older clients can echo the provider.
public record CreateReferredAppointmentRequest(string? StartUtc, Guid? ProviderPublicId = null);
