namespace HealthcareApp.Api.Models;

// Record a fixed-length booking for a patient and provider.
public class Appointment
{
    public const int DurationMinutes = 30;

    public int Id { get; set; }

    public Guid PublicId { get; set; } = Guid.NewGuid();

    public int PatientId { get; set; }
    public int ClinicalServiceId { get; set; }
    public int ProviderId { get; set; }

    // Direct bookings have no referral.
    public int? ReferralId { get; set; }

    public DateTimeOffset StartUtc { get; set; }
    public AppointmentStatus Status { get; set; } = AppointmentStatus.Confirmed;

    public int BookedByAppAccountId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    // SQL Server changes this value whenever the booking is updated.
    public byte[] RowVersion { get; set; } = [];
}
