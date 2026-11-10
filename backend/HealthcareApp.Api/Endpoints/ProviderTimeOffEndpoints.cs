using System.Globalization;
using System.Security.Claims;
using HealthcareApp.Api.Authorization;
using HealthcareApp.Api.Data;
using HealthcareApp.Api.Dtos;
using HealthcareApp.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HealthcareApp.Api.Endpoints;

public static class ProviderTimeOffEndpoints
{
    private static readonly string[] UtcFormats =
        ["yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"];

    public static void MapProviderTimeOffEndpoints(this IEndpointRouteBuilder app)
    {
        // The provider comes from the caller's linked account.
        var routes = app.MapGroup("/api/me/time-off").RequireAuthorization();
        routes.MapGet("/{publicId:guid?}", GetTimeOffAsync);
        routes.MapPost("", CreateTimeOffAsync);
        routes.MapDelete("/{publicId:guid}", DeleteTimeOffAsync);
    }

    private static async Task<IResult> GetTimeOffAsync(
        Guid? publicId,
        ClaimsPrincipal user,
        HttpContext http,
        HealthcareDbContext db,
        CancellationToken cancellationToken
    )
    {
        var identity = AccountIdentity.FromPrincipal(user);
        if (identity is null)
        {
            return Results.Challenge();
        }

        var calendar = await identity.QueryOwnProviders(db)
            .Select(provider => new
            {
                Blocks = db.ProviderTimeOff
                    .Where(block => block.ProviderId == provider.Id
                        && (publicId == null || block.PublicId == publicId))
                    .OrderBy(block => block.StartUtc)
                    .ThenBy(block => block.Id)
                    .Select(block => new { block.PublicId, block.StartUtc, block.EndUtc })
                    .ToList(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (calendar is null)
        {
            return Results.Forbid();
        }

        http.Response.Headers.CacheControl = "no-store";

        if (publicId is null)
        {
            return Results.Ok(calendar.Blocks);
        }

        return calendar.Blocks.Count == 0
            ? Results.NotFound()
            : Results.Ok(calendar.Blocks[0]);
    }

    private static async Task<IResult> CreateTimeOffAsync(
        CreateProviderTimeOffRequest request,
        ClaimsPrincipal user,
        HealthcareDbContext db,
        CancellationToken cancellationToken
    )
    {
        var identity = AccountIdentity.FromPrincipal(user);
        if (identity is null)
        {
            return Results.Challenge();
        }

        var providers = identity.QueryOwnProviders(db);
        var providerId = await providers.Select(provider => (int?)provider.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (providerId is null)
        {
            return Results.Forbid();
        }

        if (!TryParseUtc(request.StartUtc, out var start)
            || !TryParseUtc(request.EndUtc, out var end)
            || start >= end)
        {
            return Results.BadRequest(new
            {
                message = "Enter UTC start and end timestamps, with the end after the start.",
            });
        }

        // Share the lock used by weekly hours and future booking operations.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await ProviderCalendarLock.TryAcquireAsync(db, providerId.Value, cancellationToken))
        {
            return Results.Conflict(new { message = "The provider calendar is busy. Try again." });
        }

        if (!await providers.AnyAsync(provider => provider.Id == providerId.Value, cancellationToken))
        {
            return Results.Forbid();
        }

        // Adjacent blocks are allowed. Only intervals that share time conflict.
        var overlaps = await db.ProviderTimeOff.AnyAsync(
            block => block.ProviderId == providerId.Value
                && block.StartUtc < end && start < block.EndUtc,
            cancellationToken
        );

        if (overlaps)
        {
            return Results.Conflict(new { message = "This time overlaps an existing time-off block." });
        }

        var timeOff = new ProviderTimeOff
        {
            ProviderId = providerId.Value,
            StartUtc = start,
            EndUtc = end,
        };
        db.ProviderTimeOff.Add(timeOff);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Results.Created(
            $"/api/me/time-off/{timeOff.PublicId}",
            new { timeOff.PublicId, timeOff.StartUtc, timeOff.EndUtc }
        );
    }

    private static async Task<IResult> DeleteTimeOffAsync(
        Guid publicId,
        ClaimsPrincipal user,
        HealthcareDbContext db,
        CancellationToken cancellationToken
    )
    {
        var identity = AccountIdentity.FromPrincipal(user);
        if (identity is null)
        {
            return Results.Challenge();
        }

        var providers = identity.QueryOwnProviders(db);
        var providerId = await providers.Select(provider => (int?)provider.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (providerId is null)
        {
            return Results.Forbid();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await ProviderCalendarLock.TryAcquireAsync(db, providerId.Value, cancellationToken))
        {
            return Results.Conflict(new { message = "The provider calendar is busy. Try again." });
        }

        if (!await providers.AnyAsync(provider => provider.Id == providerId.Value, cancellationToken))
        {
            return Results.Forbid();
        }

        var deleted = await db.ProviderTimeOff.Where(block =>
                block.ProviderId == providerId.Value && block.PublicId == publicId)
            .ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return deleted == 0 ? Results.NotFound() : Results.NoContent();
    }

    private static bool TryParseUtc(string? value, out DateTimeOffset timestamp)
    {
        timestamp = default;
        return value is not null
            && (value.EndsWith('Z') || value.EndsWith("+00:00", StringComparison.Ordinal))
            && DateTimeOffset.TryParseExact(
                value, UtcFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal, out timestamp
            );
    }
}
