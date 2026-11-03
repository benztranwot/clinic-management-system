namespace HealthcareApp.Api.Models;

// Keep these numeric values in sync with the database status constraint.
public enum ReferralStatus
{
    PendingReview = 1,
    MissingInformation = 2,
    ReadyToSchedule = 3,
    Closed = 4,
}
