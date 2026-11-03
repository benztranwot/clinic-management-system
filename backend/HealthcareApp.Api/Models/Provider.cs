namespace HealthcareApp.Api.Models;

// Store a provider and the clinic they belong to.
public class Provider
{
    // Use this ID as the primary key for database relationships.
    public int Id { get; set; }

    // Use this public ID in API requests and responses.
    public Guid PublicId { get; set; } = Guid.NewGuid();

    public int ClinicId { get; set; }

    public required string FirstName { get; set; }

    public required string LastName { get; set; }
}
