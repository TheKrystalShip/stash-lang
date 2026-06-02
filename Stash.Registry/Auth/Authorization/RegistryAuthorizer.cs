using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Stash.Registry.Auth;
using Stash.Registry.Configuration;
using Stash.Registry.Contracts;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;

namespace Stash.Registry.Auth.Authorization;

/// <summary>
/// Default implementation of <see cref="IRegistryAuthorizer"/>.
/// Evaluates the two-step intersection: ceiling check first, then resource-side check.
/// </summary>
public sealed class RegistryAuthorizer : IRegistryAuthorizer
{
    private readonly IPermissionResolver _resolver;
    private readonly RegistryDbContext _ctx;
    private readonly ScopeChallengeService _scopeChallenge;
    private readonly ScopeOwnershipPolicyKind _scopePolicy;

    private static int RoleRank(string role) => PackageRoles.Rank(role);

    /// <summary>Returns true when <paramref name="effective"/> satisfies <paramref name="required"/>.</summary>
    private static bool HasRole(string? effective, string required) =>
        effective != null && RoleRank(effective) <= RoleRank(required);

    /// <summary>
    /// Initialises the authorizer with its dependencies.
    /// </summary>
    public RegistryAuthorizer(
        IPermissionResolver resolver,
        RegistryDbContext ctx,
        ScopeChallengeService scopeChallenge,
        RegistryConfig config)
    {
        _resolver = resolver;
        _ctx = ctx;
        _scopeChallenge = scopeChallenge;
        _scopePolicy = config.Security.ScopeOwnershipPolicy;
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
        RegistryAction.PublishPackage => TokenCeiling.Publish,
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
        RegistryAction.DeleteScope => TokenCeiling.Publish,
        RegistryAction.CreateOrg => TokenCeiling.Publish,
        RegistryAction.AddOrgMember => TokenCeiling.Publish,
        RegistryAction.RemoveOrgMember => TokenCeiling.Publish,
        RegistryAction.CreateTeam => TokenCeiling.Publish,
        RegistryAction.AddTeamMember => TokenCeiling.Publish,
        RegistryAction.DeleteOrg => TokenCeiling.Publish,
        RegistryAction.IssueToken => TokenCeiling.Read,

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

            // ── PublishPackage — public dispatch: delegates to CreatePackage or PublishVersion
            //    resource-side handler based on DB existence check ──────────────
            RegistryAction.PublishPackage =>
                await AuthorizePublishPackageAsync(username, isAdmin, resource),

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

            RegistryAction.DeleteScope =>
                await AuthorizeDeleteScopeAsync(username, isAdmin, resource),

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

            RegistryAction.DeleteOrg =>
                await AuthorizeDeleteOrgAsync(username, isAdmin, resource),

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
                await AuthorizeRevokeOwnTokenAsync(username, isAdmin, resource),

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

        if (visibility == Visibilities.Public)
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
        if (visibility == Visibilities.Internal)
        {
            string? scopeName = ExtractScope(pkg.FullName);
            if (scopeName != null)
            {
                var scope = await _ctx.Scopes.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Name == scopeName);

                if (scope?.OwnerType == ScopeOwnerTypes.Org && scope.OwnerOrgId != null)
                {
                    if (await _resolver.IsOrgMemberAsync(username, scope.OwnerOrgId))
                        return AuthzDecision.Allow();
                }

                // User-owned scope: the scope owner can read internal packages
                if (scope?.OwnerType == ScopeOwnerTypes.User && scope.OwnerUsername == username)
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
        if (ReservedScopes.IsReserved(scope))
            return AuthzDecision.Deny(AuthzDenyReason.ScopeReserved,
                $"Scope '@{scope}' is reserved and cannot be published into.");

        // Look up scope record
        var scopeRecord = await _ctx.Scopes.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Name == scope);

        // ── Unowned scope: branch by policy ──────────────────────────────────────
        if (scopeRecord == null)
        {
            return _scopePolicy switch
            {
                ScopeOwnershipPolicyKind.Open => await AutoClaimScopeAsync(username, scope),
                ScopeOwnershipPolicyKind.Verified => AuthzDecision.Deny(AuthzDenyReason.ScopeNotOwned,
                    $"Scope '@{scope}' requires verification — run `stash pkg scope claim @{scope}` then `verify`."),
                _ => AuthzDecision.Deny(AuthzDenyReason.ScopeNotOwned,
                    $"Scope '@{scope}' is not claimed — run `stash pkg scope claim @{scope}` first.")
            };
        }

        // ── Verified mode: pending scope is treated as unowned ────────────────────
        if (_scopePolicy == ScopeOwnershipPolicyKind.Verified
            && scopeRecord.State == ScopeStates.Pending)
        {
            // Treat pending as unowned — same message as the "scope does not exist" branch
            return AuthzDecision.Deny(AuthzDenyReason.ScopeNotOwned,
                $"Scope '@{scope}' requires verification — run `stash pkg scope claim @{scope}` then `verify`.");
        }

        // ── Someone else owns it ──────────────────────────────────────────────────
        bool callerOwnsScope = (scopeRecord.OwnerType == ScopeOwnerTypes.User && scopeRecord.OwnerUsername == username)
            || (scopeRecord.OwnerType == ScopeOwnerTypes.Org && scopeRecord.OwnerOrgId != null
                && await _resolver.IsOrgMemberAsync(username, scopeRecord.OwnerOrgId)
                && await IsOrgOwnerAsync(username, scopeRecord.OwnerOrgId));

        if (!callerOwnsScope && !isAdmin)
            return AuthzDecision.Deny(AuthzDenyReason.ScopeNotOwned,
                $"Scope '@{scope}' is not owned by this account.");

        return AuthzDecision.Allow();
    }

    /// <summary>
    /// Atomically claims a user-scope for the caller under Open policy (auto-claim on first publish).
    /// Returns ALLOW on success (or if scope was already claimed by the caller in a race);
    /// returns DENY(ScopeNotOwned) if a concurrent request claimed it for a different user.
    /// </summary>
    private async Task<AuthzDecision> AutoClaimScopeAsync(string username, string scope)
    {
        var (succeeded, _) = await _scopeChallenge.TryClaimAsync(
            scope, ScopeOwnerTypes.User, username, username);

        if (succeeded)
            return AuthzDecision.Allow();

        // Collision: re-read to see who won the race
        var existing = await _ctx.Scopes.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Name == scope);

        if (existing != null)
        {
            // The caller themselves won a parallel race (e.g. they retried immediately)
            bool callerOwns = existing.OwnerType == ScopeOwnerTypes.User
                              && existing.OwnerUsername == username;
            if (callerOwns)
                return AuthzDecision.Allow();
        }

        return AuthzDecision.Deny(AuthzDenyReason.ScopeNotOwned,
            $"Scope '@{scope}' was claimed by another account during this request.");
    }

    private async Task<AuthzDecision> AuthorizePublishPackageAsync(
        string? username, bool isAdmin, ResourceRef resource)
    {
        if (resource is not PackageResource pkg)
            return AuthzDecision.Deny(AuthzDenyReason.PolicyDenied, "Expected PackageResource.");

        if (username == null)
            return AuthzDecision.Deny(AuthzDenyReason.NotAuthenticated);

        bool exists = await _ctx.Packages.AsNoTracking()
            .AnyAsync(p => p.Name == pkg.FullName);

        return exists
            ? await AuthorizePackageRoleAsync(username, isAdmin, resource, PackageRoles.Publisher)
            : await AuthorizeCreatePackageAsync(username, isAdmin, resource);
    }

    private async Task<AuthzDecision> AuthorizeClaimScopeAsync(
        string? username, bool isAdmin, ResourceRef resource)
    {
        if (username == null)
            return AuthzDecision.Deny(AuthzDenyReason.NotAuthenticated);

        if (resource is not ScopeResource sr)
            return AuthzDecision.Deny(AuthzDenyReason.PolicyDenied, "Expected ScopeResource.");

        string scope = sr.Scope.ToLowerInvariant();
        if (ReservedScopes.IsReserved(scope))
            return AuthzDecision.Deny(AuthzDenyReason.ScopeReserved,
                $"Scope '@{scope}' is reserved.");

        var existing = await _ctx.Scopes.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Name == scope);

        if (existing != null)
        {
            // Already claimed by someone else → deny
            bool ownedByCaller = existing.OwnerType == ScopeOwnerTypes.User && existing.OwnerUsername == username;
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

        bool ownedByCaller = existing.OwnerType == ScopeOwnerTypes.User && existing.OwnerUsername == username;
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
        string? username, bool isAdmin, ResourceRef resource)
    {
        if (username == null)
            return AuthzDecision.Deny(AuthzDenyReason.NotAuthenticated);

        if (resource is not TokenResource tr)
            return AuthzDecision.Allow(); // generic self-service context

        // Admin short-circuit: admin role may revoke any token.
        if (isAdmin)
            return AuthzDecision.Allow();

        // Can only revoke own tokens
        var token = await _ctx.Tokens.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tr.TokenId);

        if (token == null || token.Username != username)
            return AuthzDecision.Deny(AuthzDenyReason.PolicyDenied, "Token not found or not owned by caller.");

        return AuthzDecision.Allow();
    }

    private async Task<AuthzDecision> AuthorizeDeleteScopeAsync(
        string? username, bool isAdmin, ResourceRef resource)
    {
        if (username == null)
            return AuthzDecision.Deny(AuthzDenyReason.NotAuthenticated);

        if (resource is not ScopeResource sr)
            return AuthzDecision.Deny(AuthzDenyReason.PolicyDenied, "Expected ScopeResource.");

        string scope = sr.Scope.ToLowerInvariant();

        // Reserved scopes cannot be deleted by anyone.
        if (ReservedScopes.IsReserved(scope))
            return AuthzDecision.Deny(AuthzDenyReason.ScopeReserved,
                $"Scope '@{scope}' is a reserved system scope and cannot be deleted.");

        var existing = await _ctx.Scopes.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Name == scope);

        // Scope does not exist — allow through so the controller returns 404.
        if (existing == null)
            return AuthzDecision.Allow();

        // Admin may delete any non-reserved scope.
        if (isAdmin)
            return AuthzDecision.Allow();

        bool callerOwns = existing.OwnerType == ScopeOwnerTypes.User && existing.OwnerUsername == username;
        return callerOwns
            ? AuthzDecision.Allow()
            : AuthzDecision.Deny(AuthzDenyReason.ScopeNotOwned,
                $"Scope '@{scope}' is not owned by this account.");
    }

    private async Task<AuthzDecision> AuthorizeDeleteOrgAsync(
        string? username, bool isAdmin, ResourceRef resource)
    {
        if (username == null)
            return AuthzDecision.Deny(AuthzDenyReason.NotAuthenticated);

        if (resource is not OrgResource orgRes)
            return AuthzDecision.Deny(AuthzDenyReason.PolicyDenied, "Expected OrgResource.");

        var org = await _ctx.Organizations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Name == orgRes.OrgName);

        // Org does not exist — allow through so the controller returns 404.
        if (org == null)
            return AuthzDecision.Allow();

        if (isAdmin)
            return AuthzDecision.Allow();

        bool isOrgOwner = await IsOrgOwnerAsync(username, org.Id);
        return isOrgOwner
            ? AuthzDecision.Allow()
            : AuthzDecision.Deny(AuthzDenyReason.OrgMembershipRequired,
                $"User '{username}' is not an owner of organization '{orgRes.OrgName}'.");
    }

    private async Task<bool> IsOrgOwnerAsync(string username, string orgId)
    {
        return await _ctx.OrgMembers.AsNoTracking()
            .AnyAsync(m => m.OrgId == orgId && m.Username == username && m.OrgRole == OrgRoles.Owner);
    }

    private static string? ExtractScope(string packageName)
    {
        if (!packageName.StartsWith('@')) return null;
        int slash = packageName.IndexOf('/');
        return slash < 2 ? null : packageName[1..slash];
    }
}
