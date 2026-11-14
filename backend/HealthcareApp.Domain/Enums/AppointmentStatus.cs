namespace HealthcareApp.Api.Models;

// Keep these numeric values stable because they are stored in SQL Server.
public enum AppointmentStatus
{
    Confirmed = 1,
    Cancelled = 2,
    Completed = 3,
}
