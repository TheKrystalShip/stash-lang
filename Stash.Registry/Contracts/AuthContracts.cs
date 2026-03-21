using System;
using System.Text.Json.Serialization;

namespace Stash.Registry.Contracts;

public sealed class LoginRequest
{
    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("password")]
    public string? Password { get; set; }
}

public sealed class LoginResponse
{
    [JsonPropertyName("token")]
    public required string Token { get; set; }

    [JsonPropertyName("expiresAt")]
    public required DateTime ExpiresAt { get; set; }
}

public sealed class RegisterRequest
{
    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("password")]
    public string? Password { get; set; }
}

public sealed class RegisterResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; } = true;

    [JsonPropertyName("username")]
    public required string Username { get; set; }
}

public sealed class WhoamiResponse
{
    [JsonPropertyName("username")]
    public required string Username { get; set; }

    [JsonPropertyName("role")]
    public required string Role { get; set; }
}

public sealed class TokenCreateRequest
{
    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public sealed class TokenCreateResponse
{
    [JsonPropertyName("token")]
    public required string Token { get; set; }

    [JsonPropertyName("tokenId")]
    public required string TokenId { get; set; }

    [JsonPropertyName("scope")]
    public required string Scope { get; set; }

    [JsonPropertyName("expiresAt")]
    public required DateTime ExpiresAt { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
