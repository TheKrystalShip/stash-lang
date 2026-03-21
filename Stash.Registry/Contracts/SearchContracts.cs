using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Stash.Registry.Contracts;

public sealed class PackageSummaryResponse
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("latest")]
    public string? Latest { get; set; }

    [JsonPropertyName("keywords")]
    public required List<string> Keywords { get; set; }

    [JsonPropertyName("updatedAt")]
    public required string UpdatedAt { get; set; }
}

public sealed class SearchResponse
{
    [JsonPropertyName("packages")]
    public required List<PackageSummaryResponse> Packages { get; set; }

    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }
}
