using System.Security.Claims;
using HealthcareApp.Api.Authorization;
using HealthcareApp.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HealthcareApp.Api.Data;

// Cache the trusted account lookup within one request, including authorization checks.
public sealed class WorkspaceAccess(HealthcareDbContext db)
{
    private AccountIdentity? resolvedIdentity;
    private bool resolved;
    public bool IsPrivateOwner { get; private set; }

    public async Task<bool> ResolveAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var identity = AccountIdentity.FromPrincipal(user);
        if (identity is null) return false;
        if (resolved)
        {
            if (identity != resolvedIdentity)
                throw new InvalidOperationException("A request cannot switch identities.");
            return true;
        }

        // Only this exact authenticated identity may bypass the account query filter.
        var account = await identity.QueryAccounts(db).IgnoreQueryFilters()
            .Select(row => new { row.WorkspaceId, row.IsAdministrator }).SingleOrDefaultAsync(ct);
        if (account?.WorkspaceId == Workspace.VisitorTemplateId) return false;

        db.SelectWorkspace(account?.WorkspaceId ?? Guid.Empty);
        IsPrivateOwner = account is { IsAdministrator: true } && account.WorkspaceId != Guid.Empty;
        resolvedIdentity = identity;
        resolved = true;
        return true;
    }
}
