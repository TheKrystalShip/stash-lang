using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Stash.Registry.Contracts;

/// <summary>
/// A time-window download count tuple returned by the metrics endpoints.
/// Contains pre-computed counts for four fixed rolling windows.
/// </summary>
/// <remarks>
/// The four fixed windows (<c>total</c>, <c>last24h</c>, <c>last7d</c>, <c>last30d</c>)
/// are fixed C# property names — not a parameter-side enum.  There is deliberately no
/// <c>?window=</c> discriminator; all four windows are always returned in one response
/// (per brief decision: bounded domain collapsed into fixed response fields).
/// </remarks>
public sealed class DownloadWindowCounts
{
    /// <summary>All-time download count.</summary>
    [JsonPropertyName("total")]
    public long Total { get; set; }

    /// <summary>Downloads in the rolling 24-hour window ending at response time.</summary>
    [JsonPropertyName("last24h")]
    public long Last24h { get; set; }

    /// <summary>Downloads in the rolling 7-day window ending at response time.</summary>
    [JsonPropertyName("last7d")]
    public long Last7d { get; set; }

    /// <summary>Downloads in the rolling 30-day window ending at response time.</summary>
    [JsonPropertyName("last30d")]
    public long Last30d { get; set; }
}

/// <summary>
/// Per-version download breakdown entry inside a <see cref="PackageMetricsResponse"/>.
/// </summary>
public sealed class VersionDownloadCounts
{
    /// <summary>The exact semantic version string this entry describes.</summary>
    [JsonPropertyName("version")]
    public required string Version { get; set; }

    /// <summary>All-time download count for this version.</summary>
    [JsonPropertyName("total")]
    public long Total { get; set; }

    /// <summary>Downloads for this version in the rolling 24-hour window.</summary>
    [JsonPropertyName("last24h")]
    public long Last24h { get; set; }

    /// <summary>Downloads for this version in the rolling 7-day window.</summary>
    [JsonPropertyName("last7d")]
    public long Last7d { get; set; }

    /// <summary>Downloads for this version in the rolling 30-day window.</summary>
    [JsonPropertyName("last30d")]
    public long Last30d { get; set; }
}

/// <summary>
/// Response body returned by <c>GET /api/v1/packages/{scope}/{name}/metrics</c>.
/// </summary>
/// <remarks>
/// Requires <c>[RegistryAuthorize(RegistryAction.ReadPackageMetadata)]</c> + <c>[PublicEndpoint]</c>.
/// Anonymous callers on private packages receive <c>404</c> (not <c>403</c>) from
/// the shared <c>RegistryAuthorizeFilter</c>.
/// </remarks>
public sealed class PackageMetricsResponse
{
    /// <summary>The fully-qualified package name (e.g. <c>@scope/name</c>).</summary>
    [JsonPropertyName("package")]
    public required string Package { get; set; }

    /// <summary>Aggregate download counts across all versions.</summary>
    [JsonPropertyName("downloads")]
    public required DownloadWindowCounts Downloads { get; set; }

    /// <summary>Per-version download counts, ordered by total downloads descending.</summary>
    [JsonPropertyName("perVersion")]
    public required List<VersionDownloadCounts> PerVersion { get; set; }

    /// <summary>UTC timestamp at which this response was generated.</summary>
    [JsonPropertyName("generatedAt")]
    public DateTime GeneratedAt { get; set; }
}

/// <summary>
/// Response body returned by <c>GET /api/v1/packages/{scope}/{name}/{version}/metrics</c>.
/// </summary>
/// <remarks>
/// Requires <c>[RegistryAuthorize(RegistryAction.ReadPackageVersion)]</c> + <c>[PublicEndpoint]</c>.
/// Anonymous callers on private packages receive <c>404</c> (not <c>403</c>) from
/// the shared <c>RegistryAuthorizeFilter</c>.
/// </remarks>
public sealed class VersionMetricsResponse
{
    /// <summary>The fully-qualified package name (e.g. <c>@scope/name</c>).</summary>
    [JsonPropertyName("package")]
    public required string Package { get; set; }

    /// <summary>The exact semantic version string this response describes.</summary>
    [JsonPropertyName("version")]
    public required string Version { get; set; }

    /// <summary>Download counts for this specific version across the four fixed windows.</summary>
    [JsonPropertyName("downloads")]
    public required DownloadWindowCounts Downloads { get; set; }

    /// <summary>UTC timestamp at which this response was generated.</summary>
    [JsonPropertyName("generatedAt")]
    public DateTime GeneratedAt { get; set; }
}

/// <summary>
/// A single entry in the top-packages-by-downloads list returned by
/// <c>GET /api/v1/admin/metrics/downloads</c>.
/// </summary>
public sealed class TopPackageDownloadsEntry
{
    /// <summary>The fully-qualified package name (e.g. <c>@scope/name</c>).</summary>
    [JsonPropertyName("package")]
    public required string Package { get; set; }

    /// <summary>Total download count over the requested <c>windowDays</c>.</summary>
    [JsonPropertyName("downloads")]
    public long Downloads { get; set; }

    /// <summary>The window length in days that was used to compute <see cref="Downloads"/>.</summary>
    [JsonPropertyName("windowDays")]
    public int WindowDays { get; set; }
}

/// <summary>
/// Query-string parameters for <c>GET /api/v1/admin/metrics/downloads</c>.
/// Bound via <c>[FromQuery]</c> — all fields are validated with <c>[Range]</c>
/// to reject out-of-range values rather than silently clamping them.
/// </summary>
public sealed class TopPackagesQuery
{
    /// <summary>The 1-based page index (minimum 1).</summary>
    [Range(1, int.MaxValue)]
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "AOT publish (Stash.Cli PublishAot=true) is empirically clean with this " +
                        "suppression. RangeAttribute is [RequiresUnreferencedCode] for its " +
                        "IComparable/type-conversion reflection paths, which are server-side concerns; " +
                        "the CLI never calls Validator.* or ValidateObject.")]
    [JsonPropertyName("page")]
    public int page { get; set; } = 1;

    /// <summary>The number of entries per page (1–100).</summary>
    [Range(1, 100)]
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "AOT publish (Stash.Cli PublishAot=true) is empirically clean with this " +
                        "suppression. RangeAttribute is [RequiresUnreferencedCode] for its " +
                        "IComparable/type-conversion reflection paths, which are server-side concerns; " +
                        "the CLI never calls Validator.* or ValidateObject.")]
    [JsonPropertyName("pageSize")]
    public int pageSize { get; set; } = 20;

    /// <summary>
    /// The rolling window length in days over which downloads are summed (1–30, default 7).
    /// </summary>
    /// <remarks>
    /// The brief documents three well-known points (1, 7, 30) but uses <c>[Range(1, 30)]</c>
    /// rather than an enum — the window is a continuous integer, and restricting to only
    /// three sentinel values would be overly restrictive for operator use.
    /// </remarks>
    [Range(1, 30)]
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "AOT publish (Stash.Cli PublishAot=true) is empirically clean with this " +
                        "suppression. RangeAttribute is [RequiresUnreferencedCode] for its " +
                        "IComparable/type-conversion reflection paths, which are server-side concerns; " +
                        "the CLI never calls Validator.* or ValidateObject.")]
    [JsonPropertyName("windowDays")]
    public int windowDays { get; set; } = 7;
}
