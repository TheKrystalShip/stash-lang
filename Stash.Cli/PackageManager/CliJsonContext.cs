using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stash.Cli.PackageManager;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for all JSON types used by
/// the Stash CLI package-manager subsystem.
/// </summary>
/// <remarks>
/// <para>
/// Registering types here enables AOT-safe (trimming- and NativeAOT-compatible)
/// JSON serialisation without runtime reflection. The context is consumed by
/// <see cref="RegistryClient"/> and <see cref="UserConfig"/> wherever
/// <see cref="JsonSerializer"/> is called.
/// </para>
/// <para>
/// Generation options applied to all registered types:
/// <list type="bullet">
///   <item><description>Property name matching is case-insensitive during deserialisation.</description></item>
///   <item><description>Properties with <c>null</c> values are omitted when serialising.</description></item>
///   <item><description>Output JSON is indented for human readability.</description></item>
/// </list>
/// </para>
/// </remarks>
[JsonSerializable(typeof(UserConfig))]
[JsonSerializable(typeof(RegistryEntry))]
[JsonSerializable(typeof(Dictionary<string, RegistryEntry>))]
[JsonSerializable(typeof(SearchResults))]
[JsonSerializable(typeof(SearchResultPackage))]
[JsonSerializable(typeof(List<SearchResultPackage>))]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(AssignRoleRequest))]
[JsonSerializable(typeof(RevokeRoleRequest))]
[JsonSerializable(typeof(TokenCreateRequest))]
[JsonSerializable(typeof(TokenCreateResult))]
[JsonSerializable(typeof(TokenListResult))]
[JsonSerializable(typeof(TokenListItemResult))]
[JsonSerializable(typeof(List<TokenListItemResult>))]
[JsonSerializable(typeof(TokenRefreshRequest))]
[JsonSerializable(typeof(DeprecatePackageCliRequest))]
[JsonSerializable(typeof(DeprecateVersionCliRequest))]
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
internal partial class CliJsonContext : JsonSerializerContext { }

/// <summary>
/// Request body sent to <c>POST /auth/login</c> and <c>POST /auth/register</c>
/// endpoints.
/// </summary>
internal sealed class LoginRequest
{
    /// <summary>The account username.</summary>
    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    /// <summary>The account password.</summary>
    [JsonPropertyName("password")]
    public string Password { get; set; } = "";
}

/// <summary>
/// Request body sent to <c>PUT /admin/packages/{scope}/{name}/roles</c> to assign a
/// role to a principal (user, team, or org).
/// </summary>
internal sealed class AssignRoleRequest
{
    /// <summary>The type of principal: <c>user</c>, <c>team</c>, or <c>org</c>.</summary>
    [JsonPropertyName("principal_type")]
    public required string PrincipalType { get; set; }

    /// <summary>The principal identifier — username, team ID, or org name.</summary>
    [JsonPropertyName("principal_id")]
    public required string PrincipalId { get; set; }

    /// <summary>The role to assign: <c>owner</c>, <c>maintainer</c>, <c>publisher</c>, or <c>reader</c>.</summary>
    [JsonPropertyName("role")]
    public required string Role { get; set; }
}

/// <summary>
/// Request body sent to <c>DELETE /admin/packages/{scope}/{name}/roles</c> to revoke a
/// principal's role on a package. Carries no <c>role</c> field — the revoke targets
/// whatever role the principal currently holds.
/// </summary>
internal sealed class RevokeRoleRequest
{
    /// <summary>The type of principal: <c>user</c>, <c>team</c>, or <c>org</c>.</summary>
    [JsonPropertyName("principal_type")]
    public required string PrincipalType { get; set; }

    /// <summary>The principal identifier — username, team ID, or org name.</summary>
    [JsonPropertyName("principal_id")]
    public required string PrincipalId { get; set; }
}

/// <summary>
/// Request body sent to <c>POST /auth/tokens/refresh</c> to exchange a refresh token
/// for a new access/refresh token pair.
/// </summary>
internal sealed class TokenRefreshRequest
{
    /// <summary>The refresh token string.</summary>
    [JsonPropertyName("refreshToken")]
    public string RefreshToken { get; set; } = "";

    /// <summary>The expired access token.</summary>
    [JsonPropertyName("accessToken")]
    public string AccessToken { get; set; } = "";

    /// <summary>The machine fingerprint hash.</summary>
    [JsonPropertyName("machineId")]
    public string MachineId { get; set; } = "";
}

/// <summary>
/// Request body sent to <c>PATCH /packages/{name}/deprecate</c>.
/// </summary>
internal sealed class DeprecatePackageCliRequest
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("alternative")]
    public string? Alternative { get; set; }
}

/// <summary>
/// Request body sent to <c>PATCH /packages/{name}/{version}/deprecate</c>.
/// </summary>
internal sealed class DeprecateVersionCliRequest
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

/// <summary>
/// Request body for <c>POST /auth/tokens</c> endpoint.
/// </summary>
internal sealed class TokenCreateRequest
{
    [JsonPropertyName("ceiling")]
    public string? Ceiling { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("expiresIn")]
    public string? ExpiresIn { get; set; }
}

/// <summary>
/// Response from <c>POST /auth/tokens</c> endpoint.
/// </summary>
public sealed class TokenCreateResult
{
    [JsonPropertyName("token")]
    public string Token { get; set; } = "";

    [JsonPropertyName("tokenId")]
    public string TokenId { get; set; } = "";

    [JsonPropertyName("scope")]
    public string Scope { get; set; } = "";

    [JsonPropertyName("expiresAt")]
    public DateTime ExpiresAt { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary>
/// Response from <c>GET /auth/tokens</c> endpoint.
/// </summary>
public sealed class TokenListResult
{
    [JsonPropertyName("tokens")]
    public List<TokenListItemResult> Tokens { get; set; } = [];
}

/// <summary>
/// A single token entry in the token list response.
/// </summary>
public sealed class TokenListItemResult
{
    [JsonPropertyName("tokenId")]
    public string TokenId { get; set; } = "";

    [JsonPropertyName("scope")]
    public string Scope { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// Holds the parsed response from a successful login, including both
/// access and refresh tokens with their metadata.
/// </summary>
public sealed class LoginResult
{
    /// <summary>The short-lived access token.</summary>
    public string Token { get; set; } = "";

    /// <summary>UTC expiry of the access token.</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>The long-lived refresh token.</summary>
    public string? RefreshToken { get; set; }

    /// <summary>UTC expiry of the refresh token.</summary>
    public DateTime? RefreshTokenExpiresAt { get; set; }

    /// <summary>The machine fingerprint used during login.</summary>
    public string? MachineId { get; set; }
}
