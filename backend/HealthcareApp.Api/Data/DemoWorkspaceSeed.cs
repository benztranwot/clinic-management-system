using HealthcareApp.Api.Models;

namespace HealthcareApp.Api.Data;

internal static class DemoWorkspaceSeed
{
    public static async Task CreateAsync(
        HealthcareDbContext db,
        int administratorId,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        var clinic = new Clinic
        {
            Name = "Northside Demo Clinic",
            Address = "100 Demo Avenue",
            TimeZoneId = "America/Chicago",
        };
        var alex = new Patient { FirstName = "Alex", LastName = "Demo", DateOfBirth = new DateOnly(2000, 1, 1) };
        var jamie = new Patient { FirstName = "Jamie", LastName = "Demo", DateOfBirth = new DateOnly(1998, 4, 12) };
        var riley = new Patient { FirstName = "Riley", LastName = "Demo", DateOfBirth = new DateOnly(1994, 6, 18) };
        db.AddRange(clinic, alex, jamie, riley);
        await db.SaveChangesAsync(cancellationToken);

        var beth = new Provider { ClinicId = clinic.Id, FirstName = "Beth", LastName = "Demo" };
        var john = new Provider { ClinicId = clinic.Id, FirstName = "John", LastName = "Demo" };
        var exam = new ClinicalService { ClinicId = clinic.Id, Name = "Physical exam", AllowsDirectBooking = true };
        var therapy = new ClinicalService { ClinicId = clinic.Id, Name = "Therapy" };
        db.AddRange(beth, john, exam, therapy);
        await db.SaveChangesAsync(cancellationToken);

        db.ProviderServices.AddRange(
            new ProviderService { ClinicId = clinic.Id, ProviderId = beth.Id, ClinicalServiceId = exam.Id },
            new ProviderService { ClinicId = clinic.Id, ProviderId = beth.Id, ClinicalServiceId = therapy.Id },
            new ProviderService { ClinicId = clinic.Id, ProviderId = john.Id, ClinicalServiceId = exam.Id }
        );
        await db.SaveChangesAsync(cancellationToken);

        var missingInfo = ReferralFor(jamie, exam, john, "Follow-up physical exam", ReferralStatus.MissingInformation, now);
        missingInfo.MissingInformationNote = "Please include the prior assessment.";
        var scheduled = ReferralFor(riley, exam, john, "Physical exam after referral", ReferralStatus.ReadyToSchedule, now);
        db.Referrals.AddRange(
            ReferralFor(alex, exam, john, "Routine physical exam", ReferralStatus.Closed, now.AddDays(-7)),
            ReferralFor(jamie, therapy, beth, "Therapy assessment", ReferralStatus.PendingReview, now),
            ReferralFor(alex, therapy, beth, "Follow-up therapy assessment", ReferralStatus.ReadyToSchedule, now),
            missingInfo,
            scheduled
        );
        await db.SaveChangesAsync(cancellationToken);

        db.Appointments.Add(new Appointment
        {
            PatientId = riley.Id,
            ClinicalServiceId = exam.Id,
            ProviderId = john.Id,
            ReferralId = scheduled.Id,
            BookedByAppAccountId = administratorId,
            StartUtc = NextAppointment(now, clinic.TimeZoneId),
            CreatedAtUtc = now,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static Referral ReferralFor(
        Patient patient,
        ClinicalService service,
        Provider provider,
        string reason,
        ReferralStatus status,
        DateTimeOffset createdAt
    ) => new()
    {
        PatientId = patient.Id,
        ClinicalServiceId = service.Id,
        AssignedProviderId = provider.Id,
        ReferringProviderName = "Dr. Demo",
        Reason = reason,
        Status = status,
        CreatedAtUtc = createdAt,
    };

    private static DateTimeOffset NextAppointment(DateTimeOffset now, string timeZoneId)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime).AddDays(1);
        while (!ProviderWeeklyHours.IsWeekday((int)localDate.DayOfWeek))
        {
            localDate = localDate.AddDays(1);
        }

        return TimeZoneInfo.ConvertTimeToUtc(localDate.ToDateTime(new TimeOnly(9, 0)), timeZone);
    }
}
