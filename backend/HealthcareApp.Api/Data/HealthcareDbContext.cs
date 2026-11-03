using HealthcareApp.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HealthcareApp.Api.Data;

public class HealthcareDbContext : DbContext
{
    public HealthcareDbContext(DbContextOptions<HealthcareDbContext> options)
        : base(options) { }

    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<Clinic> Clinics => Set<Clinic>();
    public DbSet<Provider> Providers => Set<Provider>();
    public DbSet<ClinicalService> ClinicalServices => Set<ClinicalService>();
    public DbSet<ProviderService> ProviderServices => Set<ProviderService>();
    public DbSet<Referral> Referrals => Set<Referral>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var patient = modelBuilder.Entity<Patient>();
        patient.HasIndex(p => p.PublicId).IsUnique();
        patient.Property(p => p.FirstName).HasMaxLength(100).IsRequired();
        patient.Property(p => p.LastName).HasMaxLength(100).IsRequired();

        var clinic = modelBuilder.Entity<Clinic>();
        clinic.HasIndex(c => c.PublicId).IsUnique();
        clinic.Property(c => c.Name).HasMaxLength(200).IsRequired();

        var provider = modelBuilder.Entity<Provider>();
        provider.HasIndex(p => p.PublicId).IsUnique();
        provider.Property(p => p.FirstName).HasMaxLength(100).IsRequired();
        provider.Property(p => p.LastName).HasMaxLength(100).IsRequired();

        provider
            .HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(p => p.ClinicId)
            .OnDelete(DeleteBehavior.Restrict);

        var service = modelBuilder.Entity<ClinicalService>();
        service.HasIndex(s => s.PublicId).IsUnique();
        service.HasIndex(s => new { s.ClinicId, s.Name }).IsUnique();
        service.Property(s => s.Name).HasMaxLength(200).IsRequired();

        service
            .HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(s => s.ClinicId)
            .OnDelete(DeleteBehavior.Restrict);

        var eligibility = modelBuilder.Entity<ProviderService>();
        eligibility.HasKey(e => new { e.ProviderId, e.ClinicalServiceId });

        // Both ends of an eligibility record must belong to the same clinic.
        eligibility
            .HasOne<Provider>()
            .WithMany()
            .HasForeignKey(e => new { e.ProviderId, e.ClinicId })
            .HasPrincipalKey(p => new { p.Id, p.ClinicId })
            .OnDelete(DeleteBehavior.Restrict);

        eligibility
            .HasOne<ClinicalService>()
            .WithMany()
            .HasForeignKey(e => new { e.ClinicalServiceId, e.ClinicId })
            .HasPrincipalKey(s => new { s.Id, s.ClinicId })
            .OnDelete(DeleteBehavior.Restrict);

        var referral = modelBuilder.Entity<Referral>();
        referral.HasIndex(r => r.PublicId).IsUnique();

        // Closed referrals remain as history and do not block a new referral.
        referral
            .HasIndex(r => new { r.PatientId, r.ClinicalServiceId })
            .IsUnique()
            .HasFilter($"[Status] <> {(int)ReferralStatus.Closed}");

        referral.ToTable(table =>
            table.HasCheckConstraint("CK_Referrals_Status", "[Status] IN (1, 2, 3, 4)")
        );

        referral.Property(r => r.ReferringProviderName).HasMaxLength(200).IsRequired();
        referral.Property(r => r.Reason).HasMaxLength(2000).IsRequired();
        referral.Property(r => r.MissingInformationNote).HasMaxLength(1000);

        referral
            .HasOne<Patient>()
            .WithMany()
            .HasForeignKey(r => r.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        referral
            .HasOne<ClinicalService>()
            .WithMany()
            .HasForeignKey(r => r.ClinicalServiceId)
            .OnDelete(DeleteBehavior.Restrict);

        // A null provider is allowed; an assigned provider must offer this service.
        referral
            .HasOne<ProviderService>()
            .WithMany()
            .HasForeignKey(r => new { r.AssignedProviderId, r.ClinicalServiceId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
