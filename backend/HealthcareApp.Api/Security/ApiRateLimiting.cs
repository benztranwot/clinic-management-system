using System.Globalization;
using System.Threading.RateLimiting;
using HealthcareApp.Api.Authorization;

namespace HealthcareApp.Api.Security;

public static class ApiRateLimiting
{
    // Applied to visitor workspace creation in addition to the global limits.
    public const string VisitorWorkspaceCreationPolicy = nameof(VisitorWorkspaceCreationPolicy);

    internal const int AccountPermitLimit = 120;
    internal const int AnonymousPermitLimit = 60;
    internal const int WorkspaceCreationPermitLimit = 10;

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<
                HttpContext, (string Issuer, string Subject)>(context =>
            {
                // Health checks opt out through DisableRateLimiting; other paths are not API traffic.
                if (!context.Request.Path.StartsWithSegments("/api"))
                    return RateLimitPartition.GetNoLimiter<(string, string)>(("", ""));

                var identity = context.User.Identity?.IsAuthenticated == true
                    ? AccountIdentity.FromPrincipal(context.User)
                    : null;

                // Requests without a valid token share one quota per client address.
                // An empty issuer never matches a validated account partition.
                if (identity is null)
                {
                    return RateLimitPartition.GetFixedWindowLimiter(
                        ("", "address:" + ClientAddress(context)),
                        _ => PerMinute(AnonymousPermitLimit));
                }

                // A new token, route, or browser tab does not reset the account's quota.
                return RateLimitPartition.GetFixedWindowLimiter(
                    (identity.Issuer, identity.Subject),
                    _ => PerMinute(AccountPermitLimit));
            });

            // Each account can own only one workspace, so limit creation by client address
            // to slow one visitor from using up the shared daily quota with several sign-ins.
            options.AddPolicy(VisitorWorkspaceCreationPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ClientAddress(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = WorkspaceCreationPermitLimit,
                        Window = TimeSpan.FromHours(1),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    }));

            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        Math.Max(1, Math.Ceiling(retryAfter.TotalSeconds))
                            .ToString(CultureInfo.InvariantCulture);
                }

                const string message = "Please wait before trying again.";
                await Results.Problem(
                    statusCode: StatusCodes.Status429TooManyRequests,
                    title: "Too many requests.",
                    detail: message,
                    // The frontend shows the message field from problem responses.
                    extensions: new Dictionary<string, object?> { ["message"] = message }
                ).ExecuteAsync(context.HttpContext);
            };
        });

        return services;
    }

    private static FixedWindowRateLimiterOptions PerMinute(int permitLimit) => new()
    {
        PermitLimit = permitLimit,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
        AutoReplenishment = true,
    };

    // UseForwardedHeaders has already replaced this with the visitor's address from nginx.
    private static string ClientAddress(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
