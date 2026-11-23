using System.Globalization;
using System.Threading.RateLimiting;
using HealthcareApp.Api.Authorization;

namespace HealthcareApp.Api.Security;

public static class ApiRateLimiting
{
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<
                HttpContext, (string Issuer, string Subject)>(context =>
            {
                var identity = context.User.Identity?.IsAuthenticated == true
                    ? AccountIdentity.FromPrincipal(context.User)
                    : null;

                // Authentication rejects invalid callers; health checks stay unthrottled.
                if (identity is null || !context.Request.Path.StartsWithSegments("/api"))
                    return RateLimitPartition.GetNoLimiter<(string, string)>(("", ""));

                // A new token, route, or browser tab does not reset the account's quota.
                return RateLimitPartition.GetFixedWindowLimiter(
                    (identity.Issuer, identity.Subject),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 120,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    });
            });

            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        Math.Max(1, Math.Ceiling(retryAfter.TotalSeconds))
                            .ToString(CultureInfo.InvariantCulture);
                }

                await Results.Problem(
                    statusCode: StatusCodes.Status429TooManyRequests,
                    title: "Too many requests.",
                    detail: "Please wait before trying again."
                ).ExecuteAsync(context.HttpContext);
            };
        });

        return services;
    }
}
