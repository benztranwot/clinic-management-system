using System.Globalization;
using System.Security.Claims;
using HealthcareApp.Api.Authorization;
using HealthcareApp.Api.Data;
using HealthcareApp.Api.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace HealthcareApp.Api.Endpoints;

public static class ProviderAvailabilityEndpoints
{
    public static void MapProviderAvailabilityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/providers/{providerPublicId:guid}/availability", GetAvailabilityAsync)
            .RequireAuthorization();
    }

    private static async Task<IResult> GetAvailabilityAsync(
        Guid providerPublicId,
        string? date,
        ClaimsPrincipal user,
        HttpContext http,
        HealthcareDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken
    )
    {
        var identity = AccountIdentity.FromPrincipal(user);
        if (identity is null)
        {
            return Results.Challenge();
        }

        if (!DateOnly.TryParseExact(
            date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var localDate
        ))
        {
            return Results.BadRequest(new { message = "Enter a date in YYYY-MM-DD format." });
        }

        var provider = await (
            from item in db.Providers
            join clinic in identity.QueryStaffClinics(db) on item.ClinicId equals clinic.Id
            where item.PublicId == providerPublicId
            select new { item.Id, clinic.TimeZoneId }
        ).SingleOrDefaultAsync(cancellationToken);

        if (provider is null)
        {
            return Results.NotFound();
        }

        if (string.IsNullOrEmpty(provider.TimeZoneId)
            || !TimeZoneInfo.TryFindSystemTimeZoneById(provider.TimeZoneId, out var timeZone))
        {
            return Results.Conflict(new { message = "Set the clinic's time zone before finding slots." });
        }

        var now = clock.GetUtcNow();
        if (!AppointmentAvailability.IsInBookingWindow(localDate, timeZone, now))
        {
            return Results.BadRequest(new { message = "Choose a date from today through 30 days ahead." });
        }

        var slots = await AppointmentAvailability.FindAsync(
            db, provider.Id, localDate, timeZone, now, cancellationToken
        );

        http.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new { Date = localDate, provider.TimeZoneId, Slots = slots });
    }
}
