namespace HealthcareApp.Api.Dtos;

// Book a service that allows visits without a referral.
public record CreateDirectAppointmentRequest(
    Guid PatientPublicId,
    Guid ClinicalServicePublicId,
    Guid ProviderPublicId,
    string? StartUtc
);
