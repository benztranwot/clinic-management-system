namespace HealthcareApp.Api.Models;

// Represent a service offered by a specific clinic.
public class ClinicalService
{
    // Use this ID as the primary key for database relationships.
    public int Id { get; set; }

    // Use this public ID in API requests and responses.
    public Guid PublicId { get; set; } = Guid.NewGuid();

    public int ClinicId { get; set; }

    public required string Name { get; set; }

    // Services require a referral unless direct booking is enabled.
    public bool AllowsDirectBooking { get; set; }
}
