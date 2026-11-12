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
        // Require read:clinics permission before listing clinics.
        app.MapGet("/api/clinics", GetClinicsAsync).RequireAuthorization("ReadClinics");

        // Require manage:clinics permission before creating a clinic.
        app.MapPost("/api/clinics", CreateClinicAsync).RequireAuthorization("ManageClinics");

        // Require read:clinics permission before viewing a clinic.
        app.MapGet("/api/clinics/{publicId:guid}", GetClinicByIdAsync)
            .RequireAuthorization("ReadClinics");

        // Require manage:clinics permission before updating a clinic.
        app.MapPut("/api/clinics/{publicId:guid}", UpdateClinicAsync)
            .RequireAuthorization("ManageClinics");

        // Require manage:clinics permission before configuring scheduling.
        app.MapPut("/api/clinics/{publicId:guid}/scheduling-settings", UpdateClinicSchedulingSettingsAsync)
            .RequireAuthorization("ManageClinics");

        // Require manage:clinics permission before deleting a clinic.
        app.MapDelete("/api/clinics/{publicId:guid}", DeleteClinicAsync)
            .RequireAuthorization("ManageClinics");
    }

    private static async Task<IResult> GetClinicsAsync(
        HealthcareDbContext db,
        CancellationToken cancellationToken
    )
    {
        // Read the clinics in name order and select the fields returned by the API.
        var clinics = await db
            .Clinics.OrderBy(clinic => clinic.Name)
            .Select(clinic => new { clinic.PublicId, clinic.Name, clinic.Address, clinic.TimeZoneId })
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

        var address = request.Address?.Trim();
        var timeZoneId = request.TimeZoneId?.Trim();
        if (ValidateSchedulingSettings(address, timeZoneId) is { } error)
        {
            return error;
        }

        var clinic = new Clinic { Name = name, Address = address!, TimeZoneId = timeZoneId! };

        // Add the new clinic and save it to SQL Server.
        db.Clinics.Add(clinic);
        await db.SaveChangesAsync(cancellationToken);

        // Return the new clinic and its URL in the Location header.
        return Results.Created(
            $"/api/clinics/{clinic.PublicId}",
            new { clinic.PublicId, clinic.Name, clinic.Address, clinic.TimeZoneId }
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
            .Select(clinic => new { clinic.PublicId, clinic.Name, clinic.Address, clinic.TimeZoneId })
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

        // Older callers can rename a clinic without changing its scheduling settings.
        if (request.Address is not null || request.TimeZoneId is not null)
        {
            var address = request.Address?.Trim();
            var timeZoneId = request.TimeZoneId?.Trim();
            if (ValidateSchedulingSettings(address, timeZoneId) is { } error)
            {
                return error;
            }

            clinic.Address = address!;
            clinic.TimeZoneId = timeZoneId!;
        }

        // Save all requested changes together.
        clinic.Name = name;
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(new { clinic.PublicId, clinic.Name, clinic.Address, clinic.TimeZoneId });
    }

    private static async Task<IResult> UpdateClinicSchedulingSettingsAsync(
        Guid publicId,
        UpdateClinicSchedulingSettingsRequest request,
        HealthcareDbContext db,
        CancellationToken cancellationToken
    )
    {
        var address = request.Address?.Trim();
        var timeZoneId = request.TimeZoneId?.Trim();

        if (ValidateSchedulingSettings(address, timeZoneId) is { } error)
        {
            return error;
        }

        // Update both settings together for the clinic in the route.
        var updatedCount = await db.Clinics.Where(clinic => clinic.PublicId == publicId)
            .ExecuteUpdateAsync(
                updates => updates
                    .SetProperty(clinic => clinic.Address, address!)
                    .SetProperty(clinic => clinic.TimeZoneId, timeZoneId!),
                cancellationToken
            );

        return updatedCount == 0 ? Results.NotFound() : Results.NoContent();
    }

    private static IResult? ValidateSchedulingSettings(string? address, string? timeZoneId)
    {
        if (string.IsNullOrEmpty(address) || address.Length > 300)
        {
            return Results.BadRequest(
                new { message = "Clinic address must be between 1 and 300 characters." }
            );
        }

        // Use an IANA ID so the same clinic settings work across environments.
        if (string.IsNullOrEmpty(timeZoneId)
            || timeZoneId.Length > 100
            || !TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZoneId, out _)
            || !TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out _))
        {
            return Results.BadRequest(
                new { message = "Enter a supported IANA time zone, such as America/Chicago." }
            );
        }

        return null;
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
