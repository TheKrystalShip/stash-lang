using System;
using System.Text.Json.Serialization;

namespace Stash.Registry.Contracts;

/// <summary>
/// Request body for the <c>POST /api/v1/auth/login</c> endpoint.
/// </summary>
public sealed class LoginRequest
{
    /// <summary>The username to authenticate with.</summary>
    [JsonPropertyName("username")]
    public string? Username { get; set; }

    /// <summary>The plaintext password for the account.</summary>
    [JsonPropertyName("password")]
    public string? Password { get; set; }
}

/// <summary>
/// Response body returned by the <c>POST /api/v1/auth/login</c> endpoint on success.
/// </summary>
public sealed class LoginResponse
{
    /// <summary>The bearer token to use in subsequent <c>Authorization</c> headers.</summary>
    [JsonPropertyName("token")]
    public required string Token { get; set; }

    /// <summary>The UTC date and time at which the token expires.</summary>
    [JsonPropertyName("expiresAt")]
    public required DateTime ExpiresAt { get; set; }
}

/// <summary>
/// Request body for the <c>POST /api/v1/auth/register</c> endpoint.
/// </summary>
public sealed class RegisterRequest
{
    /// <summary>The desired username for the new account.</summary>
    [JsonPropertyName("username")]
    public string? Username { get; set; }

    /// <summary>The plaintext password for the new account.</summary>
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

    /// <summary>The role assigned to the authenticated user (e.g. <c>"user"</c> or <c>"admin"</c>).</summary>
    [JsonPropertyName("role")]
    public required string Role { get; set; }
}

/// <summary>
/// Request body for the <c>POST /api/v1/auth/tokens</c> endpoint.
/// </summary>
public sealed class TokenCreateRequest
{
    /// <summary>The permission scope for the new token (e.g. <c>"publish"</c>, <c>"readonly"</c>).</summary>
    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    /// <summary>An optional human-readable description to identify the token's intended use.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }
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
    public required string Scope { get; set; }

    /// <summary>The UTC date and time at which this token expires.</summary>
    [JsonPropertyName("expiresAt")]
    public required DateTime ExpiresAt { get; set; }

    /// <summary>The optional human-readable description provided when the token was created.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
