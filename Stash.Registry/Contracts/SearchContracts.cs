using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Stash.Registry.Contracts;

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
}

/// <summary>
/// Response body returned by the <c>GET /api/v1/packages?q={query}</c> search endpoint.
/// </summary>
public sealed class SearchResponse
{
    /// <summary>The list of packages matching the search query for the current page.</summary>
    [JsonPropertyName("packages")]
    public required List<PackageSummaryResponse> Packages { get; set; }

    /// <summary>The total number of packages that matched the query across all pages.</summary>
    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

    /// <summary>The 1-based page index of the current result set.</summary>
    [JsonPropertyName("page")]
    public int Page { get; set; }

    /// <summary>The number of results included per page.</summary>
    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    /// <summary>The total number of pages available given <see cref="TotalCount"/> and <see cref="PageSize"/>.</summary>
    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }
}
