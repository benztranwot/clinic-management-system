using HealthcareApp.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HealthcareApp.Api.Data;

public class HealthcareDbContext : DbContext
{
    public HealthcareDbContext(DbContextOptions<HealthcareDbContext> options)
        : base(options) { }

    // Expose the tables the API can query and update through EF Core.
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<Clinic> Clinics => Set<Clinic>();
    public DbSet<Provider> Providers => Set<Provider>();
    public DbSet<ClinicalService> ClinicalServices => Set<ClinicalService>();
    public DbSet<ProviderService> ProviderServices => Set<ProviderService>();
    public DbSet<Referral> Referrals => Set<Referral>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<AppAccount> AppAccounts => Set<AppAccount>();
    public DbSet<ClinicMembership> ClinicMemberships => Set<ClinicMembership>();
    public DbSet<ProviderWeeklyHours> ProviderWeeklyHours => Set<ProviderWeeklyHours>();
    public DbSet<ProviderTimeOff> ProviderTimeOff => Set<ProviderTimeOff>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure the patient's unique public ID and required names.
        var patient = modelBuilder.Entity<Patient>();
        patient.HasIndex(p => p.PublicId).IsUnique();
        patient.Property(p => p.FirstName).HasMaxLength(100).IsRequired();
        patient.Property(p => p.LastName).HasMaxLength(100).IsRequired();

        // Configure the clinic's unique public ID and required name.
        var clinic = modelBuilder.Entity<Clinic>();
        clinic.HasIndex(c => c.PublicId).IsUnique();
        clinic.Property(c => c.Name).HasMaxLength(200).IsRequired();
        clinic.Property(c => c.Address).HasMaxLength(300);
        clinic.Property(c => c.TimeZoneId).HasMaxLength(100);

        // Configure provider names and require each provider to belong to a clinic.
        var provider = modelBuilder.Entity<Provider>();
        provider.HasIndex(p => p.PublicId).IsUnique();
        provider.Property(p => p.FirstName).HasMaxLength(100).IsRequired();
        provider.Property(p => p.LastName).HasMaxLength(100).IsRequired();

        // Each provider belongs to one clinic, and a clinic can have many providers.
        provider
            .HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(p => p.ClinicId)
            .OnDelete(DeleteBehavior.Restrict);

        // Store a working window and an optional recurring break for each provider and day.
        var weeklyHours = modelBuilder.Entity<ProviderWeeklyHours>();
        weeklyHours.HasKey(hours => new { hours.ProviderId, hours.DayOfWeek });
        weeklyHours.ToTable(table =>
        {
            table.HasCheckConstraint("CK_ProviderWeeklyHours_Day", "[DayOfWeek] BETWEEN 0 AND 6");
            table.HasCheckConstraint(
                "CK_ProviderWeeklyHours_Window",
                """
                ([StartMinute] IS NULL AND [EndMinute] IS NULL)
                OR ([StartMinute] IS NOT NULL AND [EndMinute] IS NOT NULL
                    AND [StartMinute] >= 0 AND [EndMinute] <= 1440
                    AND [StartMinute] < [EndMinute]
                    AND [StartMinute] % 30 = 0 AND [EndMinute] % 30 = 0)
                """
            );
            table.HasCheckConstraint(
                "CK_ProviderWeeklyHours_Break",
                $"""
                ([BreakStartMinute] IS NULL AND [BreakEndMinute] IS NULL)
                OR ([BreakStartMinute] IS NOT NULL AND [BreakEndMinute] IS NOT NULL
                    AND [StartMinute] IS NOT NULL AND [EndMinute] IS NOT NULL
                    AND [StartMinute] < [BreakStartMinute]
                    AND [BreakStartMinute] < [BreakEndMinute]
                    AND [BreakEndMinute] < [EndMinute]
                    AND [BreakStartMinute] % {Appointment.DurationMinutes} = 0
                    AND [BreakEndMinute] % {Appointment.DurationMinutes} = 0)
                """
            );
        });

        weeklyHours
            .HasOne<Provider>()
            .WithMany()
            .HasForeignKey(hours => hours.ProviderId)
            .OnDelete(DeleteBehavior.Cascade);

        // Keep time-off intervals in UTC and index each provider's start times.
        var timeOff = modelBuilder.Entity<ProviderTimeOff>();
        timeOff.HasIndex(block => block.PublicId).IsUnique();
        timeOff.HasIndex(block => new { block.ProviderId, block.StartUtc });
        timeOff.ToTable(table =>
            table.HasCheckConstraint(
                "CK_ProviderTimeOff_Interval",
                "[StartUtc] < [EndUtc] AND DATEPART(TZOFFSET, [StartUtc]) = 0 AND DATEPART(TZOFFSET, [EndUtc]) = 0"
            )
        );

        timeOff
            .HasOne<Provider>()
            .WithMany()
            .HasForeignKey(block => block.ProviderId)
            .OnDelete(DeleteBehavior.Cascade);

        // Require each service to belong to a clinic and give it a unique name within that clinic.
        var service = modelBuilder.Entity<ClinicalService>();
        service.HasIndex(s => s.PublicId).IsUnique();
        service.HasIndex(s => new { s.ClinicId, s.Name }).IsUnique();
        service.Property(s => s.Name).HasMaxLength(200).IsRequired();
        service.Property(s => s.AllowsDirectBooking).HasDefaultValue(false);

        // Each service belongs to one clinic, and a clinic can offer many services.
        service
            .HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(s => s.ClinicId)
            .OnDelete(DeleteBehavior.Restrict);

        // Use the provider and service IDs together to identify an eligibility record.
        var eligibility = modelBuilder.Entity<ProviderService>();
        eligibility.HasKey(e => new { e.ProviderId, e.ClinicalServiceId });

        // Link this eligibility record to a provider at the same clinic.
        eligibility
            .HasOne<Provider>()
            .WithMany()
            .HasForeignKey(e => new { e.ProviderId, e.ClinicId })
            .HasPrincipalKey(p => new { p.Id, p.ClinicId })
            .OnDelete(DeleteBehavior.Restrict);

        // Link this eligibility record to a service at the same clinic.
        eligibility
            .HasOne<ClinicalService>()
            .WithMany()
            .HasForeignKey(e => new { e.ClinicalServiceId, e.ClinicId })
            .HasPrincipalKey(s => new { s.Id, s.ClinicId })
            .OnDelete(DeleteBehavior.Restrict);

        // Configure referral fields, relationships, and rules for active referrals.
        var referral = modelBuilder.Entity<Referral>();
        referral.HasIndex(r => r.PublicId).IsUnique();

        // Allow only one active referral per patient and service while keeping closed referrals as history.
        referral
            .HasIndex(r => new { r.PatientId, r.ClinicalServiceId })
            .IsUnique()
            .HasFilter($"[Status] <> {(int)ReferralStatus.Closed}");

        // Limit stored referral statuses to the values defined in ReferralStatus.
        referral.ToTable(table =>
            table.HasCheckConstraint("CK_Referrals_Status", "[Status] IN (1, 2, 3, 4)")
        );

        referral.Property(r => r.ReferringProviderName).HasMaxLength(200).IsRequired();
        referral.Property(r => r.Reason).HasMaxLength(2000).IsRequired();
        referral.Property(r => r.MissingInformationNote).HasMaxLength(1000);

        // Each referral belongs to one patient, and a patient can have many referrals.
        referral
            .HasOne<Patient>()
            .WithMany()
            .HasForeignKey(r => r.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        // Each referral requests one service, and a service can have many referrals.
        referral
            .HasOne<ClinicalService>()
            .WithMany()
            .HasForeignKey(r => r.ClinicalServiceId)
            .OnDelete(DeleteBehavior.Restrict);

        // Every referral requires a provider who offers the requested service.
        referral
            .HasOne<ProviderService>()
            .WithMany()
            .HasForeignKey(r => new { r.AssignedProviderId, r.ClinicalServiceId })
            .OnDelete(DeleteBehavior.Restrict);

        // Reserve each provider and patient slot while the appointment is confirmed.
        var appointment = modelBuilder.Entity<Appointment>();
        appointment.HasIndex(a => a.PublicId).IsUnique();
        appointment.Property(a => a.RowVersion).IsRowVersion();

        appointment.HasIndex(a => new { a.ProviderId, a.StartUtc })
            .IsUnique()
            .HasDatabaseName("UX_Appointments_ConfirmedProviderSlot")
            .HasFilter($"[Status] = {(int)AppointmentStatus.Confirmed}");

        appointment.HasIndex(a => new { a.PatientId, a.StartUtc })
            .IsUnique()
            .HasDatabaseName("UX_Appointments_ConfirmedPatientSlot")
            .HasFilter($"[Status] = {(int)AppointmentStatus.Confirmed}");

        // Keep cancelled bookings as history and allow a referral to be booked again.
        appointment.HasIndex(a => a.ReferralId)
            .IsUnique()
            .HasDatabaseName("UX_Appointments_NonCancelledReferral")
            .HasFilter($"[ReferralId] IS NOT NULL AND [Status] <> {(int)AppointmentStatus.Cancelled}");

        appointment.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_Appointments_Status",
                $"[Status] IN ({(int)AppointmentStatus.Confirmed}, {(int)AppointmentStatus.Cancelled}, {(int)AppointmentStatus.Completed})"
            );
            table.HasCheckConstraint(
                "CK_Appointments_UtcSlot",
                $"""
                DATEPART(TZOFFSET, [StartUtc]) = 0
                AND DATEPART(MINUTE, [StartUtc]) % {Appointment.DurationMinutes} = 0
                AND DATEPART(SECOND, [StartUtc]) = 0
                AND DATEPART(NANOSECOND, [StartUtc]) = 0
                """
            );
        });

        // Keep historical bookings tied to the original patient, service, and provider.
        appointment
            .HasOne<Patient>()
            .WithMany()
            .HasForeignKey(a => a.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        appointment
            .HasOne<ClinicalService>()
            .WithMany()
            .HasForeignKey(a => a.ClinicalServiceId)
            .OnDelete(DeleteBehavior.Restrict);

        appointment
            .HasOne<Provider>()
            .WithMany()
            .HasForeignKey(a => a.ProviderId)
            .OnDelete(DeleteBehavior.Restrict);

        // A referred booking must match that referral's patient and service.
        appointment
            .HasOne<Referral>()
            .WithMany()
            .HasForeignKey(a => new { a.ReferralId, a.PatientId, a.ClinicalServiceId })
            .HasPrincipalKey(r => new { r.Id, r.PatientId, r.ClinicalServiceId })
            .HasConstraintName("FK_Appointments_Referrals_MatchingPatientService")
            .OnDelete(DeleteBehavior.Restrict);

        appointment
            .HasOne<AppAccount>()
            .WithMany()
            .HasForeignKey(a => a.BookedByAppAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // Identify sign-ins by the case-sensitive issuer and subject pair.
        var account = modelBuilder.Entity<AppAccount>();
        account.HasIndex(a => new { a.Issuer, a.Subject }).IsUnique();
        account.Property(a => a.Issuer).HasMaxLength(255).UseCollation("Latin1_General_100_BIN2");
        account.Property(a => a.Subject).HasMaxLength(255).UseCollation("Latin1_General_100_BIN2");

        // A provider or patient record can belong to at most one account.
        account
            .HasOne<Provider>()
            .WithOne()
            .HasForeignKey<AppAccount>(a => a.ProviderId)
            .OnDelete(DeleteBehavior.Restrict);

        account
            .HasOne<Patient>()
            .WithOne()
            .HasForeignKey<AppAccount>(a => a.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        // An account can hold multiple staff roles, each granted for a specific clinic.
        var membership = modelBuilder.Entity<ClinicMembership>();
        membership.HasKey(m => new { m.AppAccountId, m.ClinicId, m.Role });
        membership.ToTable(table =>
            table.HasCheckConstraint(
                "CK_ClinicMemberships_Role",
                $"[Role] IN ({(int)ClinicStaffRole.Clinician}, {(int)ClinicStaffRole.FrontDesk})"
            )
        );

        membership
            .HasOne<AppAccount>()
            .WithMany()
            .HasForeignKey(m => m.AppAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        membership
            .HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(m => m.ClinicId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
