namespace HealthcareApp.Api.Models;

public class Patient
{
    public int Id { get; set; }

    public Guid PublicId { get; set; } = Guid.NewGuid();

    public required string FirstName { get; set; }

    public required string LastName { get; set; }

    public DateOnly DateOfBirth { get; set; }
}
