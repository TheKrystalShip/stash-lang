using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Stash.Registry.Contracts;

/// <summary>
/// Response body returned by the <c>GET /api/v1/packages/{name}</c> endpoint.
/// </summary>
public sealed class PackageDetailResponse
{
    /// <summary>The fully-qualified package name (e.g. <c>"org/my-lib"</c>).</summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>A short human-readable description of the package.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>The list of usernames that own and can publish this package.</summary>
    [JsonPropertyName("owners")]
    public required List<string> Owners { get; set; }

    /// <summary>The SPDX license identifier for the package (e.g. <c>"MIT"</c>).</summary>
    [JsonPropertyName("license")]
    public string? License { get; set; }

    /// <summary>The URL of the package's source repository.</summary>
    [JsonPropertyName("repository")]
    public string? Repository { get; set; }

    /// <summary>The list of searchable keywords associated with the package.</summary>
    [JsonPropertyName("keywords")]
    public required List<string> Keywords { get; set; }

    /// <summary>The README content for the package in Markdown format.</summary>
    [JsonPropertyName("readme")]
    public string? Readme { get; set; }

    /// <summary>A dictionary mapping version strings to their detail objects.</summary>
    [JsonPropertyName("versions")]
    public required Dictionary<string, VersionDetailResponse> Versions { get; set; }

    /// <summary>The version string tagged as the latest stable release.</summary>
    [JsonPropertyName("latest")]
    public string? Latest { get; set; }

    /// <summary>The ISO 8601 timestamp of when the package was first published.</summary>
    [JsonPropertyName("createdAt")]
    public required string CreatedAt { get; set; }

    /// <summary>The ISO 8601 timestamp of when the package was last updated.</summary>
    [JsonPropertyName("updatedAt")]
    public required string UpdatedAt { get; set; }
}

/// <summary>
/// Detailed metadata for a single version of a package, embedded in <see cref="PackageDetailResponse"/>.
/// </summary>
public sealed class VersionDetailResponse
{
    /// <summary>The semantic version string for this release (e.g. <c>"1.2.3"</c>).</summary>
    [JsonPropertyName("version")]
    public required string Version { get; set; }

    /// <summary>The minimum Stash CLI version required to use this package version.</summary>
    [JsonPropertyName("stashVersion")]
    public string? StashVersion { get; set; }

    /// <summary>A dictionary of package dependencies and their version constraints.</summary>
    [JsonPropertyName("dependencies")]
    public required Dictionary<string, object> Dependencies { get; set; }

    /// <summary>The integrity hash of the package tarball (e.g. <c>"sha256-..."</c>).</summary>
    [JsonPropertyName("integrity")]
    public required string Integrity { get; set; }

    /// <summary>The ISO 8601 timestamp of when this version was published.</summary>
    [JsonPropertyName("publishedAt")]
    public required string PublishedAt { get; set; }

    /// <summary>The username of the user who published this version.</summary>
    [JsonPropertyName("publishedBy")]
    public required string PublishedBy { get; set; }
}

/// <summary>
/// Response body returned by the <c>PUT /api/v1/packages/{name}/{version}</c> publish endpoint on success.
/// </summary>
public sealed class PublishResponse
{
    /// <summary>Indicates whether the publish operation succeeded. Always <c>true</c> for a 200 response.</summary>
    [JsonPropertyName("ok")]
    public bool Ok { get; set; } = true;

    /// <summary>The name of the published package.</summary>
    [JsonPropertyName("package")]
    public required string Package { get; set; }

    /// <summary>The version string that was published.</summary>
    [JsonPropertyName("version")]
    public required string Version { get; set; }

    /// <summary>The integrity hash of the published package tarball.</summary>
    [JsonPropertyName("integrity")]
    public required string Integrity { get; set; }
}

/// <summary>
/// Response body returned by the <c>DELETE /api/v1/packages/{name}/{version}</c> unpublish endpoint on success.
/// </summary>
public sealed class UnpublishResponse
{
    /// <summary>Indicates whether the unpublish operation succeeded. Always <c>true</c> for a 200 response.</summary>
    [JsonPropertyName("ok")]
    public bool Ok { get; set; } = true;

    /// <summary>The name of the unpublished package.</summary>
    [JsonPropertyName("package")]
    public required string Package { get; set; }

    /// <summary>The version string that was unpublished.</summary>
    [JsonPropertyName("version")]
    public required string Version { get; set; }
}
