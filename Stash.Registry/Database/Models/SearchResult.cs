using System.Collections.Generic;

namespace Stash.Registry.Database.Models;

public sealed class SearchResult
{
    public List<PackageRecord> Packages { get; set; } = new();
    public int TotalCount { get; set; }
}

public sealed class SearchResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
}
