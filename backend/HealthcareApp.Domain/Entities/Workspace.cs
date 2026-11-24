namespace HealthcareApp.Api.Models;

// A workspace owns one isolated collection of clinical records.
public class Workspace
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAtUtc { get; set; }
}
