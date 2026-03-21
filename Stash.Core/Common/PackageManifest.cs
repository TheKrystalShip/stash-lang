using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Stash.Common;

public class PackageManifest
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("main")]
    public string? Main { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("dependencies")]
    public Dictionary<string, string>? Dependencies { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("license")]
    public string? License { get; set; }

    [JsonPropertyName("repository")]
    public string? Repository { get; set; }

    [JsonPropertyName("keywords")]
    public List<string>? Keywords { get; set; }

    [JsonPropertyName("stash")]
    public string? Stash { get; set; }

    [JsonPropertyName("files")]
    public List<string>? Files { get; set; }

    [JsonPropertyName("registries")]
    public Dictionary<string, string>? Registries { get; set; }

    [JsonPropertyName("private")]
    public bool? Private { get; set; }

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

    public static string GetEntryPoint(string packageDir)
    {
        var manifest = Load(packageDir);
        return manifest?.Main ?? "index.stash";
    }

    public static bool IsValidPackageName(string name)
    {
        if (name.Length > 64)
        {
            return false;
        }

        if (name.StartsWith('@'))
        {
            return Regex.IsMatch(name, @"^@[a-z][a-z0-9-]*/[a-z][a-z0-9-]*$");
        }

        return Regex.IsMatch(name, @"^[a-z][a-z0-9-]*$");
    }

    public List<string> Validate()
    {
        var errors = new List<string>();

        if (Name != null && !IsValidPackageName(Name))
        {
            errors.Add($"Invalid package name '{Name}'. Must match ^[a-z][a-z0-9-]*$ or ^@[a-z][a-z0-9-]*/[a-z][a-z0-9-]*$ and be at most 64 characters.");
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

        if (Private == true)
        {
            errors.Add("Package is marked as private.");
        }

        return errors;
    }

    private static bool IsValidVersionConstraint(string constraint)
    {
        if (constraint.StartsWith("git:", StringComparison.Ordinal))
        {
            return true;
        }

        return SemVerRange.TryParse(constraint, out _);
    }
}

[JsonSerializable(typeof(PackageManifest))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(List<string>))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
internal partial class PackageManifestJsonContext : JsonSerializerContext
{
}
