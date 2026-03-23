using System.Text.Json.Serialization;

namespace Stash.Registry.Contracts;

/// <summary>
/// Standard error response body returned by all API endpoints on failure.
/// </summary>
public sealed class ErrorResponse
{
    /// <summary>A human-readable message describing what went wrong.</summary>
    [JsonPropertyName("error")]
    public required string Error { get; set; }
}

/// <summary>
/// Standard success response body used by endpoints that have no additional data to return.
/// </summary>
public sealed class SuccessResponse
{
    /// <summary>Indicates the operation succeeded. Always <c>true</c> for a 200 response.</summary>
    [JsonPropertyName("ok")]
    public bool Ok { get; set; } = true;
}

/// <summary>
/// Response body returned by the <c>GET /api/v1/health</c> endpoint.
/// </summary>
public sealed class HealthCheckResponse
{
    /// <summary>The current health status of the registry (e.g. <c>"ok"</c>).</summary>
    [JsonPropertyName("status")]
    public required string Status { get; set; }

    /// <summary>The version string of the running registry server.</summary>
    [JsonPropertyName("version")]
    public required string Version { get; set; }
}
