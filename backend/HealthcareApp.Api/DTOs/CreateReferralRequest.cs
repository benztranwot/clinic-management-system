namespace HealthcareApp.Api.Dtos;

// Define the fields needed to create an unassigned referral.
public record CreateReferralRequest(
    Guid PatientPublicId,
    Guid ClinicalServicePublicId,
    string? ReferringProviderName,
    string? Reason
);
