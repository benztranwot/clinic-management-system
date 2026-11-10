using HealthcareApp.Api.Data;
using HealthcareApp.Api.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HealthcareApp.Api.IntegrationTests;

public class AppointmentSchemaTests
{
    [Fact]
    public async Task AppointmentSchema_EnforcesSlotsAndKeepsCancelledHistory()
    {
        var start = new DateTimeOffset(2030, 1, 7, 15, 0, 0, TimeSpan.Zero);

        await using var factory = new HealthcareApiFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthcareDbContext>();

        var connection = db.Database.GetDbConnection();
        Assert.Equal(@"(localdb)\MSSQLLocalDB", connection.DataSource);
        Assert.Equal(factory.DatabaseName, connection.Database);

        try
        {
            await db.Database.MigrateAsync();

            var clinic = new Clinic { Name = "Appointment Test Clinic" };
            var firstPatient = new Patient
            {
                FirstName = "Alex",
                LastName = "Demo",
                DateOfBirth = new DateOnly(1990, 3, 5),
            };
            var secondPatient = new Patient
            {
                FirstName = "Jamie",
                LastName = "Demo",
                DateOfBirth = new DateOnly(1992, 8, 20),
            };
            var staff = new AppAccount
            {
                Issuer = TestJwtIssuer.Issuer,
                Subject = "auth0|appointment-schema-test",
            };
            db.AddRange(clinic, firstPatient, secondPatient, staff);
            await db.SaveChangesAsync();

            var firstProvider = new Provider
            {
                ClinicId = clinic.Id, FirstName = "Beth", LastName = "Demo",
            };
            var secondProvider = new Provider
            {
                ClinicId = clinic.Id, FirstName = "John", LastName = "Demo",
            };
            var service = new ClinicalService
            {
                ClinicId = clinic.Id, Name = "Physical Exam", AllowsDirectBooking = true,
            };
            var otherService = new ClinicalService { ClinicId = clinic.Id, Name = "Therapy" };
            db.AddRange(firstProvider, secondProvider, service, otherService);
            await db.SaveChangesAsync();

            var referral = new Referral
            {
                PatientId = firstPatient.Id,
                ClinicalServiceId = service.Id,
                ReferringProviderName = "Dr. Demo",
                Reason = "Fictional referral for schema test",
                Status = ReferralStatus.ReadyToSchedule,
            };
            db.Referrals.Add(referral);
            await db.SaveChangesAsync();

            Appointment Book(
                Patient patient,
                Provider provider,
                DateTimeOffset slot,
                int? referralId = null,
                ClinicalService? bookedService = null
            ) => new()
            {
                PatientId = patient.Id,
                ProviderId = provider.Id,
                ClinicalServiceId = (bookedService ?? service).Id,
                ReferralId = referralId,
                StartUtc = slot,
                BookedByAppAccountId = staff.Id,
            };

            var original = Book(firstPatient, firstProvider, start, referral.Id);
            var direct = Book(secondPatient, secondProvider, start);
            db.Appointments.Add(original);
            await db.SaveChangesAsync();
            Assert.NotEmpty(original.RowVersion);
            var originalVersion = original.RowVersion.ToArray();

            await AssertRejectedAsync(
                db, Book(secondPatient, firstProvider, start), "UX_Appointments_ConfirmedProviderSlot"
            );
            await AssertRejectedAsync(
                db, Book(firstPatient, secondProvider, start), "UX_Appointments_ConfirmedPatientSlot"
            );
            await AssertRejectedAsync(
                db, Book(firstPatient, secondProvider, start.AddHours(1), referral.Id),
                "UX_Appointments_NonCancelledReferral"
            );
            await AssertRejectedAsync(
                db, Book(secondPatient, secondProvider, start.AddMinutes(15)), "CK_Appointments_UtcSlot"
            );
            await AssertRejectedAsync(
                db, Book(secondPatient, secondProvider, start.AddHours(1).ToOffset(TimeSpan.FromHours(-6))),
                "CK_Appointments_UtcSlot"
            );

            // Two bookings can start together when their patients and providers differ.
            db.Appointments.Add(direct);
            await db.SaveChangesAsync();
            Assert.Null(direct.ReferralId);

            original.Status = AppointmentStatus.Cancelled;
            await db.SaveChangesAsync();
            Assert.False(originalVersion.SequenceEqual(original.RowVersion));

            // With the old booking cancelled, the referral foreign key still enforces its patient and service.
            await AssertRejectedAsync(
                db, Book(secondPatient, secondProvider, start.AddHours(1), referral.Id),
                "FK_Appointments_Referrals_MatchingPatientService"
            );
            await AssertRejectedAsync(
                db, Book(firstPatient, secondProvider, start.AddHours(1), referral.Id, otherService),
                "FK_Appointments_Referrals_MatchingPatientService"
            );

            var replacement = Book(firstPatient, firstProvider, start, referral.Id);
            db.Appointments.Add(replacement);
            await db.SaveChangesAsync();
            var history = await db.Appointments.AsNoTracking()
                .Where(appointment => appointment.ReferralId == referral.Id)
                .OrderBy(appointment => appointment.Id)
                .Select(appointment => appointment.Status)
                .ToArrayAsync();
            Assert.Equal(new[] { AppointmentStatus.Cancelled, AppointmentStatus.Confirmed }, history);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static async Task AssertRejectedAsync(
        HealthcareDbContext db,
        Appointment appointment,
        string constraint
    )
    {
        db.Appointments.Add(appointment);
        try
        {
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            var sql = Assert.IsType<SqlException>(exception.InnerException);
            Assert.Contains(constraint, sql.Message);
        }
        finally
        {
            db.Entry(appointment).State = EntityState.Detached;
        }
    }
}
