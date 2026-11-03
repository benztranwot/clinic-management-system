using HealthcareApp.Api.Data;
using HealthcareApp.Api.Dtos;
using HealthcareApp.Api.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HealthcareApp.Api.Endpoints;

public static class ClinicEndpoints
{
    public static void MapClinicEndpoints(this IEndpointRouteBuilder app)
    {
        // Define the GET route for listing clinics.
        app.MapGet("/api/clinics", GetClinicsAsync);

        // Define the POST route for creating a clinic.
        app.MapPost("/api/clinics", CreateClinicAsync);

        // Define the GET route for finding a clinic by its public ID.
        app.MapGet("/api/clinics/{publicId:guid}", GetClinicByIdAsync);

        // Define the PUT route for updating a clinic by its public ID.
        app.MapPut("/api/clinics/{publicId:guid}", UpdateClinicAsync);

        // Define the DELETE route for removing a clinic by its public ID.
        app.MapDelete("/api/clinics/{publicId:guid}", DeleteClinicAsync);
    }

    private static async Task<IResult> GetClinicsAsync(
        HealthcareDbContext db,
        CancellationToken cancellationToken
    )
    {
        // Read the clinics in name order and select the fields returned by the API.
        var clinics = await db
            .Clinics.OrderBy(clinic => clinic.Name)
            .Select(clinic => new { clinic.PublicId, clinic.Name })
            .ToListAsync(cancellationToken);

        return Results.Ok(clinics);
    }

    private static async Task<IResult> CreateClinicAsync(
        CreateClinicRequest request,
        HealthcareDbContext db,
        CancellationToken cancellationToken
    )
    {
        // Trim and validate the name before creating the clinic.
        var name = request.Name?.Trim();

        if (string.IsNullOrEmpty(name) || name.Length > 200)
        {
            return Results.BadRequest(
                new { message = "Clinic name must be between 1 and 200 characters." }
            );
        }

        var clinic = new Clinic { Name = name };

        // Add the new clinic and save it to SQL Server.
        db.Clinics.Add(clinic);
        await db.SaveChangesAsync(cancellationToken);

        // Return the new clinic and its URL in the Location header.
        return Results.Created(
            $"/api/clinics/{clinic.PublicId}",
            new { clinic.PublicId, clinic.Name }
        );
    }

    private static async Task<IResult> GetClinicByIdAsync(
        Guid publicId,
        HealthcareDbContext db,
        CancellationToken cancellationToken
    )
    {
        // Look up the clinic and select the fields returned by the API.
        var clinic = await db
            .Clinics.Where(clinic => clinic.PublicId == publicId)
            .Select(clinic => new { clinic.PublicId, clinic.Name })
            .SingleOrDefaultAsync(cancellationToken);

        // Return 404 when the requested clinic does not exist.
        if (clinic is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(clinic);
    }

    private static async Task<IResult> UpdateClinicAsync(
        Guid publicId,
        UpdateClinicRequest request,
        HealthcareDbContext db,
        CancellationToken cancellationToken
    )
    {
        // Trim the new name and validate it against the database limit.
        var name = request.Name?.Trim();

        if (string.IsNullOrEmpty(name) || name.Length > 200)
        {
            return Results.BadRequest(
                new { message = "Clinic name must be between 1 and 200 characters." }
            );
        }

        // Load the existing clinic so EF Core can track changes to it.
        var clinic = await db.Clinics.SingleOrDefaultAsync(
            c => c.PublicId == publicId,
            cancellationToken
        );

        if (clinic is null)
        {
            return Results.NotFound();
        }

        // Change the clinic's name and save it to SQL Server.
        clinic.Name = name;
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(new { clinic.PublicId, clinic.Name });
    }

    private static async Task<IResult> DeleteClinicAsync(
        Guid publicId,
        HealthcareDbContext db,
        CancellationToken cancellationToken
    )
    {
        try
        {
            // Delete only the clinic that matches the public ID in the URL.
            var deletedCount = await db
                .Clinics.Where(clinic => clinic.PublicId == publicId)
                .ExecuteDeleteAsync(cancellationToken);

            // Return 404 if the database found no matching clinic.
            if (deletedCount == 0)
            {
                return Results.NotFound();
            }

            return Results.NoContent();
        }
        catch (SqlException exception) when (exception.Number == 547)
        {
            // SQL Server blocked the delete because another record references the clinic.
            return Results.Conflict(
                new { message = "This clinic still has providers or services." }
            );
        }
    }
}
