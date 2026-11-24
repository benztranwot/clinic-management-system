namespace HealthcareApp.Api.Models;

// Link a provider to a service they offer at the same clinic.
public class ProviderService : IWorkspaceRecord
{
    public Guid WorkspaceId { get; set; }

    // Use the provider and service IDs together as the primary key.
    public int ProviderId { get; set; }

    public int ClinicalServiceId { get; set; }

    // Require the provider and service to belong to this clinic.
    public int ClinicId { get; set; }
}
