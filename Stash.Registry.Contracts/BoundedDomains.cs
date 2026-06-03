using System.Text.Json.Serialization;

namespace Stash.Registry.Contracts;

// ── Token scope wire values ───────────────────────────────────────────────────

/// <summary>
/// Wire values for the <c>token_scope</c> JWT claim. These are the string values embedded in
/// issued tokens and evaluated by authorization policies in <c>Startup.ConfigureServices</c>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TokenScopes>))]
public enum TokenScopes
{
    /// <summary>Read-only access: package metadata and download.</summary>
    [JsonStringEnumMemberName("read")]
    Read,

    /// <summary>Publish access: create/update/unpublish packages and manage roles.</summary>
    [JsonStringEnumMemberName("publish")]
    Publish,

    /// <summary>Admin access: all publish actions plus administrative operations.</summary>
    [JsonStringEnumMemberName("admin")]
    Admin,
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
[JsonConverter(typeof(JsonStringEnumConverter<PackageRoles>))]
public enum PackageRoles
{
    /// <summary>Full control over the package: transfer, delete, manage roles.</summary>
    [JsonStringEnumMemberName("owner")]
    Owner,

    /// <summary>Maintainer: unpublish, deprecate, change visibility.</summary>
    [JsonStringEnumMemberName("maintainer")]
    Maintainer,

    /// <summary>Publisher: publish new versions.</summary>
    [JsonStringEnumMemberName("publisher")]
    Publisher,

    /// <summary>Reader: download and view package metadata.</summary>
    [JsonStringEnumMemberName("reader")]
    Reader,
}

/// <summary>
/// Helpers for <see cref="PackageRoles"/> ordering.
/// </summary>
public static class PackageRoleHelpers
{
    /// <summary>
    /// Canonical role ordering from highest to lowest privilege.
    /// Lower index = higher privilege.
    /// </summary>
    public static readonly PackageRoles[] RankOrder = [PackageRoles.Owner, PackageRoles.Maintainer, PackageRoles.Publisher, PackageRoles.Reader];

    /// <summary>
    /// Returns the privilege rank of <paramref name="role"/>: lower index = higher privilege.
    /// Returns <see cref="int.MaxValue"/> for unknown roles (lowest possible privilege, fail-closed).
    /// </summary>
    public static int Rank(PackageRoles role)
    {
        int idx = System.Array.IndexOf(RankOrder, role);
        return idx < 0 ? int.MaxValue : idx;
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="role"/> has at least <paramref name="required"/> privilege.
    /// </summary>
    public static bool HasRole(PackageRoles role, PackageRoles required) => Rank(role) <= Rank(required);
}

// ── Org role wire values ──────────────────────────────────────────────────────

/// <summary>
/// Wire values for organization member roles stored in <c>OrgMemberEntry.OrgRole</c>.
/// The set is closed: only <see cref="Owner"/> and <see cref="Member"/> are valid.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<OrgRoles>))]
public enum OrgRoles
{
    /// <summary>Organization member: read access to org-owned packages; no administrative rights.
    /// This is the CLR default (value = 0) and matches the EF column default 'member'.</summary>
    [JsonStringEnumMemberName("member")]
    Member = 0,

    /// <summary>Organization owner: full control over the org, its members, and its scopes.</summary>
    [JsonStringEnumMemberName("owner")]
    Owner = 1,
}

// ── Principal type wire values ────────────────────────────────────────────────

/// <summary>
/// Wire values for the <c>principal_type</c> column in <c>PackageRoleEntry</c>.
/// The set is closed: <see cref="User"/>, <see cref="Team"/>, and <see cref="Org"/>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PrincipalTypes>))]
public enum PrincipalTypes
{
    /// <summary>A single user principal.</summary>
    [JsonStringEnumMemberName("user")]
    User,

    /// <summary>A team principal (a named group of users within an org).</summary>
    [JsonStringEnumMemberName("team")]
    Team,

    /// <summary>An organization principal.</summary>
    [JsonStringEnumMemberName("org")]
    Org,
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
[JsonConverter(typeof(JsonStringEnumConverter<ScopeOwnerTypes>))]
public enum ScopeOwnerTypes
{
    /// <summary>The scope is owned by an individual user.</summary>
    [JsonStringEnumMemberName("user")]
    User,

    /// <summary>The scope is owned by an organization.</summary>
    [JsonStringEnumMemberName("org")]
    Org,

    /// <summary>The scope is system-reserved (e.g. <c>@stash</c>, <c>@admin</c>); not user-claimable.</summary>
    [JsonStringEnumMemberName("system")]
    System,
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
[JsonConverter(typeof(JsonStringEnumConverter<UserRoles>))]
public enum UserRoles
{
    /// <summary>Standard user with publish access.</summary>
    [JsonStringEnumMemberName("user")]
    User,

    /// <summary>Administrator with full access to all registry operations.</summary>
    [JsonStringEnumMemberName("admin")]
    Admin,
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
[JsonConverter(typeof(JsonStringEnumConverter<Visibilities>))]
public enum Visibilities
{
    /// <summary>Package is publicly readable by anyone, including anonymous callers.</summary>
    [JsonStringEnumMemberName("public")]
    Public,

    /// <summary>Package is readable only by authenticated users with an explicit role.</summary>
    [JsonStringEnumMemberName("private")]
    Private,

    /// <summary>Package is readable by authenticated org members of the owning scope's org, and users with an explicit role.</summary>
    [JsonStringEnumMemberName("internal")]
    Internal,
}

/// <summary>
/// Helpers for <see cref="Visibilities"/>.
/// </summary>
public static class VisibilityHelpers
{
    /// <summary>Returns <c>true</c> if <paramref name="visibility"/> is a valid <see cref="Visibilities"/> value.</summary>
    public static bool IsValid(string visibility)
        => visibility is "public" or "private" or "internal";
}

// ── Wire-string conversion helpers ────────────────────────────────────────────

/// <summary>
/// Converts between bounded-domain enum values and their lowercase wire strings.
/// Used at the EF/internal boundary where the JSON serializer is not in play.
/// </summary>
public static class BoundedDomainWire
{
    /// <summary>Returns the lowercase wire string for a <see cref="PackageRoles"/> value.</summary>
    public static string ToWire(this PackageRoles role) => role switch
    {
        PackageRoles.Owner => "owner",
        PackageRoles.Maintainer => "maintainer",
        PackageRoles.Publisher => "publisher",
        PackageRoles.Reader => "reader",
        _ => throw new System.ArgumentOutOfRangeException(nameof(role), role, null)
    };

    /// <summary>Parses a lowercase wire string to a <see cref="PackageRoles"/> value (throws on unknown).</summary>
    public static PackageRoles ToPackageRole(this string wire) => wire switch
    {
        "owner" => PackageRoles.Owner,
        "maintainer" => PackageRoles.Maintainer,
        "publisher" => PackageRoles.Publisher,
        "reader" => PackageRoles.Reader,
        _ => throw new System.ArgumentOutOfRangeException(nameof(wire), wire, null)
    };

    /// <summary>Tries to parse a lowercase wire string to a <see cref="PackageRoles"/> value. Returns false if unknown.</summary>
    public static bool TryToPackageRole(this string wire, out PackageRoles role)
    {
        switch (wire)
        {
            case "owner": role = PackageRoles.Owner; return true;
            case "maintainer": role = PackageRoles.Maintainer; return true;
            case "publisher": role = PackageRoles.Publisher; return true;
            case "reader": role = PackageRoles.Reader; return true;
            default: role = default; return false;
        }
    }

    /// <summary>Returns the lowercase wire string for a <see cref="OrgRoles"/> value.</summary>
    public static string ToWire(this OrgRoles role) => role switch
    {
        OrgRoles.Owner => "owner",
        OrgRoles.Member => "member",
        _ => throw new System.ArgumentOutOfRangeException(nameof(role), role, null)
    };

    /// <summary>Parses a lowercase wire string to an <see cref="OrgRoles"/> value.</summary>
    public static OrgRoles ToOrgRole(this string wire) => wire switch
    {
        "owner" => OrgRoles.Owner,
        "member" => OrgRoles.Member,
        _ => throw new System.ArgumentOutOfRangeException(nameof(wire), wire, null)
    };

    /// <summary>Returns the lowercase wire string for a <see cref="PrincipalTypes"/> value.</summary>
    public static string ToWire(this PrincipalTypes pt) => pt switch
    {
        PrincipalTypes.User => "user",
        PrincipalTypes.Team => "team",
        PrincipalTypes.Org => "org",
        _ => throw new System.ArgumentOutOfRangeException(nameof(pt), pt, null)
    };

    /// <summary>Parses a lowercase wire string to a <see cref="PrincipalTypes"/> value.</summary>
    public static PrincipalTypes ToPrincipalType(this string wire) => wire switch
    {
        "user" => PrincipalTypes.User,
        "team" => PrincipalTypes.Team,
        "org" => PrincipalTypes.Org,
        _ => throw new System.ArgumentOutOfRangeException(nameof(wire), wire, null)
    };

    /// <summary>Returns the lowercase wire string for a <see cref="ScopeOwnerTypes"/> value.</summary>
    public static string ToWire(this ScopeOwnerTypes ot) => ot switch
    {
        ScopeOwnerTypes.User => "user",
        ScopeOwnerTypes.Org => "org",
        ScopeOwnerTypes.System => "system",
        _ => throw new System.ArgumentOutOfRangeException(nameof(ot), ot, null)
    };

    /// <summary>Parses a lowercase wire string to a <see cref="ScopeOwnerTypes"/> value.</summary>
    public static ScopeOwnerTypes ToScopeOwnerType(this string wire) => wire switch
    {
        "user" => ScopeOwnerTypes.User,
        "org" => ScopeOwnerTypes.Org,
        "system" => ScopeOwnerTypes.System,
        _ => throw new System.ArgumentOutOfRangeException(nameof(wire), wire, null)
    };

    /// <summary>Returns the lowercase wire string for a <see cref="UserRoles"/> value.</summary>
    public static string ToWire(this UserRoles role) => role switch
    {
        UserRoles.User => "user",
        UserRoles.Admin => "admin",
        _ => throw new System.ArgumentOutOfRangeException(nameof(role), role, null)
    };

    /// <summary>Parses a lowercase wire string to a <see cref="UserRoles"/> value.</summary>
    public static UserRoles ToUserRole(this string wire) => wire switch
    {
        "user" => UserRoles.User,
        "admin" => UserRoles.Admin,
        _ => throw new System.ArgumentOutOfRangeException(nameof(wire), wire, null)
    };

    /// <summary>Returns the lowercase wire string for a <see cref="TokenScopes"/> value.</summary>
    public static string ToWire(this TokenScopes scope) => scope switch
    {
        TokenScopes.Read => "read",
        TokenScopes.Publish => "publish",
        TokenScopes.Admin => "admin",
        _ => throw new System.ArgumentOutOfRangeException(nameof(scope), scope, null)
    };

    /// <summary>Parses a lowercase wire string to a <see cref="TokenScopes"/> value (null-safe, fails to <see cref="TokenScopes.Read"/>).</summary>
    public static TokenScopes ToTokenScope(this string? wire) => wire switch
    {
        "read" => TokenScopes.Read,
        "publish" => TokenScopes.Publish,
        "admin" => TokenScopes.Admin,
        _ => TokenScopes.Read  // fail-closed
    };

    /// <summary>Returns the lowercase wire string for a <see cref="Visibilities"/> value.</summary>
    public static string ToWire(this Visibilities v) => v switch
    {
        Visibilities.Public => "public",
        Visibilities.Private => "private",
        Visibilities.Internal => "internal",
        _ => throw new System.ArgumentOutOfRangeException(nameof(v), v, null)
    };

    /// <summary>Parses a lowercase wire string to a <see cref="Visibilities"/> value.</summary>
    public static Visibilities ToVisibility(this string wire) => wire switch
    {
        "public" => Visibilities.Public,
        "private" => Visibilities.Private,
        "internal" => Visibilities.Internal,
        _ => throw new System.ArgumentOutOfRangeException(nameof(wire), wire, null)
    };
}
