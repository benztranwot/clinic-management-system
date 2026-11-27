using HealthcareApp.Api.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HealthcareApp.Api.Health;

// Report ready only when the API can open a connection to its database.
public sealed class DatabaseHealthCheck(HealthcareDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The response stays generic; the exception goes only to the server log.
            return HealthCheckResult.Unhealthy(exception: exception);
        }
    }
}
