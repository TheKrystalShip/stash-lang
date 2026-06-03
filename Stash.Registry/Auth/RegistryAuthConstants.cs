using System;
using Stash.Registry.Contracts;

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

// ── Reserved system scope names ───────────────────────────────────────────────

/// <summary>
/// Scope names that are reserved for the system and can never be user-claimed
/// (their scopes carry <see cref="ScopeOwnerTypes.System"/> ownership).
/// </summary>
/// <remarks>
/// This is the single source of truth for the reserved set, which was previously
/// duplicated as local arrays in <c>ScopesController</c>, <c>OrganizationsController</c>,
/// <c>RegistryAuthorizer</c>, and <c>StashRegistryDatabase</c>.
/// </remarks>
public static class ReservedScopes
{
    /// <summary>The <c>@stash</c> reserved scope.</summary>
    public const string Stash = "stash";

    /// <summary>The <c>@admin</c> reserved scope.</summary>
    public const string Admin = "admin";

    /// <summary>All reserved scope names.</summary>
    public static readonly string[] All = [Stash, Admin];

    /// <summary>Returns <c>true</c> if <paramref name="scope"/> is a reserved system scope name.</summary>
    public static bool IsReserved(string scope) => Array.IndexOf(All, scope) >= 0;
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
        "admin" => Authorization.TokenCeiling.Admin,
        "publish" => Authorization.TokenCeiling.Publish,
        _ => Authorization.TokenCeiling.Read
    };

    /// <summary>
    /// Converts a <see cref="Authorization.TokenCeiling"/> to its wire string value.
    /// </summary>
    public static string ToClaimValue(this Authorization.TokenCeiling ceiling) => ceiling switch
    {
        Authorization.TokenCeiling.Admin => "admin",
        Authorization.TokenCeiling.Publish => "publish",
        _ => "read"
    };
}
