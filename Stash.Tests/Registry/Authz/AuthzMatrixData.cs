using System.Collections.Generic;

namespace Stash.Tests.Registry.Authz;

/// <summary>
/// Static theory data for <see cref="RegistryAuthzMatrixTests"/>.
/// All expected-value literals (roles, ceilings, visibility, policies, deny reasons) are kept
/// as independent string/int literals here. Keeping them independent of RegistryAuthConstants
/// means a wrong constant value is still caught — the matrix validates production behavior, not
/// that constants and test data agree with each other.
/// </summary>
public static class AuthzMatrixData
{
    // ── Ceiling-axis rows ─────────────────────────────────────────────────────
    // For each ceiling (read / publish / admin), assert allowed actions pass and higher-ceiling
    // actions are denied TokenScopeInsufficient.

    /// <summary>
    /// Each row: (tag, ceiling, actionName, expectedStatus, expectedBody).
    /// actionName is used to construct HTTP request via helper in the test class.
    /// </summary>
    public static IEnumerable<object[]> CeilingAxisRows()
    {
        // read ceiling + GET public → 200
        yield return new object[] { "ceiling.read.get_public.allow", "read", "get_public", 200, null! };
        // read ceiling + PUT (publish action) → 403 TokenScopeInsufficient
        yield return new object[] { "ceiling.read.publish.deny", "read", "publish", 403, "TokenScopeInsufficient" };
        // publish ceiling + PUT → 201
        yield return new object[] { "ceiling.publish.publish.allow", "publish", "publish", 201, null! };
        // publish ceiling + admin endpoint → 403 (ASP.NET RequireAdmin policy; no PDP body)
        yield return new object[] { "ceiling.publish.admin_revoke.deny", "publish", "admin_revoke_role", 403, null! };
        // admin ceiling + PUT → 201 (admin ≥ publish)
        yield return new object[] { "ceiling.admin.publish.allow", "admin", "publish_as_admin", 201, null! };
        // admin ceiling + admin endpoint → 204
        yield return new object[] { "ceiling.admin.admin_revoke.allow", "admin", "admin_revoke_role", 204, null! };
    }

    // ── Role-axis rows ────────────────────────────────────────────────────────
    // For each of reader / publisher / maintainer / owner:
    // - CAN perform every matrix-permitted action
    // - CANNOT perform every higher action (→ PackageRoleInsufficient)
    // Three grant shapes per role: direct, team-mediated, org-owner-inherited.

    public static IEnumerable<object[]> RoleAxisRows()
    {
        // reader: can read private; cannot publish
        yield return new object[] { "role.reader.direct.read_private.allow", "reader", "direct", "read", 200, null! };
        yield return new object[] { "role.reader.team.read_private.allow", "reader", "team", "read", 200, null! };
        yield return new object[] { "role.reader.org.read_private.allow", "reader", "org", "read", 200, null! };
        yield return new object[] { "role.reader.direct.publish.deny", "reader", "direct", "publish", 403, "PackageRoleInsufficient" };
        yield return new object[] { "role.reader.team.publish.deny", "reader", "team", "publish", 403, "PackageRoleInsufficient" };
        yield return new object[] { "role.reader.org.publish.deny", "reader", "org", "publish", 403, "PackageRoleInsufficient" };

        // publisher: can publish new version; cannot change visibility
        yield return new object[] { "role.publisher.direct.publish.allow", "publisher", "direct", "publish", 201, null! };
        yield return new object[] { "role.publisher.team.publish.allow", "publisher", "team", "publish", 201, null! };
        yield return new object[] { "role.publisher.org.publish.allow", "publisher", "org", "publish", 201, null! };
        yield return new object[] { "role.publisher.direct.change_visibility.deny", "publisher", "direct", "change_visibility", 403, "PackageRoleInsufficient" };
        yield return new object[] { "role.publisher.team.change_visibility.deny", "publisher", "team", "change_visibility", 403, "PackageRoleInsufficient" };
        yield return new object[] { "role.publisher.org.change_visibility.deny", "publisher", "org", "change_visibility", 403, "PackageRoleInsufficient" };

        // maintainer: can deprecate; cannot change visibility
        yield return new object[] { "role.maintainer.direct.deprecate.allow", "maintainer", "direct", "deprecate", 200, null! };
        yield return new object[] { "role.maintainer.team.deprecate.allow", "maintainer", "team", "deprecate", 200, null! };
        yield return new object[] { "role.maintainer.org.deprecate.allow", "maintainer", "org", "deprecate", 200, null! };
        yield return new object[] { "role.maintainer.direct.change_visibility.deny", "maintainer", "direct", "change_visibility", 403, "PackageRoleInsufficient" };
        yield return new object[] { "role.maintainer.team.change_visibility.deny", "maintainer", "team", "change_visibility", 403, "PackageRoleInsufficient" };
        yield return new object[] { "role.maintainer.org.change_visibility.deny", "maintainer", "org", "change_visibility", 403, "PackageRoleInsufficient" };

        // owner: can change visibility; is not blocked by role
        yield return new object[] { "role.owner.direct.change_visibility.allow", "owner", "direct", "change_visibility", 200, null! };
        yield return new object[] { "role.owner.team.change_visibility.allow", "owner", "team", "change_visibility", 200, null! };
        yield return new object[] { "role.owner.org.change_visibility.allow", "owner", "org", "change_visibility", 200, null! };
    }

    // ── Visibility-axis rows ──────────────────────────────────────────────────

    public static IEnumerable<object[]> VisibilityAxisRows()
    {
        // public: anonymous allow
        yield return new object[] { "vis.public.anon.allow", "public", "anon", 200, null! };
        // public: authenticated allow
        yield return new object[] { "vis.public.authenticated.allow", "public", "authenticated", 200, null! };

        // private: reader+ allow
        yield return new object[] { "vis.private.reader.allow", "private", "reader", 200, null! };
        // private: anonymous → 404 (VisibilityHidden reason mapped to 404 by controller; body is "not found")
        yield return new object[] { "vis.private.anon.deny", "private", "anon", 404, null! };
        // private: unrelated authenticated user → 404
        yield return new object[] { "vis.private.unrelated_user.deny", "private", "unrelated_user", 404, null! };

        // internal: org member of org-owned scope → allow
        yield return new object[] { "vis.internal.org_member.allow", "internal", "org_member", 200, null! };
        // internal: owning user of user-owned scope → allow
        yield return new object[] { "vis.internal.scope_owner.allow", "internal", "scope_owner", 200, null! };
        // internal: direct reader → allow
        yield return new object[] { "vis.internal.direct_reader.allow", "internal", "direct_reader", 200, null! };
        // internal: anonymous → 404 (VisibilityHidden reason mapped to 404)
        yield return new object[] { "vis.internal.anon.deny", "internal", "anon", 404, null! };
        // internal: non-member unrelated user → 404
        yield return new object[] { "vis.internal.non_member.deny", "internal", "non_member", 404, null! };
    }

    // ── ScopeOwnershipPolicy-axis rows ────────────────────────────────────────

    public static IEnumerable<object[]> ScopeOwnershipPolicyRows()
    {
        // Open: unowned scope → auto-claim + allow
        yield return new object[] { "policy.open.unowned.allow", "open", "unowned", 201, null! };
        // Open: scope owned by different user → deny ScopeNotOwned
        yield return new object[] { "policy.open.other_owner.deny", "open", "other_owner", 403, "ScopeNotOwned" };

        // Claim (default): unowned scope → deny ScopeNotOwned
        yield return new object[] { "policy.claim.unowned.deny", "claim", "unowned", 403, "ScopeNotOwned" };
        // Claim: owned scope → allow
        yield return new object[] { "policy.claim.owned.allow", "claim", "owned", 201, null! };

        // Verified: pending (unowned) scope → deny ScopeNotOwned with verify message
        yield return new object[] { "policy.verified.unowned.deny", "verified", "unowned", 403, "ScopeNotOwned" };
    }

    // ── Intersection crux rows (security-critical PDP intersections) ──────────

    public static IEnumerable<object[]> IntersectionCruxRows()
    {
        // owner role + read token requesting publish → DENY TokenScopeInsufficient (ceiling fires first)
        yield return new object[] { "crux.owner_role.read_ceiling.publish.deny_ceiling", "read", "owner", "publish", 403, "TokenScopeInsufficient" };
        // reader role + admin token requesting publish → DENY PackageRoleInsufficient (ceiling allows, resource denies)
        yield return new object[] { "crux.reader_role.admin_ceiling.publish.deny_role", "admin", "reader", "publish", 403, "PackageRoleInsufficient" };
        // admin role + read token requesting any write → DENY TokenScopeInsufficient (Q2 narrow)
        yield return new object[] { "crux.admin_role.read_ceiling.publish.deny_ceiling", "read", "admin_role", "publish", 403, "TokenScopeInsufficient" };
    }

    // ── Bug A regression rows ─────────────────────────────────────────────────

    public static IEnumerable<object[]> BugARegressionRows()
    {
        // Publish into scope owned by someone else → ScopeNotOwned
        yield return new object[] { "bugA.other_owner_scope.deny", "other_owner", 403, "ScopeNotOwned" };
        // Reserved @stash under claim → ScopeReserved
        yield return new object[] { "bugA.stash_reserved.claim.deny", "stash", 403, "ScopeReserved" };
        // Reserved @admin under claim → ScopeReserved
        yield return new object[] { "bugA.admin_reserved.claim.deny", "admin_scope", 403, "ScopeReserved" };
        // Reserved @stash even for admin role → ScopeReserved
        yield return new object[] { "bugA.stash_reserved.admin_role.deny", "stash_admin_role", 403, "ScopeReserved" };
    }

    // ── Bug B regression rows ─────────────────────────────────────────────────

    public static IEnumerable<object[]> BugBRegressionRows()
    {
        // Manifest name ≠ route → 400 ManifestRouteMismatch
        yield return new object[] { "bugB.name_mismatch.deny", "name", 400, "ManifestRouteMismatch" };
        // Manifest version ≠ X-Package-Version header → 400 ManifestRouteMismatch
        yield return new object[] { "bugB.version_mismatch.deny", "version", 400, "ManifestRouteMismatch" };
    }

    // ── Token conformance rows ────────────────────────────────────────────────

    public static IEnumerable<object[]> TokenConformanceRows()
    {
        // Default login token (read ceiling) denied PublishVersion
        yield return new object[] { "token.login_default.publish.deny", "login_default", 403, "TokenScopeInsufficient" };
        // Expired token → 401 (JWT validation fails at auth middleware)
        yield return new object[] { "token.expired.any.deny", "expired", 401, null! };
        // POST auth/tokens with expires_in > MaxTokenLifetime → 400 TokenLifetimeExceeded
        yield return new object[] { "token.lifetime_exceeded.deny", "lifetime_exceeded", 400, "TokenLifetimeExceeded" };
    }

    // ── Role revocation conformance rows ─────────────────────────────────────

    public static IEnumerable<object[]> RoleRevokeConformanceRows()
    {
        // Happy path: owner revokes non-owner role → 204
        yield return new object[] { "revoke.owner_revokes_publisher.allow", "owner_revokes", 204, null! };
        // Non-owner attempt → 403 PackageRoleInsufficient
        yield return new object[] { "revoke.non_owner_attempt.deny", "non_owner_revoke", 403, "PackageRoleInsufficient" };
        // Last owner refusal → 409
        yield return new object[] { "revoke.last_owner.deny", "last_owner", 409, "cannot remove the last owner" };
        // Admin override revoke → 204
        yield return new object[] { "revoke.admin_override.allow", "admin_override", 204, null! };
        // Admin still blocked by last-owner invariant → 409
        yield return new object[] { "revoke.admin_last_owner.deny", "admin_last_owner", 409, "cannot remove the last owner" };
    }

    // ── Fail-closed cascade conformance rows ─────────────────────────────────

    public static IEnumerable<object[]> FailClosedCascadeRows()
    {
        // Role grant via deleted team yields no access → 403 PackageRoleInsufficient
        yield return new object[] { "fail_closed.dangling_team.deny", "dangling_team", 403, "PackageRoleInsufficient" };
        // DELETE non-empty scope → 409 ScopeNotEmpty
        yield return new object[] { "fail_closed.delete_nonempty_scope.deny", "delete_scope_nonempty", 409, "ScopeNotEmpty" };
        // DELETE non-empty org → 409 OrgNotEmpty
        yield return new object[] { "fail_closed.delete_nonempty_org.deny", "delete_org_nonempty", 409, "OrgNotEmpty" };
        // After children removed, scope delete succeeds
        yield return new object[] { "fail_closed.delete_scope_after_removal.allow", "delete_scope_empty", 204, null! };
        // After children removed, org delete succeeds
        yield return new object[] { "fail_closed.delete_org_after_removal.allow", "delete_org_empty", 204, null! };
    }

    // ── Audit conformance rows ────────────────────────────────────────────────

    public static IEnumerable<object[]> AuditConformanceRows()
    {
        // Successful PublishVersion → exactly one audit entry tagged package.publish
        yield return new object[] { "audit.publish_version.one_entry", "publish_version_audit", "package.publish", "allow" };
        // Successful AssignPackageRole → exactly one role.assign
        yield return new object[] { "audit.assign_role.one_entry", "assign_role_audit", "role.assign", "allow" };
        // Successful RevokePackageRole → exactly one role.revoke
        yield return new object[] { "audit.revoke_role.one_entry", "revoke_role_audit", "role.revoke", "allow" };
        // Authenticated denied PUT → one deny entry with matching reason
        yield return new object[] { "audit.authenticated_deny.one_entry", "authenticated_deny_audit", "PackageRoleInsufficient", "deny" };
    }

    // ── JTI revocation conformance row ───────────────────────────────────────

    public static IEnumerable<object[]> JtiRevocationConformanceRows()
    {
        // After RevokeOwnToken, request with revoked JWT → 401 TokenRevoked even on future-exp token
        yield return new object[] { "jti.revoked_token.any_endpoint.deny", "revoked_jti" };
    }

    // ── DeleteOrg deny audit-id conformance rows ──────────────────────────────
    // Pins that the filter writes the bare {org} string (no prefix) as resource_id
    // when an authenticated non-owner attempts DELETE /api/v1/orgs/{org}.
    // This row locks in that the mechanical fold of DeleteOrg did NOT change the
    // audit resource_id shape (zero-behavior-delta confirmation).

    public static IEnumerable<object[]> DeleteOrgDenyAuditIdRows()
    {
        // Non-owner authenticated DELETE /orgs/{org} → 403 OrgMembershipRequired,
        // one deny audit entry with resource_id == orgName (bare, no @ prefix).
        yield return new object[] { "delete_org.deny.audit_id", "non_owner_deny_audit_id" };
    }

    // ── DeleteScope deny audit-id conformance rows ────────────────────────────
    // Pins that the filter writes '@' + scope as resource_id when an authenticated
    // non-owner attempts DELETE /api/v1/scopes/{scope}.
    // This row locks in that the mechanical fold of DeleteScope conforms to the
    // '@' prefix convention used by the prior inline audit in the controller.

    public static IEnumerable<object[]> DeleteScopeDenyAuditIdRows()
    {
        // Non-owner authenticated DELETE /scopes/{scope} → 403 ScopeNotOwned,
        // one deny audit entry with resource_id == '@' + scope.
        yield return new object[] { "delete_scope.deny.audit_id", "non_owner_deny_audit_id" };
    }

    // ── PublishPackage deny-label conformance rows ────────────────────────────
    // Pins that the deny-path audit action label is uniformly 'PublishPackage'
    // regardless of DB state (whether the package already exists or not).
    // The allow-path labels (package.create / package.publish) remain unchanged
    // as they come from PackageService.PublishAsync's isNewPackage return.

    public static IEnumerable<object[]> PublishDenyLabelRows()
    {
        // Read-ceiling token denied on PUT /packages/{scope}/{name}
        // Confirms the filter writes action='PublishPackage' (not CreatePackage or PublishVersion).
        yield return new object[] { "publish.deny.label", "read_ceiling_publish_deny" };
    }

    // ── Version-deny body-shape conformance rows ──────────────────────────────
    // Pins the exact Error string AND the absence of Message for GetVersion /
    // DownloadVersion visibility-hidden denials. Guards F01 (version-scoped message)
    // and F02 (no extra Message field) together.

    public static IEnumerable<object[]> VersionDenyBodyRows()
    {
        // Anonymous caller → private package GetVersion → 404 with version-scoped Error, no Message
        yield return new object[] {
            "version_deny.get_version.anon.private",
            "get_version", "anon", "private", "1.0.0" };
        // Anonymous caller → private package DownloadVersion → 404 with version-scoped Error, no Message
        yield return new object[] {
            "version_deny.download_version.anon.private",
            "download_version", "anon", "private", "1.0.0" };
    }
}
