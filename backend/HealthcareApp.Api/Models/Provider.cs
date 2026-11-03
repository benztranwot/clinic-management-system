namespace HealthcareApp.Api.Models;

public class Provider
{
    public int Id { get; set; }

    public Guid PublicId { get; set; } = Guid.NewGuid();

    public int ClinicId { get; set; }

    public required string FirstName { get; set; }

    public required string LastName { get; set; }
}
