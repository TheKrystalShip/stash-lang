using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Stash.Registry.Contracts;

/// <summary>
/// A generic pagination envelope returned by all paginated listing endpoints in the registry.
/// The collection key is always <c>"items"</c>, unifying the wire shape across
/// <c>/search</c>, <c>/admin/audit-log</c>, and <c>/packages/{scope}/{name}/versions</c>.
/// </summary>
/// <typeparam name="T">The element type for the items collection.</typeparam>
/// <remarks>
/// Per-endpoint page-size caps are enforced on the <b>query</b> DTOs (e.g.
/// <see cref="SearchQuery"/> <c>[Range(1,100)]</c>, <see cref="AuditLogQuery"/>
/// <c>[Range(1,200)]</c>) — the envelope itself does not carry or enforce caps.
/// </remarks>
public sealed class PagedResponse<T>
{
    /// <summary>The list of items on the current page.</summary>
    [JsonPropertyName("items")]
    public required List<T> Items { get; set; }

    /// <summary>The total number of items across all pages.</summary>
    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

    /// <summary>The 1-based page index of the current result set.</summary>
    [JsonPropertyName("page")]
    public int Page { get; set; }

    /// <summary>The number of items included per page.</summary>
    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    /// <summary>The total number of pages available given <see cref="TotalCount"/> and <see cref="PageSize"/>.</summary>
    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }
}
