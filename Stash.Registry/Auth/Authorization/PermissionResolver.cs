using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Stash.Registry.Auth;
using Stash.Registry.Contracts;
using Stash.Registry.Database;

namespace Stash.Registry.Auth.Authorization;

/// <summary>
/// EF Core-backed implementation of <see cref="IPermissionResolver"/>.
/// </summary>
/// <remarks>
/// <para>
/// Resolves the effective package role as the highest of:
/// <list type="number">
///   <item><description>Direct user-principal role on the package.</description></item>
///   <item><description>Team-mediated: highest role held by any team the user belongs to, where that team has a role on the package. The team row MUST still exist (fail-closed).</description></item>
///   <item><description>Org-mediated: if the package scope is org-owned, an org-member inherits at least <c>reader</c>; an org-owner inherits <c>owner</c>. The org and scope rows MUST still exist (fail-closed). An explicit org-principal role row also participates.</description></item>
/// </list>
/// </para>
/// <para>
/// Dangling references (a <c>PackageRoleEntry</c> pointing at a deleted team/org/scope)
/// are silently treated as absent grants. The resolver never throws; it always returns
/// either a valid role string or <c>null</c>.
/// </para>
/// </remarks>
public sealed class PermissionResolver : IPermissionResolver
{
    private readonly RegistryDbContext _ctx;

    private static int RoleRank(string role) => PackageRoles.Rank(role);

    /// <summary>Returns the role with the higher privilege (lower rank), or <paramref name="b"/> when <paramref name="a"/> is null.</summary>
    private static string BestRole(string? a, string b) =>
        a == null ? b : (RoleRank(a) <= RoleRank(b) ? a : b);

    /// <summary>Extracts the bare scope name from <c>@scope/name</c>, or <c>null</c>.</summary>
    private static string? ExtractScope(string packageName)
    {
        if (!packageName.StartsWith('@')) return null;
        int slash = packageName.IndexOf('/');
        return slash < 2 ? null : packageName[1..slash];
    }

    /// <summary>
    /// Initialises the resolver with the EF Core context for the current request scope.
    /// </summary>
    public PermissionResolver(RegistryDbContext ctx)
    {
        _ctx = ctx;
    }

    /// <inheritdoc/>
    public async Task<string?> GetEffectiveRoleAsync(string username, string packageName)
    {
        string? bestRole = null;

        // 1. Direct user-principal role
        var directEntry = await _ctx.PackageRoles
            .AsNoTracking()
            .FirstOrDefaultAsync(r =>
                r.PackageName == packageName &&
                r.PrincipalType == PrincipalTypes.User &&
                r.PrincipalId == username);
        if (directEntry != null)
            bestRole = BestRole(bestRole, directEntry.Role);

        // 2. Team-mediated roles — fail closed: only count teams whose TeamRecord still exists.
        var teamIds = await _ctx.TeamMembers
            .AsNoTracking()
            .Where(tm => tm.Username == username)
            .Select(tm => tm.TeamId)
            .ToListAsync();

        if (teamIds.Count > 0)
        {
            // Filter to teams that still have a TeamRecord row (existence guard).
            var existingTeamIds = await _ctx.Teams
                .AsNoTracking()
                .Where(t => teamIds.Contains(t.Id))
                .Select(t => t.Id)
                .ToListAsync();

            if (existingTeamIds.Count > 0)
            {
                var teamRoles = await _ctx.PackageRoles
                    .AsNoTracking()
                    .Where(r =>
                        r.PackageName == packageName &&
                        r.PrincipalType == PrincipalTypes.Team &&
                        existingTeamIds.Contains(r.PrincipalId))
                    .ToListAsync();

                foreach (var tr in teamRoles)
                    bestRole = BestRole(bestRole, tr.Role);
            }
        }

        // 3. Org-mediated roles — fail closed: only count scopes/orgs that still exist.
        string? scopeName = ExtractScope(packageName);
        if (scopeName != null)
        {
            var scope = await _ctx.Scopes
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Name == scopeName);

            if (scope?.OwnerType == ScopeOwnerTypes.Org && scope.OwnerOrgId != null)
            {
                // Existence guard: the org row must still exist.
                bool orgExists = await _ctx.Organizations
                    .AsNoTracking()
                    .AnyAsync(o => o.Id == scope.OwnerOrgId);

                if (orgExists)
                {
                    var orgMember = await _ctx.OrgMembers
                        .AsNoTracking()
                        .FirstOrDefaultAsync(m =>
                            m.OrgId == scope.OwnerOrgId &&
                            m.Username == username);

                    if (orgMember != null)
                    {
                        // Org owners inherit package owner; org members inherit reader.
                        string inheritedRole = orgMember.OrgRole == OrgRoles.Owner ? PackageRoles.Owner : PackageRoles.Reader;
                        bestRole = BestRole(bestRole, inheritedRole);
                    }

                    // Explicit org-principal role row
                    var orgRoleEntry = await _ctx.PackageRoles
                        .AsNoTracking()
                        .FirstOrDefaultAsync(r =>
                            r.PackageName == packageName &&
                            r.PrincipalType == PrincipalTypes.Org &&
                            r.PrincipalId == scope.OwnerOrgId);
                    if (orgRoleEntry != null)
                        bestRole = BestRole(bestRole, orgRoleEntry.Role);
                }
            }
        }

        return bestRole;
    }

    /// <inheritdoc/>
    public async Task<bool> IsOrgMemberAsync(string username, string orgId)
    {
        // Fail closed: if the org row doesn't exist, the membership is void.
        bool orgExists = await _ctx.Organizations
            .AsNoTracking()
            .AnyAsync(o => o.Id == orgId);

        if (!orgExists) return false;

        return await _ctx.OrgMembers
            .AsNoTracking()
            .AnyAsync(m => m.OrgId == orgId && m.Username == username);
    }
}
