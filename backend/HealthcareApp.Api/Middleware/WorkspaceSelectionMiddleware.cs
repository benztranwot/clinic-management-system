using HealthcareApp.Api.Data;
using Microsoft.AspNetCore.Authorization;

namespace HealthcareApp.Api.Middleware;

public sealed class WorkspaceSelectionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, WorkspaceAccess access)
    {
        var protectedRoute = http.GetEndpoint()?.Metadata.GetOrderedMetadata<IAuthorizeData>().Count > 0;
        if (protectedRoute && http.User.Identity?.IsAuthenticated == true
            && http.Request.Path.StartsWithSegments("/api")
            && !http.Request.Path.StartsWithSegments("/api/auth")
            && !await access.ResolveAsync(http.User, http.RequestAborted))
        {
            await Results.Challenge().ExecuteAsync(http);
            return;
        }
        await next(http);
    }
}
