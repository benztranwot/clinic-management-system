namespace HealthcareApp.Api.Dtos;

// Require an explicit choice between direct booking and referral-only booking.
public record UpdateClinicalServiceBookingPolicyRequest(bool? AllowsDirectBooking);
