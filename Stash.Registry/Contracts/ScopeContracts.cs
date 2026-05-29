using System.Text.Json.Serialization;

namespace Stash.Registry.Contracts;

/// <summary>
/// Request body for the <c>POST /api/v1/scopes</c> endpoint.
/// </summary>
public sealed class ClaimScopeRequest
{
    /// <summary>The bare scope name to claim (without the leading <c>@</c>).</summary>
    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    /// <summary>The owner type: <c>user</c> or <c>org</c>.</summary>
    [JsonPropertyName("owner_type")]
    public string? OwnerType { get; set; }

    /// <summary>The owner identifier — a username for <c>user</c> scopes, an org name for <c>org</c> scopes.</summary>
    [JsonPropertyName("owner")]
    public string? Owner { get; set; }
}

/// <summary>
/// Response body returned by <c>GET /api/v1/scopes/{scope}</c> and <c>POST /api/v1/scopes</c>.
/// </summary>
public sealed class ScopeDetailResponse
{
    /// <summary>The bare scope name (without the leading <c>@</c>).</summary>
    [JsonPropertyName("scope")]
    public required string Scope { get; set; }

    /// <summary>The type of owner: <c>system</c>, <c>user</c>, or <c>org</c>.</summary>
    [JsonPropertyName("owner_type")]
    public required string OwnerType { get; set; }

    /// <summary>The owner identifier (username, org name, or <c>null</c> for system scopes).</summary>
    [JsonPropertyName("owner")]
    public string? Owner { get; set; }
}
