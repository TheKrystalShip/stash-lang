using System.Collections.Generic;

namespace Stash.Registry.Database.Models;

/// <summary>
/// Encapsulates the result of a paginated package search query.
/// </summary>
/// <remarks>
/// Returned by <see cref="IRegistryDatabase.SearchPackagesAsync"/>. The
/// <see cref="Packages"/> list contains only the items for the requested page;
/// <see cref="TotalCount"/> reflects the full un-paginated match count and should
/// be used by callers to compute total page count.
/// </remarks>
public sealed class SearchResult
{
    /// <summary>The <see cref="PackageRecord"/> items for the current page.</summary>
    public List<PackageRecord> Packages { get; set; } = new();

    /// <summary>The total number of packages matching the query across all pages.</summary>
    public int TotalCount { get; set; }
}

/// <summary>
/// Encapsulates the result of a paginated query over any entity type.
/// </summary>
/// <typeparam name="T">The entity type contained in the result set.</typeparam>
/// <remarks>
/// Used by <see cref="IRegistryDatabase.GetAuditLogAsync"/> and similar methods that
/// need to return both a page of items and the total match count for pagination.
/// </remarks>
public sealed class SearchResult<T>
{
    /// <summary>The items for the current page.</summary>
    public List<T> Items { get; set; } = new();

    /// <summary>The total number of items matching the query across all pages.</summary>
    public int TotalCount { get; set; }
}
