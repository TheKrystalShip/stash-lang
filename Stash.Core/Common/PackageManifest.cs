using System.Text.Json;
using System.Text.Json.Serialization;

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

    public static PackageManifest? Load(string directoryPath)
    {
        string manifestPath = Path.Combine(directoryPath, "stash.json");
        if (!File.Exists(manifestPath))
            return null;

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
}

[JsonSerializable(typeof(PackageManifest))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
internal partial class PackageManifestJsonContext : JsonSerializerContext
{
}
