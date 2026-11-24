using HealthcareApp.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HealthcareApp.Api.Data;

public class HealthcareDbContext : DbContext
{
    public HealthcareDbContext(DbContextOptions<HealthcareDbContext> options)
        : base(options) { }

    // Empty identifies the original operator-managed dataset.
    public Guid CurrentWorkspaceId { get; private set; }
    private bool workspaceSelected;

    // Only trusted server code selects a workspace; requests cannot supply this value.
    public void SelectWorkspace(Guid workspaceId)
    {
        if (workspaceSelected && CurrentWorkspaceId != workspaceId)
            throw new InvalidOperationException("A data context cannot switch workspaces.");
        if (ChangeTracker.Entries<IWorkspaceRecord>().Any())
            throw new InvalidOperationException("Select the workspace before tracking clinical records.");
        CurrentWorkspaceId = workspaceId;
        workspaceSelected = true;
    }

    public DbSet<Workspace> Workspaces => Set<Workspace>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardWorkspaceWrites();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardWorkspaceWrites();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void GuardWorkspaceWrites()
    {
        foreach (var entry in ChangeTracker.Entries<IWorkspaceRecord>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.WorkspaceId != Guid.Empty && entry.Entity.WorkspaceId != CurrentWorkspaceId)
                    throw new InvalidOperationException("A record cannot be added to another workspace.");
                entry.Entity.WorkspaceId = CurrentWorkspaceId;
            }
            else if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                if (entry.Property(record => record.WorkspaceId).OriginalValue != CurrentWorkspaceId
                    || entry.Entity.WorkspaceId != CurrentWorkspaceId)
                    throw new InvalidOperationException("A record cannot be changed across workspaces.");
            }
        }
    }

    private void ConfigureWorkspace<TEntity>(ModelBuilder modelBuilder) where TEntity : class, IWorkspaceRecord
    {
        var entity = modelBuilder.Entity<TEntity>();
        entity.Property(record => record.WorkspaceId).HasDefaultValue(Guid.Empty);
        entity.HasQueryFilter(record => record.WorkspaceId == CurrentWorkspaceId);
        entity.HasOne<Workspace>().WithMany().HasForeignKey(record => record.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);
    }

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

        // Keep existing records in the original workspace when upgrading.
        var workspace = modelBuilder.Entity<Workspace>();
        workspace.Property(row => row.Id).ValueGeneratedNever();
        workspace.HasQueryFilter(row => row.Id == CurrentWorkspaceId);
        workspace.HasData(new Workspace { Id = Guid.Empty, CreatedAtUtc = DateTimeOffset.UnixEpoch });
        ConfigureWorkspace<Patient>(modelBuilder);
        ConfigureWorkspace<Clinic>(modelBuilder);
        ConfigureWorkspace<Provider>(modelBuilder);
        ConfigureWorkspace<ClinicalService>(modelBuilder);
        ConfigureWorkspace<ProviderService>(modelBuilder);
        ConfigureWorkspace<Referral>(modelBuilder);
        ConfigureWorkspace<Appointment>(modelBuilder);
        ConfigureWorkspace<AppAccount>(modelBuilder);
        ConfigureWorkspace<ClinicMembership>(modelBuilder);
        ConfigureWorkspace<ProviderWeeklyHours>(modelBuilder);
        ConfigureWorkspace<ProviderTimeOff>(modelBuilder);

        // Configure the patient's unique public ID and required names.
        var patient = modelBuilder.Entity<Patient>();
        patient.HasIndex(p => p.PublicId).IsUnique();
        patient.Property(p => p.FirstName).HasMaxLength(100).IsRequired();
        patient.Property(p => p.LastName).HasMaxLength(100).IsRequired();

        // Configure the clinic's unique public ID and required name.
        var clinic = modelBuilder.Entity<Clinic>();
        clinic.HasIndex(c => c.PublicId).IsUnique();
        clinic.Property(c => c.Name).HasMaxLength(200).IsRequired();
        clinic.Property(c => c.Address).HasMaxLength(300).IsRequired();
        clinic.Property(c => c.TimeZoneId).HasMaxLength(100).IsRequired();

        // Configure provider names and require each provider to belong to a clinic.
        var provider = modelBuilder.Entity<Provider>();
        provider.HasIndex(p => p.PublicId).IsUnique();
        provider.Property(p => p.FirstName).HasMaxLength(100).IsRequired();
        provider.Property(p => p.LastName).HasMaxLength(100).IsRequired();

        // Each provider belongs to one clinic, and a clinic can have many providers.
        provider
            .HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(p => new { p.WorkspaceId, p.ClinicId })
            .HasPrincipalKey(c => new { c.WorkspaceId, c.Id })
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
            .HasForeignKey(hours => new { hours.WorkspaceId, hours.ProviderId })
            .HasPrincipalKey(p => new { p.WorkspaceId, p.Id })
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
            .HasForeignKey(block => new { block.WorkspaceId, block.ProviderId })
            .HasPrincipalKey(p => new { p.WorkspaceId, p.Id })
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
            .HasForeignKey(s => new { s.WorkspaceId, s.ClinicId })
            .HasPrincipalKey(c => new { c.WorkspaceId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // Use the provider and service IDs together to identify an eligibility record.
        var eligibility = modelBuilder.Entity<ProviderService>();
        eligibility.HasKey(e => new { e.ProviderId, e.ClinicalServiceId });

        // Link this eligibility record to a provider at the same clinic.
        eligibility
            .HasOne<Provider>()
            .WithMany()
            .HasForeignKey(e => new { e.WorkspaceId, e.ProviderId, e.ClinicId })
            .HasPrincipalKey(p => new { p.WorkspaceId, p.Id, p.ClinicId })
            .OnDelete(DeleteBehavior.Restrict);

        // Link this eligibility record to a service at the same clinic.
        eligibility
            .HasOne<ClinicalService>()
            .WithMany()
            .HasForeignKey(e => new { e.WorkspaceId, e.ClinicalServiceId, e.ClinicId })
            .HasPrincipalKey(s => new { s.WorkspaceId, s.Id, s.ClinicId })
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
            .HasForeignKey(r => new { r.WorkspaceId, r.PatientId })
            .HasPrincipalKey(p => new { p.WorkspaceId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // Each referral requests one service, and a service can have many referrals.
        referral
            .HasOne<ClinicalService>()
            .WithMany()
            .HasForeignKey(r => new { r.WorkspaceId, r.ClinicalServiceId })
            .HasPrincipalKey(s => new { s.WorkspaceId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // Every referral requires a provider who offers the requested service.
        referral
            .HasOne<ProviderService>()
            .WithMany()
            .HasForeignKey(r => new { r.WorkspaceId, r.AssignedProviderId, r.ClinicalServiceId })
            .HasPrincipalKey(e => new { e.WorkspaceId, e.ProviderId, e.ClinicalServiceId })
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
            .HasForeignKey(a => new { a.WorkspaceId, a.PatientId })
            .HasPrincipalKey(p => new { p.WorkspaceId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);

        appointment
            .HasOne<ClinicalService>()
            .WithMany()
            .HasForeignKey(a => new { a.WorkspaceId, a.ClinicalServiceId })
            .HasPrincipalKey(s => new { s.WorkspaceId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);

        appointment
            .HasOne<Provider>()
            .WithMany()
            .HasForeignKey(a => new { a.WorkspaceId, a.ProviderId })
            .HasPrincipalKey(p => new { p.WorkspaceId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // A referred booking must match that referral's patient and service.
        appointment
            .HasOne<Referral>()
            .WithMany()
            .HasForeignKey(a => new { a.WorkspaceId, a.ReferralId, a.PatientId, a.ClinicalServiceId })
            .HasPrincipalKey(r => new { r.WorkspaceId, r.Id, r.PatientId, r.ClinicalServiceId })
            .HasConstraintName("FK_Appointments_Referrals_MatchingPatientService")
            .OnDelete(DeleteBehavior.Restrict);

        appointment
            .HasOne<AppAccount>()
            .WithMany()
            .HasForeignKey(a => new { a.WorkspaceId, a.BookedByAppAccountId })
            .HasPrincipalKey(a => new { a.WorkspaceId, a.Id })
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
            .HasForeignKey<AppAccount>(a => new { a.WorkspaceId, a.ProviderId })
            .HasPrincipalKey<Provider>(p => new { p.WorkspaceId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);

        account
            .HasOne<Patient>()
            .WithOne()
            .HasForeignKey<AppAccount>(a => new { a.WorkspaceId, a.PatientId })
            .HasPrincipalKey<Patient>(p => new { p.WorkspaceId, p.Id })
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
            .HasForeignKey(m => new { m.WorkspaceId, m.AppAccountId })
            .HasPrincipalKey(a => new { a.WorkspaceId, a.Id })
            .OnDelete(DeleteBehavior.Restrict);

        membership
            .HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(m => new { m.WorkspaceId, m.ClinicId })
            .HasPrincipalKey(c => new { c.WorkspaceId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
