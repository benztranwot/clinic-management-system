using HealthcareApp.Api.Data;
using HealthcareApp.Api.Workspaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace HealthcareApp.Api.Authorization;

public sealed record WorkspacePermissionRequirement(string Permission) : IAuthorizationRequirement;

public sealed class WorkspacePermissionHandler(WorkspaceAccess access,
    IHttpContextAccessor httpContext, IOptions<VisitorWorkspaceOptions> options)
    : AuthorizationHandler<WorkspacePermissionRequirement>
{
    // These capabilities apply only after the account selects its isolated workspace.
    public static readonly IReadOnlyList<string> OwnerPermissions = Array.AsReadOnly(new[]
    {
        "read:clinics", "manage:clinics", "read:patients", "manage:patients", "read:providers", "manage:providers",
        "read:services", "manage:services", "read:provider-services", "manage:provider-services",
        "read:referrals", "manage:referrals", "read:appointments",
    });

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context,
        WorkspacePermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true) return;
        if (context.User.HasClaim("permissions", requirement.Permission))
        {
            context.Succeed(requirement);
            return;
        }

        // Turning off visitor access does not alter the always-on data filters.
        if (!options.Value.Enabled || !OwnerPermissions.Contains(requirement.Permission)) return;
        var ct = httpContext.HttpContext?.RequestAborted ?? CancellationToken.None;
        if (await access.ResolveAsync(context.User, ct) && access.IsPrivateOwner)
            context.Succeed(requirement);
    }
}
