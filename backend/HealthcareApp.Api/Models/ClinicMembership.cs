namespace HealthcareApp.Api.Models;

// Grants an account a staff role at a specific clinic.
public class ClinicMembership
{
    public int AppAccountId { get; set; }
    public int ClinicId { get; set; }
    public ClinicStaffRole Role { get; set; }
}
