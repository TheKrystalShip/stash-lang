using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Stash.Registry.Contracts;

public sealed class PackageDetailResponse
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("owners")]
    public required List<string> Owners { get; set; }

    [JsonPropertyName("license")]
    public string? License { get; set; }

    [JsonPropertyName("repository")]
    public string? Repository { get; set; }

    [JsonPropertyName("keywords")]
    public required List<string> Keywords { get; set; }

    [JsonPropertyName("readme")]
    public string? Readme { get; set; }

    [JsonPropertyName("versions")]
    public required Dictionary<string, VersionDetailResponse> Versions { get; set; }

    [JsonPropertyName("latest")]
    public string? Latest { get; set; }

    [JsonPropertyName("createdAt")]
    public required string CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public required string UpdatedAt { get; set; }
}

public sealed class VersionDetailResponse
{
    [JsonPropertyName("version")]
    public required string Version { get; set; }

    [JsonPropertyName("stashVersion")]
    public string? StashVersion { get; set; }

    [JsonPropertyName("dependencies")]
    public required Dictionary<string, object> Dependencies { get; set; }

    [JsonPropertyName("integrity")]
    public required string Integrity { get; set; }

    [JsonPropertyName("publishedAt")]
    public required string PublishedAt { get; set; }

    [JsonPropertyName("publishedBy")]
    public required string PublishedBy { get; set; }
}

public sealed class PublishResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; } = true;

    [JsonPropertyName("package")]
    public required string Package { get; set; }

    [JsonPropertyName("version")]
    public required string Version { get; set; }

    [JsonPropertyName("integrity")]
    public required string Integrity { get; set; }
}

public sealed class UnpublishResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; } = true;

    [JsonPropertyName("package")]
    public required string Package { get; set; }

    [JsonPropertyName("version")]
    public required string Version { get; set; }
}
