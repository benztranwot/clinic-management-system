using HealthcareApp.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HealthcareApp.Api.Data;

internal sealed class DemoWorkspaceMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, HealthcareDbContext db, DemoWorkspaceAccess access)
    {
        // Workspace selection is based only on the authenticated account, never a route or request value.
        if (context.User.Identity?.IsAuthenticated == true
            && context.GetEndpoint()?.Metadata.GetMetadata<IAuthorizeData>() is not null
            && AccountIdentity.FromPrincipal(context.User) is { } identity)
        {
            var account = await identity.QueryAccounts(db)
                .IgnoreQueryFilters([HealthcareDbContext.WorkspaceQueryFilter])
                .Select(account => new { account.WorkspaceId, account.IsAdministrator })
                .SingleOrDefaultAsync(context.RequestAborted);

            if (account is not null)
            {
                db.SelectWorkspace(account.WorkspaceId);
                if (account.WorkspaceId != Guid.Empty && account.IsAdministrator)
                {
                    access.Administrator = identity;
                }
            }
        }

        await next(context);
    }
}
