namespace HealthcareApp.Api.Models;

public class ClinicalService
{
    public int Id { get; set; }

    public Guid PublicId { get; set; } = Guid.NewGuid();

    public int ClinicId { get; set; }

    public required string Name { get; set; }
}
