using HealthcareApp.Api.Data;
using HealthcareApp.Api.Dtos;
using HealthcareApp.Api.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HealthcareApp.Api.Endpoints;

public static class ProviderEndpoints
{
    public static void MapProviderEndpoints(this IEndpointRouteBuilder app)
    {
        // Define the GET route for listing providers at a clinic.
        app.MapGet("/api/clinics/{clinicPublicId:guid}/providers", GetProvidersByClinicAsync);

        // Define the POST route for adding a provider to a clinic.
        app.MapPost("/api/clinics/{clinicPublicId:guid}/providers", CreateProviderAsync);

        // Define the GET route for finding a provider by their public ID.
        app.MapGet("/api/providers/{publicId:guid}", GetProviderByIdAsync);

        // Define the PUT route for updating a provider by their public ID.
        app.MapPut("/api/providers/{publicId:guid}", UpdateProviderAsync);

        // Define the DELETE route for removing a provider by their public ID.
        app.MapDelete("/api/providers/{publicId:guid}", DeleteProviderAsync);
    }

    private static async Task<IResult> GetProvidersByClinicAsync(
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

        // List providers whose ClinicId matches the clinic's internal ID.
        var providers = await db
            .Providers.Where(p => p.ClinicId == clinic.Id)
            .OrderBy(p => p.LastName)
            .ThenBy(p => p.FirstName)
            .Select(p => new
            {
                p.PublicId,
                p.FirstName,
                p.LastName,
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(providers);
    }

    private static async Task<IResult> CreateProviderAsync(
        Guid clinicPublicId,
        CreateProviderRequest request,
        HealthcareDbContext db,
        CancellationToken cancellationToken
    )
    {
        // Trim the names and check the database's 100-character limit.
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

        // Find the internal clinic ID to store in the provider's foreign key.
        var clinic = await db
            .Clinics.Where(c => c.PublicId == clinicPublicId)
            .Select(c => new { c.Id })
            .SingleOrDefaultAsync(cancellationToken);

        if (clinic is null)
        {
            return Results.NotFound(new { message = "Clinic was not found." });
        }

        // Create the provider under the selected clinic.
        var provider = new Provider
        {
            ClinicId = clinic.Id,
            FirstName = firstName,
            LastName = lastName,
        };

        db.Providers.Add(provider);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is SqlException { Number: 547 })
        {
            // Handle a clinic that was deleted between lookup and save.
            return Results.NotFound(new { message = "Clinic was not found." });
        }

        // Return 201 Created with the new provider's URL in the Location header.
        return Results.Created(
            $"/api/providers/{provider.PublicId}",
            new
            {
                provider.PublicId,
                provider.FirstName,
                provider.LastName,
            }
        );
    }

    private static async Task<IResult> GetProviderByIdAsync(
        Guid publicId,
        HealthcareDbContext db,
        CancellationToken cancellationToken
    )
    {
        // Find the provider and select the fields returned by the API.
        var provider = await db
            .Providers.Where(p => p.PublicId == publicId)
            .Select(p => new
            {
                p.PublicId,
                p.FirstName,
                p.LastName,
            })
            .SingleOrDefaultAsync(cancellationToken);

        // Return 404 when no provider matches the public ID.
        if (provider is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(provider);
    }

    private static async Task<IResult> UpdateProviderAsync(
        Guid publicId,
        UpdateProviderRequest request,
        HealthcareDbContext db,
        CancellationToken cancellationToken
    )
    {
        // Trim the names and check the database's 100-character limit.
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

        // Load the provider so EF Core can track the changes.
        var provider = await db.Providers.SingleOrDefaultAsync(
            p => p.PublicId == publicId,
            cancellationToken
        );

        if (provider is null)
        {
            return Results.NotFound();
        }

        // Update the provider's names and save them to SQL Server.
        provider.FirstName = firstName;
        provider.LastName = lastName;
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(
            new
            {
                provider.PublicId,
                provider.FirstName,
                provider.LastName,
            }
        );
    }

    private static async Task<IResult> DeleteProviderAsync(
        Guid publicId,
        HealthcareDbContext db,
        CancellationToken cancellationToken
    )
    {
        try
        {
            // Delete only the provider who matches the public ID in the URL.
            var deletedCount = await db
                .Providers.Where(provider => provider.PublicId == publicId)
                .ExecuteDeleteAsync(cancellationToken);

            // Return 404 if no provider matched the public ID.
            if (deletedCount == 0)
            {
                return Results.NotFound();
            }

            return Results.NoContent();
        }
        catch (SqlException exception) when (exception.Number == 547)
        {
            // Return 409 when a service eligibility record still references this provider.
            return Results.Conflict(
                new { message = "This provider is still linked to one or more services." }
            );
        }
    }
}
