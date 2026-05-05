using System.Text.Json.Serialization;

namespace Stash.Registry.Contracts;

/// <summary>
/// Standard error response body returned by all API endpoints on failure.
/// </summary>
public sealed class ErrorResponse
{
    /// <summary>A machine-readable error code or human-readable message describing what went wrong.</summary>
    [JsonPropertyName("error")]
    public required string Error { get; set; }

    /// <summary>An optional human-readable description of the error. Omitted when null.</summary>
    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }
}

/// <summary>
/// Error response body returned when a publish request conflicts with an existing version (HTTP 409).
/// </summary>
public sealed class VersionConflictResponse
{
    /// <summary>Machine-readable error code. Always <c>"version_exists"</c>.</summary>
    [JsonPropertyName("error")]
    public string Error { get; set; } = "version_exists";

    /// <summary>Human-readable description of the conflict.</summary>
    [JsonPropertyName("message")]
    public required string Message { get; set; }
}

/// <summary>
/// Error response body returned when a publish request is rejected because the
/// manifest declares <c>"private": true</c> (HTTP 403).
/// </summary>
public sealed class PrivatePackageResponse
{
    /// <summary>Machine-readable error code. Always <c>"private_package"</c>.</summary>
    [JsonPropertyName("error")]
    public string Error { get; set; } = "private_package";

    /// <summary>Human-readable description of the rejection.</summary>
    [JsonPropertyName("message")]
    public required string Message { get; set; }
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
