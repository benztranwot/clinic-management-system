using HealthcareApp.Api.Data;
using HealthcareApp.Api.Dtos;
using HealthcareApp.Api.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HealthcareApp.Api.Endpoints;

public static class PatientEndpoints
{
    public static void MapPatientEndpoints(this IEndpointRouteBuilder app)
    {
        // Require read:patients permission before listing patients.
        app.MapGet("/api/patients", GetPatientsAsync).RequireAuthorization("ReadPatients");

        // Require manage:patients permission before registering a patient.
        app.MapPost("/api/patients", CreatePatientAsync).RequireAuthorization("ManagePatients");

        // Require read:patients permission before viewing a patient.
        app.MapGet("/api/patients/{publicId:guid}", GetPatientByIdAsync)
            .RequireAuthorization("ReadPatients");

        // Require manage:patients permission before updating a patient.
        app.MapPut("/api/patients/{publicId:guid}", UpdatePatientAsync)
            .RequireAuthorization("ManagePatients");

        // Require manage:patients permission before deleting a patient.
        app.MapDelete("/api/patients/{publicId:guid}", DeletePatientAsync)
            .RequireAuthorization("ManagePatients");
    }

    private static async Task<IResult> GetPatientsAsync(
        HealthcareDbContext db,
        CancellationToken cancellationToken
    )
    {
        // Sort patients by name and return the fields needed for lookup.
        var patients = await db
            .Patients.OrderBy(patient => patient.LastName)
            .ThenBy(patient => patient.FirstName)
            .Select(patient => new
            {
                patient.PublicId,
                patient.FirstName,
                patient.LastName,
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(patients);
    }

    private static async Task<IResult> CreatePatientAsync(
        CreatePatientRequest request,
        HealthcareDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken
    )
    {
        // Trim both names and check the database's 100-character limit.
        var firstName = request.FirstName?.Trim();
        var lastName = request.LastName?.Trim();

        if (
            string.IsNullOrEmpty(firstName)
            || firstName.Length > 100
            || string.IsNullOrEmpty(lastName)
            || lastName.Length > 100
        )
        {
            return Results.BadRequest(
                new { message = "First and last names must each be between 1 and 100 characters." }
            );
        }

        // Require a date of birth on or before today.
        var dateOfBirth = request.DateOfBirth;
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        if (dateOfBirth is null || dateOfBirth.Value > today)
        {
            return Results.BadRequest(
                new { message = "Date of birth is required and cannot be in the future." }
            );
        }

        // Create the patient and save the new row to SQL Server.
        var patient = new Patient
        {
            FirstName = firstName,
            LastName = lastName,
            DateOfBirth = dateOfBirth.Value,
        };

        db.Patients.Add(patient);
        await db.SaveChangesAsync(cancellationToken);

        // Return the registered patient with HTTP 201 Created.
        // Return the new patient and their URL in the Location header.
        return Results.Created(
            $"/api/patients/{patient.PublicId}",
            new
            {
                patient.PublicId,
                patient.FirstName,
                patient.LastName,
                patient.DateOfBirth,
            }
        );
    }

    private static async Task<IResult> GetPatientByIdAsync(
        Guid publicId,
        HealthcareDbContext db,
        CancellationToken cancellationToken
    )
    {
        // Find the patient and select the fields returned by the detail endpoint.
        var patient = await db
            .Patients.Where(p => p.PublicId == publicId)
            .Select(p => new
            {
                p.PublicId,
                p.FirstName,
                p.LastName,
                p.DateOfBirth,
            })
            .SingleOrDefaultAsync(cancellationToken);

        // Return 404 when no patient matches the public ID.
        if (patient is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(patient);
    }

    private static async Task<IResult> UpdatePatientAsync(
        Guid publicId,
        UpdatePatientRequest request,
        HealthcareDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken
    )
    {
        // Trim the names and apply the same limits used during registration.
        var firstName = request.FirstName?.Trim();
        var lastName = request.LastName?.Trim();

        if (
            string.IsNullOrEmpty(firstName)
            || firstName.Length > 100
            || string.IsNullOrEmpty(lastName)
            || lastName.Length > 100
        )
        {
            return Results.BadRequest(
                new { message = "First and last names must each be between 1 and 100 characters." }
            );
        }

        // Require a date of birth on or before today.
        var dateOfBirth = request.DateOfBirth;
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        if (dateOfBirth is null || dateOfBirth.Value > today)
        {
            return Results.BadRequest(
                new { message = "Date of birth is required and cannot be in the future." }
            );
        }

        // Load the existing patient so EF Core can track the changes.
        var patient = await db.Patients.SingleOrDefaultAsync(
            p => p.PublicId == publicId,
            cancellationToken
        );

        if (patient is null)
        {
            return Results.NotFound();
        }

        // Update the patient's details and save them to SQL Server.
        patient.FirstName = firstName;
        patient.LastName = lastName;
        patient.DateOfBirth = dateOfBirth.Value;

        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(
            new
            {
                patient.PublicId,
                patient.FirstName,
                patient.LastName,
                patient.DateOfBirth,
            }
        );
    }

    private static async Task<IResult> DeletePatientAsync(
        Guid publicId,
        HealthcareDbContext db,
        CancellationToken cancellationToken
    )
    {
        try
        {
            // Delete only the patient who matches the public ID in the URL.
            var deletedCount = await db
                .Patients.Where(patient => patient.PublicId == publicId)
                .ExecuteDeleteAsync(cancellationToken);

            // Return 404 if no patient matched the public ID.
            if (deletedCount == 0)
            {
                return Results.NotFound();
            }

            return Results.NoContent();
        }
        catch (SqlException exception) when (exception.Number == 547)
        {
            // Return 409 when a referral still references this patient.
            return Results.Conflict(new { message = "This patient still has referrals." });
        }
    }
}
