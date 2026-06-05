using System.Text.Json.Serialization;

namespace Stash.Registry.Contracts;

// ── Discovery ─────────────────────────────────────────────────────────────────

/// <summary>
/// Paging limit values advertised by <see cref="DiscoveryLimits"/>.
/// </summary>
public sealed class DiscoveryLimits
{
    /// <summary>
    /// The maximum allowed tarball size in bytes for a single published package version.
    /// Sourced from <c>SecurityConfig.MaxPackageSizeBytes</c>.
    /// </summary>
    [JsonPropertyName("maxPackageSize")]
    public long MaxPackageSize { get; set; }

    /// <summary>
    /// The maximum number of items per page for the <c>/search</c> and <c>/versions</c>
    /// endpoints.  Sourced from <see cref="PagingLimits.MaxPageSize"/> — the same
    /// constant used by <c>[Range]</c> on <c>SearchQuery.pageSize</c> and
    /// <c>VersionsQuery.pageSize</c>.
    /// </summary>
    [JsonPropertyName("maxPageSize")]
    public int MaxPageSize { get; set; }
}

/// <summary>
/// Well-known URL links advertised by the discovery endpoint.
/// </summary>
public sealed class DiscoveryLinks
{
    /// <summary>The URL of the search endpoint (<c>/api/v1/search</c>).</summary>
    [JsonPropertyName("search")]
    public required string Search { get; set; }

    /// <summary>The URL of the packages endpoint (<c>/api/v1/packages</c>).</summary>
    [JsonPropertyName("packages")]
    public required string Packages { get; set; }

    /// <summary>The URL of the published OpenAPI document (<c>/openapi/v1.json</c>).</summary>
    [JsonPropertyName("openapi")]
    public required string OpenApi { get; set; }

    /// <summary>The URL of this discovery endpoint (<c>/api/v1/.well-known/registry</c>).</summary>
    [JsonPropertyName("wellKnown")]
    public required string WellKnown { get; set; }
}

/// <summary>
/// Feature flags advertised by the discovery endpoint.
/// Bucket-B features are pinned to <c>false</c> until their backing data/logic lands.
/// </summary>
public sealed class DiscoveryFeatures
{
    // ── Bucket-B (not yet implemented — pinned false) ─────────────────────

    /// <summary>Whether download metrics are available. Bucket-B — pinned <c>false</c>.</summary>
    [JsonPropertyName("metrics")]
    public bool Metrics { get; set; }

    /// <summary>Whether security advisory data is available. Bucket-B — pinned <c>false</c>.</summary>
    [JsonPropertyName("advisories")]
    public bool Advisories { get; set; }

    /// <summary>Whether provenance attestation is available. Bucket-B — pinned <c>false</c>.</summary>
    [JsonPropertyName("provenance")]
    public bool Provenance { get; set; }

    /// <summary>Whether package signature verification is available. Bucket-B — pinned <c>false</c>.</summary>
    [JsonPropertyName("signatures")]
    public bool Signatures { get; set; }

    /// <summary>Whether trusted-publishing (OIDC-based) is available. Bucket-B — pinned <c>false</c>.</summary>
    [JsonPropertyName("trustedPublishing")]
    public bool TrustedPublishing { get; set; }

    /// <summary>Whether publisher verification is available. Bucket-B — pinned <c>false</c>.</summary>
    [JsonPropertyName("verifiedPublishers")]
    public bool VerifiedPublishers { get; set; }

    // ── Bucket-A (implemented today — true) ──────────────────────────────

    /// <summary>Whether organization-scoped packages are supported. <c>true</c> — organizations exist today.</summary>
    [JsonPropertyName("organizations")]
    public bool Organizations { get; set; }

    /// <summary>Whether private packages are supported. <c>true</c> — private visibility exists today.</summary>
    [JsonPropertyName("privatePackages")]
    public bool PrivatePackages { get; set; }

    /// <summary>Whether CORS headers are enabled. Reflects <c>CorsConfig.Enabled</c> at runtime.</summary>
    [JsonPropertyName("cors")]
    public bool Cors { get; set; }

    // ── Bucket-A (implemented today — true) ──────────────────────────────

    /// <summary>Whether the audit log is available (export, verify, widened filters). <c>true</c> — shipped in <c>registry-audit-log-v2</c>.</summary>
    [JsonPropertyName("audit")]
    public bool Audit { get; set; }
}

/// <summary>
/// Response body returned by the <c>GET /api/v1/.well-known/registry</c> discovery endpoint.
/// </summary>
/// <remarks>
/// <para>
/// The endpoint is public (no authentication required) and is hosted as a minimal-API
/// <c>MapGet</c> — it does not live on a controller and carries no <c>[RegistryAuthorize]</c>.
/// Capability advertisement is static; the response does not touch the database.
/// </para>
/// <para>
/// The <see cref="DiscoveryFeatures.Advisories"/>, <see cref="DiscoveryFeatures.Provenance"/>,
/// <see cref="DiscoveryFeatures.Signatures"/>, <see cref="DiscoveryFeatures.TrustedPublishing"/>,
/// and <see cref="DiscoveryFeatures.VerifiedPublishers"/> flags are Bucket-B and are pinned
/// <c>false</c> until those features land.
/// </para>
/// </remarks>
public sealed class DiscoveryResponse
{
    /// <summary>The human-readable name of this registry instance.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>The API version string. Always <c>"v1"</c> for this endpoint.</summary>
    [JsonPropertyName("apiVersion")]
    public required string ApiVersion { get; set; }

    /// <summary>The base path for all API endpoints (e.g. <c>"/api/v1"</c>).</summary>
    [JsonPropertyName("basePath")]
    public required string BasePath { get; set; }

    /// <summary>Server-enforced paging and size limits.</summary>
    [JsonPropertyName("limits")]
    public required DiscoveryLimits Limits { get; set; }

    /// <summary>Well-known URLs for key registry endpoints.</summary>
    [JsonPropertyName("links")]
    public required DiscoveryLinks Links { get; set; }

    /// <summary>Feature-presence flags for capability detection.</summary>
    [JsonPropertyName("features")]
    public required DiscoveryFeatures Features { get; set; }
}

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
