namespace HealthcareApp.Api.Models;

// A workspace owns one isolated collection of clinical records.
public class Workspace
{
    // Reserved for the operator-run fictional seed. User sessions cannot select it.
    public static readonly Guid VisitorTemplateId = new("00000000-0000-0000-0000-000000000001");

    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAtUtc { get; set; }
}
