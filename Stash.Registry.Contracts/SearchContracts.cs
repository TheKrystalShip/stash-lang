using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Stash.Registry.Contracts;

// ── Search sort order ─────────────────────────────────────────────────────────

/// <summary>
/// Vocabulary of valid <c>sort=</c> values for <c>GET /api/v1/search</c>.
/// </summary>
/// <remarks>
/// <para>
/// Members are named to match the wire spelling directly because
/// <c>[FromQuery]</c> enum binding uses case-insensitive member-name matching,
/// NOT <c>[JsonStringEnumMemberName]</c> (which only affects JSON body serialization).
/// </para>
/// <para>
/// An unknown <c>sort=</c> string (e.g. <c>sort=downloads</c>) is rejected by the model
/// binder and returns <c>400 InvalidRequest</c> via the existing
/// <c>InvalidModelStateResponseFactory</c> — no explicit switch/throw needed.
/// </para>
/// </remarks>
public enum PackageSortOrder
{
    /// <summary>Default: order by package name ascending (free-text relevance proxy).</summary>
    Relevance = 0,

    /// <summary>Order by package name ascending (<c>sort=Name</c>).</summary>
    Name,

    /// <summary>Order by most-recently updated descending (<c>sort=Updated</c>).</summary>
    Updated,

    /// <summary>Order by most-recently published (created) descending (<c>sort=Published</c>).</summary>
    Published,
}

// ── Search query ──────────────────────────────────────────────────────────────

/// <summary>
/// Query-string parameters for the <c>GET /api/v1/search</c> endpoint.
/// Bound via <c>[FromQuery]</c> — page and pageSize are validated via <c>[Range]</c>
/// to reject out-of-range values rather than silently clamping them.
/// </summary>
public sealed class SearchQuery
{
    /// <summary>The free-text search query string (bound from the <c>?q=</c> query-string parameter).</summary>
    [JsonPropertyName("q")]
    public string? q { get; set; }

    /// <summary>
    /// Optional keyword filter: exact match against one element of the package keywords array.
    /// Absent or empty means no keyword filter.
    /// </summary>
    [JsonPropertyName("keyword")]
    public string? keyword { get; set; }

    /// <summary>
    /// Optional SPDX license identifier filter: exact match against <c>PackageRecord.License</c>.
    /// Absent or empty means no license filter.
    /// </summary>
    [JsonPropertyName("license")]
    public string? license { get; set; }

    /// <summary>
    /// Optional deprecated filter: when <c>true</c>, only deprecated packages are returned;
    /// when <c>false</c>, only non-deprecated packages; when absent, both are included.
    /// </summary>
    [JsonPropertyName("deprecated")]
    public bool? deprecated { get; set; }

    /// <summary>
    /// Optional owner filter: exact match against a user principal with <c>Owner</c> role on the package.
    /// Absent or empty means no owner filter.
    /// </summary>
    [JsonPropertyName("owner")]
    public string? owner { get; set; }

    /// <summary>
    /// Sort order for search results. Defaults to <see cref="PackageSortOrder.Relevance"/>.
    /// An unknown value (e.g. <c>sort=downloads</c>) is rejected with <c>400 InvalidRequest</c>
    /// by the model binder — no Bucket-B sort values are accepted.
    /// </summary>
    [JsonPropertyName("sort")]
    public PackageSortOrder sort { get; set; } = PackageSortOrder.Relevance;

    /// <summary>The 1-based page index (minimum 1).</summary>
    [Range(1, int.MaxValue)]
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "AOT publish (Stash.Cli PublishAot=true) is empirically clean with this " +
                        "suppression. RangeAttribute is [RequiresUnreferencedCode] for its " +
                        "IComparable/type-conversion reflection paths, which are server-side concerns; " +
                        "the CLI never calls Validator.* or ValidateObject.")]
    [JsonPropertyName("page")]
    public int page { get; set; } = 1;

    /// <summary>The number of results per page (1–100).</summary>
    [Range(1, 100)]
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "AOT publish (Stash.Cli PublishAot=true) is empirically clean with this " +
                        "suppression. RangeAttribute is [RequiresUnreferencedCode] for its " +
                        "IComparable/type-conversion reflection paths, which are server-side concerns; " +
                        "the CLI never calls Validator.* or ValidateObject.")]
    [JsonPropertyName("pageSize")]
    public int pageSize { get; set; } = 20;
}

// ── Package summary response ──────────────────────────────────────────────────

/// <summary>
/// A lightweight package summary included in search result listings.
/// </summary>
public sealed class PackageSummaryResponse
{
    /// <summary>The fully-qualified package name (e.g. <c>"org/my-lib"</c>).</summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>A short human-readable description of the package.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>The version string tagged as the latest stable release.</summary>
    [JsonPropertyName("latest")]
    public string? Latest { get; set; }

    /// <summary>The list of searchable keywords associated with the package.</summary>
    [JsonPropertyName("keywords")]
    public required List<string> Keywords { get; set; }

    /// <summary>The ISO 8601 timestamp of when the package was last updated.</summary>
    [JsonPropertyName("updatedAt")]
    public required string UpdatedAt { get; set; }

    /// <summary>Whether the package has been deprecated.</summary>
    [JsonPropertyName("deprecated")]
    public bool Deprecated { get; set; }

    /// <summary>
    /// The SPDX license identifier for the package, or <c>null</c> if not specified.
    /// Copied from <c>PackageRecord.License</c>.
    /// </summary>
    [JsonPropertyName("license")]
    public string? License { get; set; }

    /// <summary>
    /// The number of user principals with the <c>Owner</c> role on this package.
    /// Derived from <c>PackageRoleEntry</c> rows with <c>PrincipalType=User</c> and <c>Role=Owner</c>.
    /// </summary>
    [JsonPropertyName("ownerCount")]
    public int OwnerCount { get; set; }
}

