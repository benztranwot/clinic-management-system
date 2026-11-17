namespace HealthcareApp.Api.Models;

public class DemoEmailChallenge : IWorkspaceRecord
{
    public long Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public required string Issuer { get; set; }
    public required string Subject { get; set; }
    public required byte[] TokenHash { get; set; }
    public byte[]? CodeSalt { get; set; }
    public int FailedAttempts { get; set; }
    public DateTimeOffset RequestedAtUtc { get; set; }
    public DateTimeOffset? ConsumedAtUtc { get; set; }
}
