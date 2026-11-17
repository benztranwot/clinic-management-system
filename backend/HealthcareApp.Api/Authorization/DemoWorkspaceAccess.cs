using Microsoft.AspNetCore.Authorization;

namespace HealthcareApp.Api.Authorization;

internal sealed class DemoWorkspaceAccess
{
    public AccountIdentity? Administrator { get; set; }
}

internal sealed record WorkspacePermissionRequirement(string Permission) : IAuthorizationRequirement;

internal sealed class DemoWorkspacePermissionHandler(DemoWorkspaceAccess access)
    : AuthorizationHandler<WorkspacePermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        WorkspacePermissionRequirement requirement
    )
    {
        if (context.User.HasClaim("permissions", requirement.Permission)
            || (access.Administrator is { } administrator
                && administrator == AccountIdentity.FromPrincipal(context.User)))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
