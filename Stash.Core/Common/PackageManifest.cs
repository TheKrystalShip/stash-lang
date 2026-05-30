using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Stash.Common;

/// <summary>
/// Provides source-generated <see cref="System.Text.RegularExpressions.Regex"/> instances for
/// validating Stash package names.
/// </summary>
public static partial class PackagingRegexes
{
    /// <summary>
    /// Returns a compiled <see cref="System.Text.RegularExpressions.Regex"/> that matches a
    /// scoped package name of the form <c>@scope/name</c>, where each segment starts with a
    /// lowercase letter, contains only <c>[a-z0-9-]</c>, and is between 1 and 39 characters.
    /// </summary>
    /// <returns>A <see cref="System.Text.RegularExpressions.Regex"/> matching <c>^@[a-z][a-z0-9-]{0,38}/[a-z][a-z0-9-]{0,38}$</c>.</returns>
    [GeneratedRegex(@"^@[a-z][a-z0-9-]{0,38}/[a-z][a-z0-9-]{0,38}$", RegexOptions.Compiled)]
    public static partial Regex NamespacedPackageName();

    /// <summary>
    /// Returns a compiled <see cref="System.Text.RegularExpressions.Regex"/> that matches a
    /// single scope or segment name: a lowercase letter followed by up to 38 lowercase letters,
    /// digits, or hyphens (max 39 characters total).
    /// </summary>
    /// <returns>A <see cref="System.Text.RegularExpressions.Regex"/> matching <c>^[a-z][a-z0-9-]{0,38}$</c>.</returns>
    [GeneratedRegex(@"^[a-z][a-z0-9-]{0,38}$", RegexOptions.Compiled)]
    public static partial Regex ScopeSegment();
}

/// <summary>
/// Represents the contents of a <c>stash.json</c> package manifest file.
/// </summary>
/// <remarks>
/// <para>
/// Every Stash project or publishable package has a <c>stash.json</c> at its root. The manifest
/// describes the package identity (<see cref="Name"/>, <see cref="Version"/>), its entry point
/// (<see cref="Main"/>), its runtime dependencies (<see cref="Dependencies"/>), and optional
/// publishing metadata such as <see cref="Author"/>, <see cref="License"/>, and
/// <see cref="Keywords"/>.
/// </para>
/// <para>
/// Use <see cref="Load"/> to deserialise a manifest from disk. Call <see cref="Validate"/> for
/// general well-formedness checks or <see cref="ValidateForPublishing"/> when the manifest must
/// satisfy the stricter requirements of a registry publish.
/// </para>
/// </remarks>
public class PackageManifest
{
    /// <summary>
    /// Gets or sets the package name. Must be a scoped name of the form <c>@scope/my-pkg</c>,
    /// where both segments start with a lowercase letter, contain only <c>[a-z0-9-]</c>,
    /// and are between 1 and 39 characters each.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the package version as a SemVer string, e.g. <c>1.2.3</c>.
    /// </summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    /// <summary>
    /// Gets or sets the relative path to the package entry point file. Defaults to
    /// <c>index.stash</c> when absent.
    /// </summary>
    [JsonPropertyName("main")]
    public string? Main { get; set; }

    /// <summary>
    /// Gets or sets a human-readable description of the package.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the map of package names to their version constraints.
    /// </summary>
    [JsonPropertyName("dependencies")]
    public Dictionary<string, string>? Dependencies { get; set; }

    /// <summary>
    /// Gets or sets the name or contact information of the package author.
    /// </summary>
    [JsonPropertyName("author")]
    public string? Author { get; set; }

    /// <summary>
    /// Gets or sets the SPDX license identifier for the package, e.g. <c>MIT</c>.
    /// </summary>
    [JsonPropertyName("license")]
    public string? License { get; set; }

    /// <summary>
    /// Gets or sets the URL of the source repository for the package.
    /// </summary>
    [JsonPropertyName("repository")]
    public string? Repository { get; set; }

    /// <summary>
    /// Gets or sets the list of search keywords associated with the package.
    /// </summary>
    [JsonPropertyName("keywords")]
    public List<string>? Keywords { get; set; }

    /// <summary>
    /// Gets or sets the Stash CLI version that last modified this manifest.
    /// </summary>
    [JsonPropertyName("stash")]
    public string? Stash { get; set; }

    /// <summary>
    /// Gets or sets the list of file or glob patterns that should be included when the package
    /// is published to a registry.
    /// </summary>
    [JsonPropertyName("files")]
    public List<string>? Files { get; set; }

    /// <summary>
    /// Gets or sets additional registry aliases mapped to their base URLs, supplementing the
    /// default registry configuration.
    /// </summary>
    [JsonPropertyName("registries")]
    public Dictionary<string, string>? Registries { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the package is private and should not be
    /// published to a public registry.
    /// </summary>
    [JsonPropertyName("private")]
    public bool? Private { get; set; }

    /// <summary>
    /// Loads and deserialises the <c>stash.json</c> manifest found in <paramref name="directoryPath"/>.
    /// Returns <c>null</c> if the file does not exist.
    /// </summary>
    /// <param name="directoryPath">The directory expected to contain <c>stash.json</c>.</param>
    /// <returns>The deserialised <see cref="PackageManifest"/>, or <c>null</c> if no manifest is present.</returns>
    public static PackageManifest? Load(string directoryPath)
    {
        string manifestPath = Path.Combine(directoryPath, "stash.json");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        string json = File.ReadAllText(manifestPath);
        try
        {
            return JsonSerializer.Deserialize(json, PackageManifestJsonContext.Default.PackageManifest);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Malformed stash.json at '{manifestPath}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Gets the entry-point file path for the package located in <paramref name="packageDir"/>,
    /// falling back to <c>index.stash</c> when no <see cref="Main"/> is specified.
    /// </summary>
    /// <returns>The relative path to the package entry point, e.g. <c>index.stash</c>.</returns>
    public static string GetEntryPoint(string packageDir)
    {
        var manifest = Load(packageDir);
        return manifest?.Main ?? "index.stash";
    }

    /// <summary>
    /// Validates whether <paramref name="name"/> is a legal Stash package name.
    /// Only scoped names of the form <c>@scope/name</c> are accepted, where each segment starts
    /// with a lowercase letter, contains only <c>[a-z0-9-]</c>, and is between 1 and 39
    /// characters. The combined name must be at most 64 characters.
    /// </summary>
    /// <param name="name">The package name string to validate.</param>
    /// <returns><c>true</c> if <paramref name="name"/> is a valid scoped package name; otherwise <c>false</c>.</returns>
    public static bool IsValidPackageName(string name)
    {
        if (name.Length > 64)
        {
            return false;
        }

        return PackagingRegexes.NamespacedPackageName().IsMatch(name);
    }

    /// <summary>
    /// Validates whether <paramref name="name"/> is a legal Stash scope or segment name.
    /// A valid scope name starts with a lowercase letter, contains only lowercase letters,
    /// digits, or hyphens, and is between 1 and 39 characters. This grammar applies to
    /// usernames, organization names, and any explicitly claimed scope.
    /// </summary>
    /// <param name="name">The scope name string to validate.</param>
    /// <returns><c>true</c> if <paramref name="name"/> is a valid scope name; otherwise <c>false</c>.</returns>
    public static bool IsValidScopeName(string name)
    {
        return name is not null && PackagingRegexes.ScopeSegment().IsMatch(name);
    }

    /// <summary>
    /// Validates the manifest for general well-formedness, checking that the package name,
    /// version, and dependency constraints are all syntactically correct.
    /// </summary>
    /// <returns>
    /// A list of human-readable error messages. An empty list indicates the manifest is valid.
    /// </returns>
    public List<string> Validate()
    {
        var errors = new List<string>();

        if (Name != null && !IsValidPackageName(Name))
        {
            errors.Add($"Invalid package name '{Name}'. Must match ^@[a-z][a-z0-9-]{{0,38}}/[a-z][a-z0-9-]{{0,38}}$ and be at most 64 characters.");
        }

        if (Version != null && !SemVer.TryParse(Version, out _))
        {
            errors.Add($"Invalid version '{Version}'. Must be a valid SemVer string.");
        }

        if (Dependencies != null)
        {
            foreach (var (dep, constraint) in Dependencies)
            {
                if (!IsValidVersionConstraint(constraint))
                {
                    errors.Add($"Invalid version constraint '{constraint}' for dependency '{dep}'.");
                }
            }
        }

        return errors;
    }

    /// <summary>
    /// Validates the manifest against the stricter requirements needed before publishing to a
    /// registry, including the presence of <see cref="Name"/> and <see cref="Version"/>.
    /// </summary>
    /// <returns>
    /// A list of human-readable error messages. An empty list indicates the manifest is ready
    /// to publish.
    /// </returns>
    public List<string> ValidateForPublishing()
    {
        var errors = Validate();

        if (string.IsNullOrWhiteSpace(Name))
        {
            errors.Add("Package name is required for publishing.");
        }
        else if (!IsValidPackageName(Name))
        {
            // already reported by Validate()
        }

        if (string.IsNullOrWhiteSpace(Version))
        {
            errors.Add("Package version is required for publishing.");
        }
        else if (!SemVer.TryParse(Version, out _))
        {
            // already reported by Validate()
        }

        return errors;
    }

    /// <summary>
    /// Splits a valid scoped package name of the form <c>@scope/name</c> into its
    /// bare scope and local name components.
    /// </summary>
    /// <param name="name">
    /// A valid scoped package name, e.g. <c>@alice/widget</c>. Must satisfy
    /// <see cref="IsValidPackageName"/>.
    /// </param>
    /// <returns>
    /// A tuple of (<c>Scope</c>, <c>LocalName</c>) where <c>Scope</c> is the bare
    /// scope string without the leading <c>@</c> (e.g. <c>alice</c>) and
    /// <c>LocalName</c> is the name segment (e.g. <c>widget</c>).
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="name"/> is not a valid scoped package name.
    /// </exception>
    public static (string Scope, string LocalName) SplitScopedName(string name)
    {
        if (!IsValidPackageName(name))
        {
            throw new ArgumentException(
                $"'{name}' is not a valid scoped package name. Expected @scope/name format.",
                nameof(name));
        }

        // Strip the leading '@', then split on the first '/'
        ReadOnlySpan<char> rest = name.AsSpan(1); // skip '@'
        int slash = rest.IndexOf('/');
        return (rest[..slash].ToString(), rest[(slash + 1)..].ToString());
    }

    /// <summary>
    /// Validates whether <paramref name="constraint"/> is a legal version constraint: either a
    /// <c>git:</c> URI or a value parseable as a <see cref="SemVerRange"/>.
    /// </summary>
    /// <param name="constraint">The version constraint string to validate.</param>
    /// <returns><c>true</c> if the constraint is valid; otherwise <c>false</c>.</returns>
    private static bool IsValidVersionConstraint(string constraint)
    {
        if (constraint.StartsWith("git:", StringComparison.Ordinal))
        {
            return true;
        }

        return SemVerRange.TryParse(constraint, out _);
    }
}

/// <summary>
/// Source-generation context that registers <see cref="PackageManifest"/> and related types for
/// AOT-compatible JSON serialisation.
/// </summary>
[JsonSerializable(typeof(PackageManifest))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(List<string>))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
public partial class PackageManifestJsonContext : JsonSerializerContext
{
}
