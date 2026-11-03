using HealthcareApp.Api.Data;
using HealthcareApp.Api.Dtos;
using HealthcareApp.Api.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HealthcareApp.Api.Endpoints;

public static class ProviderServiceEndpoints
{
    public static void MapProviderServiceEndpoints(this IEndpointRouteBuilder app)
    {
        // Define the GET route for listing services offered by a provider.
        app.MapGet("/api/providers/{providerPublicId:guid}/services", GetServicesByProviderAsync);

        // Define the POST route for linking a service to a provider.
        app.MapPost("/api/providers/{providerPublicId:guid}/services", AddProviderServiceAsync);

        // Define the DELETE route for removing a service link from a provider.
        app.MapDelete(
            "/api/providers/{providerPublicId:guid}/services/{clinicalServicePublicId:guid}",
            RemoveProviderServiceAsync
        );
    }

    private static async Task<IResult> GetServicesByProviderAsync(
        Guid providerPublicId,
        HealthcareDbContext db,
        CancellationToken cancellationToken
    )
    {
        // Find the provider's internal ID using the public ID from the URL.
        var provider = await db
            .Providers.Where(p => p.PublicId == providerPublicId)
            .Select(p => new { p.Id })
            .SingleOrDefaultAsync(cancellationToken);

        if (provider is null)
        {
            return Results.NotFound(new { message = "Provider was not found." });
        }

        // Join the provider's eligibility records to their service details.
        var services = await (
            from eligibility in db.ProviderServices
            join service in db.ClinicalServices on eligibility.ClinicalServiceId equals service.Id
            where eligibility.ProviderId == provider.Id
            orderby service.Name
            select new { service.PublicId, service.Name }
        ).ToListAsync(cancellationToken);

        return Results.Ok(services);
    }

    private static async Task<IResult> AddProviderServiceAsync(
        Guid providerPublicId,
        AddProviderServiceRequest request,
        HealthcareDbContext db,
        CancellationToken cancellationToken
    )
    {
        if (request.ClinicalServicePublicId == Guid.Empty)
        {
            return Results.BadRequest(new { message = "Select a clinical service." });
        }

        // Find the provider and the clinic they belong to.
        var provider = await db
            .Providers.Where(p => p.PublicId == providerPublicId)
            .Select(p => new { p.Id, p.ClinicId })
            .SingleOrDefaultAsync(cancellationToken);

        if (provider is null)
        {
            return Results.NotFound(new { message = "Provider was not found." });
        }

        // Find the service and the clinic that offers it.
        var service = await db
            .ClinicalServices.Where(s => s.PublicId == request.ClinicalServicePublicId)
            .Select(s => new
            {
                s.Id,
                s.ClinicId,
                s.PublicId,
                s.Name,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (service is null)
        {
            return Results.NotFound(new { message = "Service was not found." });
        }

        // Require the provider and service to belong to the same clinic.
        if (provider.ClinicId != service.ClinicId)
        {
            return Results.BadRequest(
                new { message = "The provider and service must belong to the same clinic." }
            );
        }

        // Save the link using the internal IDs from both tables.
        db.ProviderServices.Add(
            new ProviderService
            {
                ProviderId = provider.Id,
                ClinicalServiceId = service.Id,
                ClinicId = provider.ClinicId,
            }
        );

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is SqlException { Number: 2627 })
        {
            // The composite primary key blocks a duplicate provider and service pair.
            return Results.Conflict(
                new { message = "This provider is already linked to that service." }
            );
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is SqlException { Number: 547 })
        {
            // Handle a provider or service that became unavailable before saving.
            return Results.Conflict(
                new { message = "The provider or service is no longer available for linking." }
            );
        }

        return Results.Json(
            new { service.PublicId, service.Name },
            statusCode: StatusCodes.Status201Created
        );
    }

    private static async Task<IResult> RemoveProviderServiceAsync(
        Guid providerPublicId,
        Guid clinicalServicePublicId,
        HealthcareDbContext db,
        CancellationToken cancellationToken
    )
    {
        try
        {
            // Match the link to both public IDs before deleting it.
            var deletedCount = await db
                .ProviderServices.Where(link =>
                    db.Providers.Any(provider =>
                        provider.Id == link.ProviderId && provider.PublicId == providerPublicId
                    )
                    && db.ClinicalServices.Any(service =>
                        service.Id == link.ClinicalServiceId
                        && service.PublicId == clinicalServicePublicId
                    )
                )
                .ExecuteDeleteAsync(cancellationToken);

            // Return 404 when the provider and service have no matching link.
            if (deletedCount == 0)
            {
                return Results.NotFound(new { message = "Provider service link was not found." });
            }

            return Results.NoContent();
        }
        catch (SqlException exception) when (exception.Number == 547)
        {
            // Return 409 when a referral still references this link.
            return Results.Conflict(
                new { message = "This provider service link is still used by a referral." }
            );
        }
    }
}
