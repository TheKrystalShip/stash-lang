using System;

namespace Stash.Registry.Contracts;

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
/// Teams are never scope owners (use <see cref="PrincipalTypes"/> for the broader package-role principal set).
/// </summary>
/// <remarks>
/// <see cref="User"/> and <see cref="Org"/> are the only <em>user-claimable</em> owner types.
/// <see cref="System"/> is reserved for internally provisioned scopes (e.g. <c>@stash</c>, <c>@admin</c>)
/// and is never selectable through the claim API.
/// </remarks>
public static class ScopeOwnerTypes
{
    /// <summary>The scope is owned by an individual user.</summary>
    public const string User = "user";

    /// <summary>The scope is owned by an organization.</summary>
    public const string Org = "org";

    /// <summary>The scope is system-reserved (e.g. <c>@stash</c>, <c>@admin</c>); not user-claimable.</summary>
    public const string System = "system";
}

// ── User role wire values ─────────────────────────────────────────────────────

/// <summary>
/// Wire values for the <c>role</c> JWT claim (stored in <see cref="System.Security.Claims.ClaimTypes.Role"/>)
/// and for the <c>role</c> field in <c>CreateUserRequest</c> / <c>CreateUserResponse</c>.
/// </summary>
/// <remarks>
/// Although <c>UserRoles</c> values are embedded in JWT tokens (as the <c>role</c> claim),
/// they are also wire-bound: <c>POST /api/v1/admin/users</c> accepts a <c>role</c> field in
/// its request body and echoes it in the response. Shared with <c>Stash.Cli</c> and any
/// future Razor UI that needs to present or validate user roles.
/// </remarks>
public static class UserRoles
{
    /// <summary>Standard user with publish access.</summary>
    public const string User = "user";

    /// <summary>Administrator with full access to all registry operations.</summary>
    public const string Admin = "admin";
}

// ── Package visibility wire values ────────────────────────────────────────────

/// <summary>
/// Wire values for the <c>visibility</c> column in <c>PackageRecord</c>.
/// The set is closed: <see cref="Public"/>, <see cref="Private"/>, <see cref="Internal"/>.
/// </summary>
/// <remarks>
/// This is the single source of truth for package visibility values, replacing inline
/// literals that previously appeared in <c>PackageRecord</c>, <c>RegistryDbContext</c>,
/// <c>RegistryAuthorizer</c>, <c>StashRegistryDatabase</c>, and <c>PackagesController</c>.
/// </remarks>
public static class Visibilities
{
    /// <summary>Package is publicly readable by anyone, including anonymous callers.</summary>
    public const string Public = "public";

    /// <summary>Package is readable only by authenticated users with an explicit role.</summary>
    public const string Private = "private";

    /// <summary>Package is readable by authenticated org members of the owning scope's org, and users with an explicit role.</summary>
    public const string Internal = "internal";

    /// <summary>All valid visibility values, ordered: public, private, internal.</summary>
    public static readonly string[] All = [Public, Private, Internal];

    /// <summary>Returns <c>true</c> if <paramref name="visibility"/> is a valid visibility value.</summary>
    public static bool IsValid(string visibility) => Array.IndexOf(All, visibility) >= 0;
}
