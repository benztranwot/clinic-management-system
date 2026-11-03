namespace HealthcareApp.Api.Models;

// Store patient demographics for registration and referral intake.
public class Patient
{
    // Use this ID as the primary key for database relationships.
    public int Id { get; set; }

    // Use this public ID in API requests and responses.
    public Guid PublicId { get; set; } = Guid.NewGuid();

    public required string FirstName { get; set; }

    public required string LastName { get; set; }

    public DateOnly DateOfBirth { get; set; }
}
