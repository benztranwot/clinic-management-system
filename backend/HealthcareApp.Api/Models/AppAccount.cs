namespace HealthcareApp.Api.Models;

// Links a verified sign-in to application records.
public class AppAccount
{
    public int Id { get; set; }

    // The issuer and subject together identify the sign-in.
    public required string Issuer { get; set; }
    public required string Subject { get; set; }

    public int? ProviderId { get; set; }
    public int? PatientId { get; set; }

    // Administrators can manage the application across clinics.
    public bool IsAdministrator { get; set; }
}
