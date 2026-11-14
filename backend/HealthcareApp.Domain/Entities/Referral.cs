namespace HealthcareApp.Api.Models;

public class Referral
{
    // Use this ID as the primary key for database relationships.
    public int Id { get; set; }

    // Use this public ID in API requests and responses.
    public Guid PublicId { get; set; } = Guid.NewGuid();

    public int PatientId { get; set; }

    public int ClinicalServiceId { get; set; }

    // Leave the provider unset until someone is assigned to the referral.
    public required int AssignedProviderId { get; set; }

    public required string ReferringProviderName { get; set; }

    public required string Reason { get; set; }

    public ReferralStatus Status { get; set; } = ReferralStatus.PendingReview;

    public string? MissingInformationNote { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
