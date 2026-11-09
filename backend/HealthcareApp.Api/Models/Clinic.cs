namespace HealthcareApp.Api.Models;

// Represent a clinic that has providers and offers clinical services.
public class Clinic
{
    // Use this as the primary key for database relationships.
    public int Id { get; set; }

    // Expose this ID in clinic URLs and responses.
    public Guid PublicId { get; set; } = Guid.NewGuid();

    public required string Name { get; set; }

    // Scheduling settings are filled in when a clinic is configured for booking.
    public string? Address { get; set; }

    public string? TimeZoneId { get; set; }
}
