namespace HealthcareApp.Api.Dtos;

// Define the fields needed to create a referral with an eligible receiving provider.
public record CreateReferralRequest(
    Guid PatientPublicId,
    Guid ClinicalServicePublicId,
    Guid AssignedProviderPublicId,
    string? Reason
);
