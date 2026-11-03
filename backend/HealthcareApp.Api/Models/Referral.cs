namespace HealthcareApp.Api.Models;

public class Referral
{
    public int Id { get; set; }

    public Guid PublicId { get; set; } = Guid.NewGuid();

    public int PatientId { get; set; }

    public int ClinicId { get; set; }

    public required string ReferringProviderName { get; set; }

    public required string Reason { get; set; }

    public ReferralStatus Status { get; set; } = ReferralStatus.PendingReview;

    public string? MissingInformationNote { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
