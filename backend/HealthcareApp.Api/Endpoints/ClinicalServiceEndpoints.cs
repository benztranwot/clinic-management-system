using HealthcareApp.Api.Data;
using HealthcareApp.Api.Dtos;
using HealthcareApp.Api.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HealthcareApp.Api.Endpoints;

public static class ClinicalServiceEndpoints
{
    public static void MapClinicalServiceEndpoints(this IEndpointRouteBuilder app)
    {
        // Require read:services permission before listing a clinic's services.
        app.MapGet("/api/clinics/{clinicPublicId:guid}/services", GetServicesByClinicAsync)
            .RequireAuthorization("ReadClinicalServices");

        // Require manage:services permission before adding a service.
        app.MapPost("/api/clinics/{clinicPublicId:guid}/services", CreateServiceAsync)
            .RequireAuthorization("ManageClinicalServices");

        // Require read:services permission before viewing a service.
        app.MapGet("/api/services/{publicId:guid}", GetServiceByIdAsync)
            .RequireAuthorization("ReadClinicalServices");

        // Require manage:services permission before renaming a service.
        app.MapPut("/api/services/{publicId:guid}", UpdateServiceAsync)
            .RequireAuthorization("ManageClinicalServices");

        // Require manage:services permission before deleting a service.
        app.MapDelete("/api/services/{publicId:guid}", DeleteServiceAsync)
            .RequireAuthorization("ManageClinicalServices");
    }

    private static async Task<IResult> GetServicesByClinicAsync(
        Guid clinicPublicId,
        HealthcareDbContext db,
        CancellationToken cancellationToken
    )
    {
        // Find the clinic's internal ID using the public ID from the URL.
        var clinic = await db
            .Clinics.Where(c => c.PublicId == clinicPublicId)
            .Select(c => new { c.Id })
            .SingleOrDefaultAsync(cancellationToken);

        if (clinic is null)
        {
            return Results.NotFound(new { message = "Clinic was not found." });
        }

        // List services offered by this clinic in name order.
        var services = await db
            .ClinicalServices.Where(service => service.ClinicId == clinic.Id)
            .OrderBy(service => service.Name)
            .Select(service => new { service.PublicId, service.Name })
            .ToListAsync(cancellationToken);

        return Results.Ok(services);
    }

    private static async Task<IResult> CreateServiceAsync(
        Guid clinicPublicId,
        CreateClinicalServiceRequest request,
        HealthcareDbContext db,
        CancellationToken cancellationToken
    )
    {
        // Trim and validate the service name before saving it.
        var name = request.Name?.Trim();

        if (string.IsNullOrEmpty(name) || name.Length > 200)
        {
            return Results.BadRequest(
                new { message = "Service name must be between 1 and 200 characters." }
            );
        }

        // Find the clinic's internal ID to use as the service's foreign key.
        var clinic = await db
            .Clinics.Where(c => c.PublicId == clinicPublicId)
            .Select(c => new { c.Id })
            .SingleOrDefaultAsync(cancellationToken);

        if (clinic is null)
        {
            return Results.NotFound(new { message = "Clinic was not found." });
        }

        // Add the service under the selected clinic.
        var service = new ClinicalService { ClinicId = clinic.Id, Name = name };
        db.ClinicalServices.Add(service);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateServiceName(exception))
        {
            // Return 409 when the clinic already offers a service with this name.
            return Results.Conflict(
                new { message = "This clinic already has a service with that name." }
            );
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is SqlException { Number: 547 })
        {
            // Handle a clinic that was deleted between lookup and save.
            return Results.NotFound(new { message = "Clinic was not found." });
        }

        // Return 201 Created with the new service's URL in the Location header.
        return Results.Created(
            $"/api/services/{service.PublicId}",
            new { service.PublicId, service.Name }
        );
    }

    private static async Task<IResult> GetServiceByIdAsync(
        Guid publicId,
        HealthcareDbContext db,
        CancellationToken cancellationToken
    )
    {
        // Find the service and select the fields returned by the API.
        var service = await db
            .ClinicalServices.Where(s => s.PublicId == publicId)
            .Select(s => new { s.PublicId, s.Name })
            .SingleOrDefaultAsync(cancellationToken);

        // Return 404 when no service matches the public ID.
        if (service is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(service);
    }

    private static async Task<IResult> UpdateServiceAsync(
        Guid publicId,
        UpdateClinicalServiceRequest request,
        HealthcareDbContext db,
        CancellationToken cancellationToken
    )
    {
        // Trim the new name and check the database's 200-character limit.
        var name = request.Name?.Trim();

        if (string.IsNullOrEmpty(name) || name.Length > 200)
        {
            return Results.BadRequest(
                new { message = "Service name must be between 1 and 200 characters." }
            );
        }

        // Load the existing service so EF Core can track the name change.
        var service = await db.ClinicalServices.SingleOrDefaultAsync(
            s => s.PublicId == publicId,
            cancellationToken
        );

        if (service is null)
        {
            return Results.NotFound();
        }

        // Save the new name. SQL Server checks whether it is unique within the clinic.
        service.Name = name;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateServiceName(exception))
        {
            return Results.Conflict(
                new { message = "This clinic already has a service with that name." }
            );
        }

        return Results.Ok(new { service.PublicId, service.Name });
    }

    // Identify a duplicate name reported by the clinic-and-name unique index.
    private static bool IsDuplicateServiceName(DbUpdateException exception)
    {
        return exception.InnerException is SqlException { Number: 2601 or 2627 } sql
            && sql.Message.Contains("IX_ClinicalServices_ClinicId_Name", StringComparison.Ordinal);
    }

    private static async Task<IResult> DeleteServiceAsync(
        Guid publicId,
        HealthcareDbContext db,
        CancellationToken cancellationToken
    )
    {
        try
        {
            // Delete only the service that matches the public ID in the URL.
            var deletedCount = await db
                .ClinicalServices.Where(service => service.PublicId == publicId)
                .ExecuteDeleteAsync(cancellationToken);

            // Return 404 when no service matches the public ID.
            if (deletedCount == 0)
            {
                return Results.NotFound();
            }

            return Results.NoContent();
        }
        catch (SqlException exception) when (exception.Number == 547)
        {
            // Return 409 when another record still references this service.
            return Results.Conflict(
                new { message = "This service is still linked to providers or referrals." }
            );
        }
    }
}
