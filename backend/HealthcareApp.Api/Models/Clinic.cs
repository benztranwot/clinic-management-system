namespace HealthcareApp.Api.Models;

public class Clinic
{
    public int Id { get; set; }

    public Guid PublicId { get; set; } = Guid.NewGuid();

    public required string Name { get; set; }
}
