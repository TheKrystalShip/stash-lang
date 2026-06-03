using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Stash.Registry.Contracts.Validation;

namespace Stash.Registry.Contracts;

/// <summary>
/// Request body for the <c>POST /api/v1/auth/login</c> endpoint.
/// </summary>
public sealed class LoginRequest
{
    /// <summary>The username to authenticate with.</summary>
    [Required]
    [JsonPropertyName("username")]
    public string? Username { get; set; }

    /// <summary>The plaintext password for the account.</summary>
    [Required]
    [JsonPropertyName("password")]
    public string? Password { get; set; }
}

/// <summary>
/// Response body returned by the <c>POST /api/v1/auth/login</c> endpoint on success.
/// </summary>
public sealed class LoginResponse
{
    /// <summary>The short-lived access token to use in subsequent <c>Authorization</c> headers.</summary>
    [JsonPropertyName("accessToken")]
    public required string AccessToken { get; set; }

    // DEPRECATED: read accessToken instead. Remove after next release.
    [JsonPropertyName("token")]
    public string Token => AccessToken;

    /// <summary>The UTC date and time at which the access token expires.</summary>
    [JsonPropertyName("expiresAt")]
    public required DateTime ExpiresAt { get; set; }

    /// <summary>Lifetime of the access token in seconds.</summary>
    [JsonPropertyName("expiresIn")]
    public int ExpiresIn { get; set; }

    /// <summary>The long-lived refresh token for obtaining new access tokens without re-authenticating.</summary>
    [JsonPropertyName("refreshToken")]
    public string? RefreshToken { get; set; }

    /// <summary>The UTC date and time at which the refresh token expires.</summary>
    [JsonPropertyName("refreshTokenExpiresAt")]
    public DateTime? RefreshTokenExpiresAt { get; set; }
}

/// <summary>
/// Request body for the <c>POST /api/v1/auth/register</c> endpoint.
/// </summary>
public sealed class RegisterRequest
{
    /// <summary>The desired username for the new account (scope grammar: 1–39 chars, <c>[a-z][a-z0-9-]*</c>).</summary>
    [Required]
    [ScopeGrammar]
    [JsonPropertyName("username")]
    public string? Username { get; set; }

    /// <summary>The plaintext password for the new account (minimum 8 characters).</summary>
    [Required]
    [StringLength(int.MaxValue, MinimumLength = 8)]
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "AOT publish (Stash.Cli PublishAot=true) is empirically clean with this " +
                        "suppression. StringLengthAttribute is [RequiresUnreferencedCode] for its " +
                        "ICollection.Count reflection path, which is a server-side validation concern; " +
                        "the CLI has zero calls to Validator.*, ValidateObject, or ValidateValue and " +
                        "never reaches that path at runtime.")]
    [JsonPropertyName("password")]
    public string? Password { get; set; }
}

/// <summary>
/// Response body returned by the <c>POST /api/v1/auth/register</c> endpoint on success.
/// </summary>
public sealed class RegisterResponse
{
    /// <summary>Indicates whether the registration succeeded. Always <c>true</c> for a 200 response.</summary>
    [JsonPropertyName("ok")]
    public bool Ok { get; set; } = true;

    /// <summary>The username of the newly created account.</summary>
    [JsonPropertyName("username")]
    public required string Username { get; set; }
}

/// <summary>
/// Response body returned by the <c>GET /api/v1/auth/whoami</c> endpoint.
/// </summary>
public sealed class WhoamiResponse
{
    /// <summary>The username of the currently authenticated user.</summary>
    [JsonPropertyName("username")]
    public required string Username { get; set; }

    /// <summary>The role assigned to the authenticated user.</summary>
    [JsonPropertyName("role")]
    public required UserRoles Role { get; set; }
}

/// <summary>
/// Request body for the <c>POST /api/v1/auth/tokens</c> endpoint.
/// </summary>
public sealed class TokenCreateRequest
{
    /// <summary>
    /// The coarse capability ceiling for the new token: <c>"read"</c>, <c>"publish"</c>, or <c>"admin"</c>.
    /// Mandatory — absent or unrecognised values are rejected 400.
    /// </summary>
    [Required]
    [JsonPropertyName("ceiling")]
    public string? Ceiling { get; set; }

    /// <summary>An optional human-readable name or description to identify the token's intended use.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>An optional human-readable description to identify the token's intended use.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Required token lifetime, e.g. "30d", "12h", "90m". Must be present; absent or invalid
    /// values are rejected 400. Must not exceed <c>Security.MaxTokenLifetime</c>.
    /// </summary>
    [Required]
    [TokenExpiry]
    [JsonPropertyName("expiresIn")]
    public string? ExpiresIn { get; set; }

    /// <summary>
    /// Fine-grained capability rules (deferred to a follow-up feature). If this field is
    /// present with any non-null value in the request body, <c>POST /auth/tokens</c> returns
    /// <c>400</c> to prevent the deferred shape from silently leaking in.
    /// </summary>
    [JsonPropertyName("capabilities")]
    public object? Capabilities { get; set; }
}

/// <summary>
/// Response body returned by the <c>POST /api/v1/auth/tokens</c> endpoint on success.
/// </summary>
public sealed class TokenCreateResponse
{
    /// <summary>The newly created bearer token value.</summary>
    [JsonPropertyName("token")]
    public required string Token { get; set; }

    /// <summary>The unique identifier for the newly created token, used to revoke it later.</summary>
    [JsonPropertyName("tokenId")]
    public required string TokenId { get; set; }

    /// <summary>The permission scope granted to this token.</summary>
    [JsonPropertyName("scope")]
    public required TokenScopes Scope { get; set; }

    /// <summary>The UTC date and time at which this token expires.</summary>
    [JsonPropertyName("expiresAt")]
    public required DateTime ExpiresAt { get; set; }

    /// <summary>The optional human-readable description provided when the token was created.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary>
/// A single token entry returned by the <c>GET /api/v1/auth/tokens</c> endpoint.
/// Does not include the token value itself.
/// </summary>
public sealed class TokenListItem
{
    [JsonPropertyName("tokenId")]
    public string TokenId { get; set; } = "";

    [JsonPropertyName("scope")]
    public TokenScopes Scope { get; set; } = TokenScopes.Publish;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// Response body returned by the <c>GET /api/v1/auth/tokens</c> endpoint.
/// </summary>
public sealed class TokenListResponse
{
    [JsonPropertyName("tokens")]
    public List<TokenListItem> Tokens { get; set; } = [];
}

/// <summary>
/// Request body for the <c>POST /api/v1/auth/tokens/refresh</c> endpoint.
/// </summary>
public sealed class RefreshTokenRequest
{
    /// <summary>The refresh token string issued during login or a previous refresh.</summary>
    [Required]
    [JsonPropertyName("refreshToken")]
    public string? RefreshToken { get; set; }

    /// <summary>The expired access token to be renewed.</summary>
    [Required]
    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; set; }

    /// <summary>The SHA-256 machine fingerprint of the requesting client.</summary>
    [Required]
    [JsonPropertyName("machineId")]
    public string? MachineId { get; set; }
}

/// <summary>
/// Response body returned by the <c>POST /api/v1/auth/tokens/refresh</c> endpoint on success.
/// </summary>
public sealed class RefreshTokenResponse
{
    /// <summary>The new short-lived access token.</summary>
    [JsonPropertyName("accessToken")]
    public required string AccessToken { get; set; }

    /// <summary>The new refresh token (rotation — the old one is invalidated).</summary>
    [JsonPropertyName("refreshToken")]
    public required string RefreshToken { get; set; }

    /// <summary>The UTC date and time at which the new access token expires.</summary>
    [JsonPropertyName("expiresAt")]
    public required DateTime ExpiresAt { get; set; }

    /// <summary>The UTC date and time at which the new refresh token expires.</summary>
    [JsonPropertyName("refreshTokenExpiresAt")]
    public DateTime? RefreshTokenExpiresAt { get; set; }
}
