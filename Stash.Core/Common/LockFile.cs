using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stash.Common;

/// <summary>
/// Represents a single resolved package entry recorded in the lock file.
/// </summary>
public class LockFileEntry
{
    /// <summary>
    /// Gets or sets the exact resolved version of the package.
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    /// <summary>
    /// Gets or sets the URL or registry path from which the package was downloaded.
    /// </summary>
    [JsonPropertyName("resolved")]
    public string? Resolved { get; set; }

    /// <summary>
    /// Gets or sets the integrity hash of the downloaded package archive, in the format <c>sha256-&lt;base64&gt;</c>.
    /// </summary>
    [JsonPropertyName("integrity")]
    public string? Integrity { get; set; }

    /// <summary>
    /// Gets or sets the map of the package's own transitive dependencies and their resolved version constraints.
    /// </summary>
    [JsonPropertyName("dependencies")]
    public Dictionary<string, string>? Dependencies { get; set; }
}

/// <summary>
/// Represents the <c>stash-lock.json</c> lock file that pins every resolved package to an exact
/// version, download URL, and integrity hash.
/// </summary>
/// <remarks>
/// <para>
/// The lock file is written by the package installer and consumed by all tooling that needs
/// deterministic, reproducible installs. It mirrors the shape of a <c>package-lock.json</c>
/// in the npm ecosystem.
/// </para>
/// <para>
/// Use <see cref="Load"/> to deserialise an existing file and <see cref="Save"/> to persist
/// the current in-memory state. Both methods operate on the <c>stash-lock.json</c> file
/// located in the supplied directory. Keys inside the serialised JSON are written in
/// lexicographic order to keep diffs minimal.
/// </para>
/// </remarks>
public class LockFile
{
    /// <summary>
    /// Gets or sets the schema version of the lock file format. Defaults to <c>1</c>.
    /// </summary>
    [JsonPropertyName("lockVersion")]
    public int LockVersion { get; set; } = 1;

    /// <summary>
    /// Gets or sets the Stash CLI version that last wrote this lock file.
    /// </summary>
    [JsonPropertyName("stash")]
    public string? Stash { get; set; }

    /// <summary>
    /// Gets or sets the map of package names to their resolved <see cref="LockFileEntry"/> records.
    /// </summary>
    [JsonPropertyName("resolved")]
    public Dictionary<string, LockFileEntry> Resolved { get; set; } = new();

    /// <summary>
    /// Loads and deserialises the <c>stash-lock.json</c> file found in <paramref name="directoryPath"/>.
    /// Returns <c>null</c> if the file does not exist.
    /// </summary>
    /// <param name="directoryPath">The directory expected to contain <c>stash-lock.json</c>.</param>
    /// <returns>The deserialised <see cref="LockFile"/>, or <c>null</c> if no lock file is present.</returns>
    public static LockFile? Load(string directoryPath)
    {
        string lockPath = Path.Combine(directoryPath, "stash-lock.json");
        if (!File.Exists(lockPath))
        {
            return null;
        }

        string json = File.ReadAllText(lockPath);
        try
        {
            return JsonSerializer.Deserialize(json, LockFileJsonContext.Default.LockFile);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Malformed stash-lock.json at '{lockPath}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Serialises the lock file to <c>stash-lock.json</c> in <paramref name="directoryPath"/>,
    /// writing all JSON object keys in lexicographic order and appending a trailing newline.
    /// </summary>
    /// <param name="directoryPath">The directory in which to write <c>stash-lock.json</c>.</param>
    public void Save(string directoryPath)
    {
        string lockPath = Path.Combine(directoryPath, "stash-lock.json");

        var sortedResolved = new SortedDictionary<string, LockFileEntry>(StringComparer.Ordinal);
        foreach (var (name, entry) in Resolved)
        {
            var sortedEntry = new LockFileEntry
            {
                Version = entry.Version,
                Resolved = entry.Resolved,
                Integrity = entry.Integrity,
                Dependencies = entry.Dependencies != null
                    ? new Dictionary<string, string>(
                        new SortedDictionary<string, string>(entry.Dependencies, StringComparer.Ordinal))
                    : null
            };
            sortedResolved[name] = sortedEntry;
        }

        var normalized = new LockFile
        {
            LockVersion = LockVersion,
            Stash = Stash,
            Resolved = new Dictionary<string, LockFileEntry>(sortedResolved)
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            IndentSize = 2,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        string json = JsonSerializer.Serialize(normalized, LockFileJsonContext.Default.LockFile);

        // Source-generated JsonSerializerContext ignores custom JsonSerializerOptions,
        // so we re-parse and re-write with Utf8JsonWriter for deterministic sorted output.
        using var doc = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, IndentSize = 2 }))
        {
            WriteDocumentSorted(writer, doc.RootElement);
        }

        // Utf8JsonWriter does not append a trailing newline; add one for
        // consistency with text editors and version control tooling.
        stream.WriteByte((byte)'\n');
        File.WriteAllBytes(lockPath, stream.ToArray());
    }

    /// <summary>
    /// Computes the SHA-256 integrity hash of the file at <paramref name="filePath"/>,
    /// returning a string in the format <c>sha256-&lt;base64&gt;</c>.
    /// </summary>
    /// <param name="filePath">The path to the file whose integrity hash should be computed.</param>
    /// <returns>The integrity string in the form <c>sha256-&lt;base64&gt;</c>.</returns>
    public static string ComputeIntegrity(string filePath)
    {
        byte[] bytes = File.ReadAllBytes(filePath);
        byte[] hash = SHA256.HashData(bytes);
        return $"sha256-{Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// Verifies that the SHA-256 integrity hash of the file at <paramref name="filePath"/> matches
    /// <paramref name="expectedIntegrity"/>.
    /// </summary>
    /// <param name="filePath">The path to the file to verify.</param>
    /// <param name="expectedIntegrity">The expected integrity string in the form <c>sha256-&lt;base64&gt;</c>.</param>
    /// <returns><c>true</c> if the computed hash matches the expected value; otherwise <c>false</c>.</returns>
    public static bool VerifyIntegrity(string filePath, string expectedIntegrity)
    {
        string actual = ComputeIntegrity(filePath);
        return string.Equals(actual, expectedIntegrity, StringComparison.Ordinal);
    }

    /// <summary>
    /// Recursively writes <paramref name="element"/> to <paramref name="writer"/>, sorting the
    /// properties of every JSON object in lexicographic order to produce deterministic output.
    /// </summary>
    /// <param name="writer">The <see cref="Utf8JsonWriter"/> to write to.</param>
    /// <param name="element">The <see cref="JsonElement"/> to serialise.</param>
    private static void WriteDocumentSorted(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                var properties = element.EnumerateObject()
                    .OrderBy(p => p.Name, StringComparer.Ordinal)
                    .ToList();
                foreach (var prop in properties)
                {
                    writer.WritePropertyName(prop.Name);
                    WriteDocumentSorted(writer, prop.Value);
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteDocumentSorted(writer, item);
                }

                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;

            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText());
                break;

            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;

            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;

            case JsonValueKind.Null:
            default:
                writer.WriteNullValue();
                break;
        }
    }
}

/// <summary>
/// Source-generation context that registers <see cref="LockFile"/> and related types for
/// AOT-compatible JSON serialisation.
/// </summary>
[JsonSerializable(typeof(LockFile))]
[JsonSerializable(typeof(LockFileEntry))]
[JsonSerializable(typeof(Dictionary<string, LockFileEntry>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class LockFileJsonContext : JsonSerializerContext
{
}
