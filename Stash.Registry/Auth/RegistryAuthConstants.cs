using System;

namespace Stash.Registry.Auth;

// ── Claim names ──────────────────────────────────────────────────────────────

/// <summary>
/// Custom JWT claim names used by the Stash package registry.
/// </summary>
/// <remarks>
/// Do NOT add framework claim names here — use <see cref="System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames"/>
/// for <c>sub</c>, <c>jti</c>, <c>name</c>, and <see cref="System.Security.Claims.ClaimTypes.Role"/>
/// for the role claim. Only Stash-specific custom claim names belong in this class.
/// </remarks>
public static class RegistryClaims
{
    /// <summary>The <c>token_scope</c> claim: carries the permission scope of the token.</summary>
    public const string TokenScope = "token_scope";

    /// <summary>The <c>machine_id</c> claim: SHA-256 machine fingerprint for refresh-token binding.</summary>
    public const string MachineId = "machine_id";
}

// ── Token scope wire values ───────────────────────────────────────────────────

/// <summary>
/// Wire values for the <c>token_scope</c> JWT claim. These are the string values embedded in
/// issued tokens and evaluated by authorization policies in <c>Startup.ConfigureServices</c>.
/// </summary>
public static class TokenScopes
{
    /// <summary>Read-only access: package metadata and download.</summary>
    public const string Read = "read";

    /// <summary>Publish access: create/update/unpublish packages and manage roles.</summary>
    public const string Publish = "publish";

    /// <summary>Admin access: all publish actions plus administrative operations.</summary>
    public const string Admin = "admin";
}

// ── User role wire values ─────────────────────────────────────────────────────

/// <summary>
/// Wire values for the <c>role</c> JWT claim (stored in <see cref="System.Security.Claims.ClaimTypes.Role"/>).
/// </summary>
public static class UserRoles
{
    /// <summary>Standard user with publish access.</summary>
    public const string User = "user";

    /// <summary>Administrator with full access to all registry operations.</summary>
    public const string Admin = "admin";
}

// ── Package role wire values ──────────────────────────────────────────────────

/// <summary>
/// Wire values for package-level roles stored in <c>PackageRoleEntry.Role</c>.
/// </summary>
/// <remarks>
/// <para>
/// Roles are ordered from highest to lowest privilege: <see cref="Owner"/> &gt; <see cref="Maintainer"/> &gt;
/// <see cref="Publisher"/> &gt; <see cref="Reader"/>.
/// </para>
/// <para>
/// <see cref="RankOrder"/> and <see cref="Rank"/> are the single source of truth for role ordering
/// across all three implementations that previously duplicated it
/// (<c>PermissionResolver</c>, <c>RegistryAuthorizer</c>, <c>StashRegistryDatabase</c>).
/// </para>
/// </remarks>
public static class PackageRoles
{
    /// <summary>Full control over the package: transfer, delete, manage roles.</summary>
    public const string Owner = "owner";

    /// <summary>Maintainer: unpublish, deprecate, change visibility.</summary>
    public const string Maintainer = "maintainer";

    /// <summary>Publisher: publish new versions.</summary>
    public const string Publisher = "publisher";

    /// <summary>Reader: download and view package metadata.</summary>
    public const string Reader = "reader";

    /// <summary>
    /// Canonical role ordering from highest to lowest privilege.
    /// Lower index = higher privilege.
    /// </summary>
    public static readonly string[] RankOrder = [Owner, Maintainer, Publisher, Reader];

    /// <summary>
    /// Returns the privilege rank of <paramref name="role"/>: lower index = higher privilege.
    /// Returns <see cref="int.MaxValue"/> for unknown roles (lowest possible privilege, fail-closed).
    /// </summary>
    public static int Rank(string role)
    {
        int idx = Array.IndexOf(RankOrder, role);
        return idx < 0 ? int.MaxValue : idx;
    }
}

// ── Authorization policy names ────────────────────────────────────────────────

/// <summary>
/// Authorization policy names registered in <c>Startup.ConfigureServices</c>.
/// These are compile-time constants so they can be used in <c>[Authorize(Policy = ...)]</c> attributes.
/// </summary>
public static class AuthPolicies
{
    /// <summary>Token scope must be <c>read</c>, <c>publish</c>, or <c>admin</c>.</summary>
    public const string RequireReadScope = "RequireReadScope";

    /// <summary>Token scope must be <c>publish</c> or <c>admin</c>.</summary>
    public const string RequirePublishScope = "RequirePublishScope";

    /// <summary>Token scope must be <c>admin</c>.</summary>
    public const string RequireAdminScope = "RequireAdminScope";

    /// <summary>Token scope must be <c>admin</c> AND user role must be <c>admin</c>.</summary>
    public const string RequireAdmin = "RequireAdmin";
}

// ── Org role wire values ──────────────────────────────────────────────────────

/// <summary>
/// Wire values for organization member roles stored in <c>OrgMemberEntry.OrgRole</c>.
/// The set is closed: only <see cref="Owner"/> and <see cref="Member"/> are valid.
/// </summary>
public static class OrgRoles
{
    /// <summary>Organization owner: full control over the org, its members, and its scopes.</summary>
    public const string Owner = "owner";

    /// <summary>Organization member: read access to org-owned packages; no administrative rights.</summary>
    public const string Member = "member";
}

// ── Principal type wire values ────────────────────────────────────────────────

/// <summary>
/// Wire values for the <c>principal_type</c> column in <c>PackageRoleEntry</c>.
/// The set is closed: <see cref="User"/>, <see cref="Team"/>, and <see cref="Org"/>.
/// </summary>
public static class PrincipalTypes
{
    /// <summary>A single user principal.</summary>
    public const string User = "user";

    /// <summary>A team principal (a named group of users within an org).</summary>
    public const string Team = "team";

    /// <summary>An organization principal.</summary>
    public const string Org = "org";

    /// <summary>All valid principal types, ordered: user, team, org.</summary>
    public static readonly string[] All = [User, Team, Org];
}

// ── Scope owner type wire values ──────────────────────────────────────────────

/// <summary>
/// Wire values for the <c>owner_type</c> column in <c>ScopeRecord</c>.
/// Only <see cref="User"/> and <see cref="Org"/> are legal scope owners — teams are never
/// scope owners (use <see cref="PrincipalTypes"/> for the broader package-role principal set).
/// </summary>
public static class ScopeOwnerTypes
{
    /// <summary>The scope is owned by an individual user.</summary>
    public const string User = "user";

    /// <summary>The scope is owned by an organization.</summary>
    public const string Org = "org";
}

// ── Token ceiling converter ───────────────────────────────────────────────────

/// <summary>
/// Converts between the <c>token_scope</c> claim wire string and the <see cref="Authorization.TokenCeiling"/> enum.
/// This is the single switch in the codebase that parses the wire format; all other code should call these helpers.
/// </summary>
public static class TokenCeilingConverter
{
    /// <summary>
    /// Parses a <c>token_scope</c> claim value into a <see cref="Authorization.TokenCeiling"/>.
    /// Unknown or null values default to <see cref="Authorization.TokenCeiling.Read"/> (fail-closed).
    /// </summary>
    public static Authorization.TokenCeiling FromClaimValue(string? value) => value switch
    {
        TokenScopes.Admin => Authorization.TokenCeiling.Admin,
        TokenScopes.Publish => Authorization.TokenCeiling.Publish,
        _ => Authorization.TokenCeiling.Read
    };

    /// <summary>
    /// Converts a <see cref="Authorization.TokenCeiling"/> to its wire string value.
    /// </summary>
    public static string ToClaimValue(this Authorization.TokenCeiling ceiling) => ceiling switch
    {
        Authorization.TokenCeiling.Admin => TokenScopes.Admin,
        Authorization.TokenCeiling.Publish => TokenScopes.Publish,
        _ => TokenScopes.Read
    };
}
