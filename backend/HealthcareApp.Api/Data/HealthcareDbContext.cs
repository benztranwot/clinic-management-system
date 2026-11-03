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

        // Require each service to belong to a clinic and give it a unique name within that clinic.
        var service = modelBuilder.Entity<ClinicalService>();
        service.HasIndex(s => s.PublicId).IsUnique();
        service.HasIndex(s => new { s.ClinicId, s.Name }).IsUnique();
        service.Property(s => s.Name).HasMaxLength(200).IsRequired();

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

        // Allow an unassigned referral. An assigned provider must offer the requested service.
        referral
            .HasOne<ProviderService>()
            .WithMany()
            .HasForeignKey(r => new { r.AssignedProviderId, r.ClinicalServiceId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
