using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HealthcareApp.Api.Data;
using HealthcareApp.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HealthcareApp.Api.IntegrationTests;

public class PatientAppointmentAccessTests
{
    private const string Route = "/api/me/appointments";

    [Fact]
    public async Task GetMyAppointments_RequiresAuthenticationAndReadPermission()
    {
        using var tokens = new TestJwtIssuer();
        await using var factory = new HealthcareApiFactory();
        await using var configuredFactory = tokens.CreateFactory(factory);
        using var client = configuredFactory.CreateClient();

        using var anonymous = await client.GetAsync(Route);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        tokens.Authorize(client, "manage:appointments");
        using var missingRead = await client.GetAsync(Route);
        Assert.Equal(HttpStatusCode.Forbidden, missingRead.StatusCode);
    }

    [Fact]
    public async Task GetMyAppointments_WithReadPermission_ReturnsOnlyLinkedPatientsBookings()
    {
        const string subject = "auth0|patient-with-appointments";
        const string emptySubject = "auth0|patient-without-appointments";
        const string staffSubject = "auth0|staff-without-patient-link";

        using var tokens = new TestJwtIssuer();
        await using var factory = new HealthcareApiFactory();
        await using var configuredFactory = tokens.CreateFactory(factory);
        using var client = configuredFactory.CreateClient();
        await using var scope = configuredFactory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthcareDbContext>();

        var connection = db.Database.GetDbConnection();
        Assert.Equal(@"(localdb)\MSSQLLocalDB", connection.DataSource);
        Assert.Equal(factory.DatabaseName, connection.Database);

        try
        {
            await db.Database.MigrateAsync();

            var clinic = new Clinic
            {
                Name = "Test Clinic", TimeZoneId = "America/Chicago",
                Address = "100 Test Avenue",
            };
            var patient = NewPatient("Taylor");
            var otherPatient = NewPatient("Jordan");
            var emptyPatient = NewPatient("Casey");
            db.AddRange(clinic, patient, otherPatient, emptyPatient);
            await db.SaveChangesAsync();

            var service = new ClinicalService { Name = "Physical Exam", ClinicId = clinic.Id, AllowsDirectBooking = true };
            var provider = new Provider { FirstName = "Avery", LastName = "Demo", ClinicId = clinic.Id };
            var staff = new AppAccount { Issuer = TestJwtIssuer.Issuer, Subject = staffSubject, IsAdministrator = true };
            db.AddRange(service, provider, staff,
                new AppAccount { Issuer = TestJwtIssuer.Issuer, Subject = subject, PatientId = patient.Id },
                new AppAccount { Issuer = TestJwtIssuer.Issuer, Subject = emptySubject, PatientId = emptyPatient.Id });
            await db.SaveChangesAsync();

            var start = new DateTimeOffset(2030, 1, 7, 15, 0, 0, TimeSpan.Zero);
            Appointment Book(Patient owner, DateTimeOffset time) => new()
            {
                PatientId = owner.Id,
                ClinicalServiceId = service.Id,
                ProviderId = provider.Id,
                StartUtc = time,
                BookedByAppAccountId = staff.Id,
            };

            var ownBooking = Book(patient, start);
            db.Appointments.AddRange(ownBooking, Book(otherPatient, start.AddHours(1)));
            await db.SaveChangesAsync();

            tokens.AuthorizeSubject(client, staffSubject, "read:appointments");
            using var unlinked = await client.GetAsync(Route);
            Assert.Equal(HttpStatusCode.Forbidden, unlinked.StatusCode);

            tokens.AuthorizeSubject(client, emptySubject, "read:appointments");
            using var empty = await client.GetAsync(Route);
            Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
            Assert.Equal(0, (await empty.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength());

            tokens.AuthorizeSubject(client, subject, "read:appointments");
            using var response = await client.GetAsync(Route);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            var booking = Assert.Single(body.EnumerateArray());
            Assert.Equal(ownBooking.PublicId, booking.GetProperty("publicId").GetGuid());
            Assert.Equal("Confirmed", booking.GetProperty("status").GetString());
            Assert.Equal(start.AddMinutes(30), booking.GetProperty("endUtc").GetDateTimeOffset());
            Assert.Equal(clinic.TimeZoneId, booking.GetProperty("timeZoneId").GetString());
            Assert.False(booking.TryGetProperty("patientId", out _));
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static Patient NewPatient(string firstName) => new()
    {
        FirstName = firstName,
        LastName = "Demo",
        DateOfBirth = new DateOnly(1990, 1, 1),
    };
}
