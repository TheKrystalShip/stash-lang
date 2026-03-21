using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stash.Common;

public class LockFileEntry
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("resolved")]
    public string? Resolved { get; set; }

    [JsonPropertyName("integrity")]
    public string? Integrity { get; set; }

    [JsonPropertyName("dependencies")]
    public Dictionary<string, string>? Dependencies { get; set; }
}

public class LockFile
{
    [JsonPropertyName("lockVersion")]
    public int LockVersion { get; set; } = 1;

    [JsonPropertyName("stash")]
    public string? Stash { get; set; }

    [JsonPropertyName("resolved")]
    public Dictionary<string, LockFileEntry> Resolved { get; set; } = new();

    public static LockFile? Load(string directoryPath)
    {
        string lockPath = Path.Combine(directoryPath, "stash-lock.json");
        if (!File.Exists(lockPath))
            return null;

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

    public static string ComputeIntegrity(string filePath)
    {
        byte[] bytes = File.ReadAllBytes(filePath);
        byte[] hash = SHA256.HashData(bytes);
        return $"sha256-{Convert.ToBase64String(hash)}";
    }

    public static bool VerifyIntegrity(string filePath, string expectedIntegrity)
    {
        string actual = ComputeIntegrity(filePath);
        return string.Equals(actual, expectedIntegrity, StringComparison.Ordinal);
    }

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
                    WriteDocumentSorted(writer, item);
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

[JsonSerializable(typeof(LockFile))]
[JsonSerializable(typeof(LockFileEntry))]
[JsonSerializable(typeof(Dictionary<string, LockFileEntry>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class LockFileJsonContext : JsonSerializerContext
{
}
