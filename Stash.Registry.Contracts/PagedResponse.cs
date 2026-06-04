using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Stash.Registry.Contracts;

/// <summary>
/// Server-wide paging limit constants.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="MaxPageSize"/> is the single source of truth for the 100-item per-page cap
/// enforced by <c>SearchQuery.pageSize</c> (<c>[Range(1, PagingLimits.MaxPageSize)]</c>) and
/// <c>VersionsQuery.pageSize</c> (<c>[Range(1, PagingLimits.MaxPageSize)]</c>).  The
/// discovery endpoint at <c>GET /api/v1/.well-known/registry</c> reads this same constant
/// so the advertised limit cannot drift from the enforced limit at compile time.
/// </para>
/// <para>
/// Note: <c>AuditLogQuery.pageSize</c> uses a separate cap of 200 — do NOT fold it into
/// this constant.
/// </para>
/// </remarks>
public static class PagingLimits
{
    /// <summary>
    /// The maximum number of items per page for the <c>/search</c> and <c>/versions</c>
    /// endpoints.  Value: <c>100</c>.
    /// </summary>
    public const int MaxPageSize = 100;
}

/// <summary>
/// A generic pagination envelope returned by all paginated listing endpoints in the registry.
/// The collection key is always <c>"items"</c>, unifying the wire shape across
/// <c>/search</c>, <c>/admin/audit-log</c>, and <c>/packages/{scope}/{name}/versions</c>.
/// </summary>
/// <typeparam name="T">The element type for the items collection.</typeparam>
/// <remarks>
/// Per-endpoint page-size caps are enforced on the <b>query</b> DTOs (e.g.
/// <see cref="SearchQuery"/> <c>[Range(1, PagingLimits.MaxPageSize)]</c>, <see cref="AuditLogQuery"/>
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
