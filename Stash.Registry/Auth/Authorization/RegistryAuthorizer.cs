using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Stash.Registry.Auth;
using Stash.Registry.Database;

namespace Stash.Registry.Auth.Authorization;

/// <summary>
/// Default implementation of <see cref="IRegistryAuthorizer"/>.
/// Evaluates the two-step intersection: ceiling check first, then resource-side check.
/// </summary>
public sealed class RegistryAuthorizer : IRegistryAuthorizer
{
    private readonly IPermissionResolver _resolver;
    private readonly RegistryDbContext _ctx;

    private static int RoleRank(string role) => PackageRoles.Rank(role);

    /// <summary>Returns true when <paramref name="effective"/> satisfies <paramref name="required"/>.</summary>
    private static bool HasRole(string? effective, string required) =>
        effective != null && RoleRank(effective) <= RoleRank(required);

    /// <summary>Reserved scope names that no principal may publish into.</summary>
    private static readonly string[] ReservedScopes = ["stash", "admin"];

    /// <summary>
    /// Initialises the authorizer with its dependencies.
    /// </summary>
    public RegistryAuthorizer(IPermissionResolver resolver, RegistryDbContext ctx)
    {
        _resolver = resolver;
        _ctx = ctx;
    }

    /// <inheritdoc/>
    public async Task<AuthzDecision> AuthorizeAsync(
        Principal principal,
        RegistryAction action,
        ResourceRef resource)
    {
        // ── Step 1: ceiling check ─────────────────────────────────────────────
        var ceilingResult = CheckCeiling(principal, action);
        if (ceilingResult != null)
            return ceilingResult.Value;

        // ── Step 2: resource-side check ───────────────────────────────────────
        return await CheckResourceAsync(principal, action, resource);
    }

    // ── Ceiling check ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a denial if the ceiling is insufficient, <c>null</c> if the ceiling check passes.
    /// Anonymous principals fail with <see cref="AuthzDenyReason.NotAuthenticated"/> for actions
    /// that require authentication.
    /// </summary>
    private static AuthzDecision? CheckCeiling(Principal principal, RegistryAction action)
    {
        TokenCeiling required = RequiredCeiling(action);

        // Public/unauthenticated actions: no ceiling check needed.
        // We represent these with a sentinel value below.
        if (required == (TokenCeiling)(-1))
            return null; // pass through to resource check

        if (principal is AnonymousPrincipal)
        {
            // Anonymous callers cannot satisfy any authenticated ceiling.
            return AuthzDecision.Deny(AuthzDenyReason.NotAuthenticated);
        }

        var user = (UserPrincipal)principal;
        if (user.Ceiling < required)
            return AuthzDecision.Deny(AuthzDenyReason.TokenScopeInsufficient);

        return null; // ceiling check passed
    }

    /// <summary>
    /// Returns the minimum <see cref="TokenCeiling"/> required for <paramref name="action"/>.
    /// Returns <c>(TokenCeiling)(-1)</c> as a sentinel for actions that are fully public
    /// (anonymous allowed at the ceiling level; resource-side check may still deny).
    /// </summary>
    private static TokenCeiling RequiredCeiling(RegistryAction action) => action switch
    {
        // Public read actions — anonymous callers may attempt; resource-side visibility decides.
        RegistryAction.ReadPackageMetadata => (TokenCeiling)(-1),
        RegistryAction.ReadPackageVersion => (TokenCeiling)(-1),
        RegistryAction.DownloadPackageVersion => (TokenCeiling)(-1),
        RegistryAction.ResolveScope => (TokenCeiling)(-1),
        RegistryAction.ReadOrg => (TokenCeiling)(-1),
        RegistryAction.Search => (TokenCeiling)(-1),
        RegistryAction.Login => (TokenCeiling)(-1),
        RegistryAction.Register => (TokenCeiling)(-1),

        // Read-ceiling actions — must be authenticated, read ceiling or above.
        RegistryAction.Whoami => TokenCeiling.Read,
        RegistryAction.ListOwnTokens => TokenCeiling.Read,
        RegistryAction.RevokeOwnToken => TokenCeiling.Read,

        // Publish-ceiling actions
        RegistryAction.CreatePackage => TokenCeiling.Publish,
        RegistryAction.PublishVersion => TokenCeiling.Publish,
        RegistryAction.UnpublishVersion => TokenCeiling.Publish,
        RegistryAction.DeprecatePackage => TokenCeiling.Publish,
        RegistryAction.DeprecateVersion => TokenCeiling.Publish,
        RegistryAction.ChangePackageVisibility => TokenCeiling.Publish,
        RegistryAction.ListPackageRoles => TokenCeiling.Publish,
        RegistryAction.AssignPackageRole => TokenCeiling.Publish,
        RegistryAction.RevokePackageRole => TokenCeiling.Publish,
        RegistryAction.DeletePackage => TokenCeiling.Publish,
        RegistryAction.ClaimScope => TokenCeiling.Publish,
        RegistryAction.VerifyScope => TokenCeiling.Publish,
        RegistryAction.CreateOrg => TokenCeiling.Publish,
        RegistryAction.AddOrgMember => TokenCeiling.Publish,
        RegistryAction.RemoveOrgMember => TokenCeiling.Publish,
        RegistryAction.CreateTeam => TokenCeiling.Publish,
        RegistryAction.AddTeamMember => TokenCeiling.Publish,
        RegistryAction.IssueToken => TokenCeiling.Publish,

        // Admin-ceiling actions
        RegistryAction.ReadAdminStats => TokenCeiling.Admin,
        RegistryAction.ManageUser => TokenCeiling.Admin,
        RegistryAction.AdminAssignPackageRole => TokenCeiling.Admin,
        RegistryAction.AdminRevokePackageRole => TokenCeiling.Admin,
        RegistryAction.ReadAuditLog => TokenCeiling.Admin,

        // Safety net — default to admin ceiling so unknown actions fail closed.
        _ => TokenCeiling.Admin,
    };

    // ── Resource-side check ───────────────────────────────────────────────────

    private async Task<AuthzDecision> CheckResourceAsync(
        Principal principal, RegistryAction action, ResourceRef resource)
    {
        // Admin short-circuit at the resource level (ceiling already passed).
        bool isAdmin = principal is UserPrincipal { Role: UserRole.Admin };
        string? username = principal is UserPrincipal u ? u.Username : null;

        return action switch
        {
            // ── Package read actions ─────────────────────────────────────────
            RegistryAction.ReadPackageMetadata or
            RegistryAction.ReadPackageVersion or
            RegistryAction.DownloadPackageVersion =>
                await AuthorizePackageReadAsync(username, isAdmin, resource),

            // ── Package write actions — require package role ─────────────────
            RegistryAction.PublishVersion =>
                await AuthorizePackageRoleAsync(username, isAdmin, resource, requiredRole: PackageRoles.Publisher),

            RegistryAction.UnpublishVersion or
            RegistryAction.DeprecatePackage or
            RegistryAction.DeprecateVersion =>
                await AuthorizePackageRoleAsync(username, isAdmin, resource, requiredRole: PackageRoles.Maintainer),

            RegistryAction.ChangePackageVisibility or
            RegistryAction.ListPackageRoles or
            RegistryAction.AssignPackageRole or
            RegistryAction.RevokePackageRole or
            RegistryAction.DeletePackage =>
                await AuthorizePackageRoleAsync(username, isAdmin, resource, requiredRole: PackageRoles.Owner),

            // ── CreatePackage — gated on scope ownership ─────────────────────
            RegistryAction.CreatePackage =>
                await AuthorizeCreatePackageAsync(username, isAdmin, resource),

            // ── Admin-only package role overrides ────────────────────────────
            RegistryAction.AdminAssignPackageRole or
            RegistryAction.AdminRevokePackageRole =>
                isAdmin ? AuthzDecision.Allow()
                        : AuthzDecision.Deny(AuthzDenyReason.PackageRoleInsufficient,
                              "Admin role required."),

            // ── Scope actions ────────────────────────────────────────────────
            RegistryAction.ResolveScope =>
                AuthzDecision.Allow(), // public

            RegistryAction.ClaimScope =>
                await AuthorizeClaimScopeAsync(username, isAdmin, resource),

            RegistryAction.VerifyScope =>
                await AuthorizeVerifyScopeAsync(username, isAdmin, resource),

            // ── Org actions ──────────────────────────────────────────────────
            RegistryAction.ReadOrg =>
                AuthzDecision.Allow(), // public

            RegistryAction.CreateOrg =>
                username != null ? AuthzDecision.Allow()
                                 : AuthzDecision.Deny(AuthzDenyReason.NotAuthenticated),

            RegistryAction.AddOrgMember or
            RegistryAction.RemoveOrgMember or
            RegistryAction.CreateTeam or
            RegistryAction.AddTeamMember =>
                await AuthorizeOrgOwnerAsync(username, isAdmin, resource),

            // ── Auth / self-service ──────────────────────────────────────────
            RegistryAction.Login or
            RegistryAction.Register =>
                AuthzDecision.Allow(),

            RegistryAction.Whoami or
            RegistryAction.IssueToken or
            RegistryAction.ListOwnTokens =>
                username != null ? AuthzDecision.Allow()
                                 : AuthzDecision.Deny(AuthzDenyReason.NotAuthenticated),

            RegistryAction.RevokeOwnToken =>
                await AuthorizeRevokeOwnTokenAsync(username, resource),

            // ── Admin plane ──────────────────────────────────────────────────
            RegistryAction.ReadAdminStats or
            RegistryAction.ManageUser or
            RegistryAction.ReadAuditLog =>
                isAdmin ? AuthzDecision.Allow()
                        : AuthzDecision.Deny(AuthzDenyReason.PackageRoleInsufficient,
                              "Admin role required."),

            // ── Search ───────────────────────────────────────────────────────
            RegistryAction.Search =>
                AuthzDecision.Allow(),

            // Safety net
            _ => AuthzDecision.Deny(AuthzDenyReason.PolicyDenied, $"Unrecognised action: {action}")
        };
    }

    // ── Resource helpers ──────────────────────────────────────────────────────

    private async Task<AuthzDecision> AuthorizePackageReadAsync(
        string? username, bool isAdmin, ResourceRef resource)
    {
        if (resource is not PackageResource pkg)
            return AuthzDecision.Deny(AuthzDenyReason.PolicyDenied, "Expected PackageResource.");

        var packageRecord = await _ctx.Packages
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Name == pkg.FullName);

        if (packageRecord == null)
            return AuthzDecision.Deny(AuthzDenyReason.PackageNotFound);

        string visibility = packageRecord.Visibility;

        if (visibility == "public")
            return AuthzDecision.Allow();

        // private or internal: caller must be authenticated
        if (username == null)
            return AuthzDecision.Deny(AuthzDenyReason.VisibilityHidden);

        // Admin bypasses the role check for reads too
        if (isAdmin)
            return AuthzDecision.Allow();

        // Check effective role
        string? effectiveRole = await _resolver.GetEffectiveRoleAsync(username, pkg.FullName);
        if (effectiveRole != null && HasRole(effectiveRole, PackageRoles.Reader))
            return AuthzDecision.Allow();

        // Internal packages: org members of the owning org can read
        if (visibility == "internal")
        {
            string? scopeName = ExtractScope(pkg.FullName);
            if (scopeName != null)
            {
                var scope = await _ctx.Scopes.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Name == scopeName);

                if (scope?.OwnerType == "org" && scope.OwnerOrgId != null)
                {
                    if (await _resolver.IsOrgMemberAsync(username, scope.OwnerOrgId))
                        return AuthzDecision.Allow();
                }

                // User-owned scope: the scope owner can read internal packages
                if (scope?.OwnerType == "user" && scope.OwnerUsername == username)
                    return AuthzDecision.Allow();
            }
        }

        return AuthzDecision.Deny(AuthzDenyReason.VisibilityHidden);
    }

    private async Task<AuthzDecision> AuthorizePackageRoleAsync(
        string? username, bool isAdmin, ResourceRef resource, string requiredRole)
    {
        if (resource is not PackageResource pkg)
            return AuthzDecision.Deny(AuthzDenyReason.PolicyDenied, "Expected PackageResource.");

        if (username == null)
            return AuthzDecision.Deny(AuthzDenyReason.NotAuthenticated);

        // Admin short-circuit: admin role resolves to effective owner on any package.
        if (isAdmin)
            return AuthzDecision.Allow();

        string? effectiveRole = await _resolver.GetEffectiveRoleAsync(username, pkg.FullName);
        if (effectiveRole != null && HasRole(effectiveRole, requiredRole))
            return AuthzDecision.Allow();

        return AuthzDecision.Deny(AuthzDenyReason.PackageRoleInsufficient);
    }

    private async Task<AuthzDecision> AuthorizeCreatePackageAsync(
        string? username, bool isAdmin, ResourceRef resource)
    {
        if (resource is not PackageResource pkg)
            return AuthzDecision.Deny(AuthzDenyReason.PolicyDenied, "Expected PackageResource.");

        if (username == null)
            return AuthzDecision.Deny(AuthzDenyReason.NotAuthenticated);

        // Reserved scopes are denied under every policy and for any role including admin.
        string scope = pkg.Scope.ToLowerInvariant();
        if (Array.IndexOf(ReservedScopes, scope) >= 0)
            return AuthzDecision.Deny(AuthzDenyReason.ScopeReserved,
                $"Scope '@{scope}' is reserved and cannot be published into.");

        // Look up scope record
        var scopeRecord = await _ctx.Scopes.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Name == scope);

        if (scopeRecord == null)
            return AuthzDecision.Deny(AuthzDenyReason.ScopeNotOwned,
                $"Scope '@{scope}' is not claimed — run `stash pkg scope claim @{scope}` first.");

        // Someone else owns it
        bool callerOwnsScope = (scopeRecord.OwnerType == "user" && scopeRecord.OwnerUsername == username)
            || (scopeRecord.OwnerType == "org" && scopeRecord.OwnerOrgId != null
                && await _resolver.IsOrgMemberAsync(username, scopeRecord.OwnerOrgId)
                && await IsOrgOwnerAsync(username, scopeRecord.OwnerOrgId));

        if (!callerOwnsScope && !isAdmin)
            return AuthzDecision.Deny(AuthzDenyReason.ScopeNotOwned,
                $"Scope '@{scope}' is not owned by this account.");

        return AuthzDecision.Allow();
    }

    private async Task<AuthzDecision> AuthorizeClaimScopeAsync(
        string? username, bool isAdmin, ResourceRef resource)
    {
        if (username == null)
            return AuthzDecision.Deny(AuthzDenyReason.NotAuthenticated);

        if (resource is not ScopeResource sr)
            return AuthzDecision.Deny(AuthzDenyReason.PolicyDenied, "Expected ScopeResource.");

        string scope = sr.Scope.ToLowerInvariant();
        if (Array.IndexOf(ReservedScopes, scope) >= 0)
            return AuthzDecision.Deny(AuthzDenyReason.ScopeReserved,
                $"Scope '@{scope}' is reserved.");

        var existing = await _ctx.Scopes.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Name == scope);

        if (existing != null)
        {
            // Already claimed by someone else → deny
            bool ownedByCaller = existing.OwnerType == "user" && existing.OwnerUsername == username;
            if (!ownedByCaller && !isAdmin)
                return AuthzDecision.Deny(AuthzDenyReason.ScopeNotOwned,
                    $"Scope '@{scope}' is already claimed by another account.");
        }

        return AuthzDecision.Allow();
    }

    private async Task<AuthzDecision> AuthorizeVerifyScopeAsync(
        string? username, bool isAdmin, ResourceRef resource)
    {
        if (username == null)
            return AuthzDecision.Deny(AuthzDenyReason.NotAuthenticated);

        if (resource is not ScopeResource sr)
            return AuthzDecision.Deny(AuthzDenyReason.PolicyDenied, "Expected ScopeResource.");

        string scope = sr.Scope.ToLowerInvariant();
        var existing = await _ctx.Scopes.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Name == scope);

        if (existing == null)
            return AuthzDecision.Deny(AuthzDenyReason.ScopeNotOwned, $"Scope '@{scope}' does not exist.");

        bool ownedByCaller = existing.OwnerType == "user" && existing.OwnerUsername == username;
        if (!ownedByCaller && !isAdmin)
            return AuthzDecision.Deny(AuthzDenyReason.ScopeNotOwned,
                $"Only the scope owner may verify '@{scope}'.");

        return AuthzDecision.Allow();
    }

    private async Task<AuthzDecision> AuthorizeOrgOwnerAsync(
        string? username, bool isAdmin, ResourceRef resource)
    {
        if (username == null)
            return AuthzDecision.Deny(AuthzDenyReason.NotAuthenticated);

        if (isAdmin)
            return AuthzDecision.Allow();

        if (resource is not OrgResource orgRes)
            return AuthzDecision.Deny(AuthzDenyReason.PolicyDenied, "Expected OrgResource.");

        var org = await _ctx.Organizations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Name == orgRes.OrgName);

        if (org == null)
            return AuthzDecision.Deny(AuthzDenyReason.PolicyDenied, "Organisation not found.");

        bool isOrgOwner = await IsOrgOwnerAsync(username, org.Id);
        return isOrgOwner
            ? AuthzDecision.Allow()
            : AuthzDecision.Deny(AuthzDenyReason.OrgMembershipRequired, "Org owner role required.");
    }

    private async Task<AuthzDecision> AuthorizeRevokeOwnTokenAsync(
        string? username, ResourceRef resource)
    {
        if (username == null)
            return AuthzDecision.Deny(AuthzDenyReason.NotAuthenticated);

        if (resource is not TokenResource tr)
            return AuthzDecision.Allow(); // generic self-service context

        // Can only revoke own tokens
        var token = await _ctx.Tokens.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tr.TokenId);

        if (token == null || token.Username != username)
            return AuthzDecision.Deny(AuthzDenyReason.PolicyDenied, "Token not found or not owned by caller.");

        return AuthzDecision.Allow();
    }

    private async Task<bool> IsOrgOwnerAsync(string username, string orgId)
    {
        return await _ctx.OrgMembers.AsNoTracking()
            .AnyAsync(m => m.OrgId == orgId && m.Username == username && m.OrgRole == "owner");
    }

    private static string? ExtractScope(string packageName)
    {
        if (!packageName.StartsWith('@')) return null;
        int slash = packageName.IndexOf('/');
        return slash < 2 ? null : packageName[1..slash];
    }
}
