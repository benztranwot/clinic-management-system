namespace HealthcareApp.Api.Dtos;

public sealed record EditReferralRequest(
    Guid ExpectedProviderPublicId,
    string? ExpectedStatus,
    string? ExpectedMissingInformationNote,
    Guid? ProviderPublicId,
    string? Status,
    string? MissingInformationNote
);
