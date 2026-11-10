namespace HealthcareApp.Api.Models;

// Block a provider's calendar for a specific interval.
public class ProviderTimeOff
{
    public int Id { get; set; }

    public Guid PublicId { get; set; } = Guid.NewGuid();

    public int ProviderId { get; set; }

    // Treat the end as exclusive when comparing this block with appointment slots.
    public DateTimeOffset StartUtc { get; set; }
    public DateTimeOffset EndUtc { get; set; }
}
