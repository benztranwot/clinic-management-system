namespace HealthcareApp.Api.Dtos;

// The referral supplies the patient and service for this booking.
public record CreateReferredAppointmentRequest(Guid ProviderPublicId, string? StartUtc);
