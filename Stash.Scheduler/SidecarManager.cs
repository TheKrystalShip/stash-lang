namespace Stash.Scheduler;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Manages JSON sidecar metadata files for installed Stash services.
/// Stored at ~/.config/stash/services/{name}.json
/// </summary>
internal static class SidecarManager
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static string GetBaseDirectory()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".config", "stash", "services");
    }

    private static string GetSidecarPath(string serviceName) =>
        Path.Combine(GetBaseDirectory(), $"{serviceName}.json");

    /// <summary>Write sidecar atomically (write tmp, then rename).</summary>
    public static void Write(SidecarData data)
    {
        string dir = GetBaseDirectory();
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                File.SetUnixFileMode(dir,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        string path = GetSidecarPath(data.Name);
        string tmpPath = path + ".tmp";

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(data, _jsonOptions);
        File.WriteAllBytes(tmpPath, json);

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(tmpPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        File.Move(tmpPath, path, overwrite: true); // Atomic rename
    }

    /// <summary>Read a sidecar file. Returns null if not found or parse error.</summary>
    public static SidecarData? Read(string serviceName)
    {
        string path = GetSidecarPath(serviceName);
        if (!File.Exists(path)) return null;

        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            return JsonSerializer.Deserialize<SidecarData>(bytes, _jsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Delete a sidecar file.</summary>
    public static void Delete(string serviceName)
    {
        string path = GetSidecarPath(serviceName);
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>List all sidecar files (names only, without .json).</summary>
    public static IReadOnlyList<string> ListAll()
    {
        string dir = GetBaseDirectory();
        if (!Directory.Exists(dir)) return Array.Empty<string>();

        var names = new List<string>();
        foreach (string file in Directory.GetFiles(dir, "*.json"))
        {
            names.Add(Path.GetFileNameWithoutExtension(file));
        }
        return names;
    }
}

internal sealed class SidecarData
{
    public string Name { get; set; } = string.Empty;
    public string ScriptPath { get; set; } = string.Empty;
    public string? InstalledAt { get; set; }    // ISO 8601 UTC
    public string? InstalledBy { get; set; }    // username
    public string Mode { get; set; } = "user";  // "user" or "system"
    public string? Schedule { get; set; }
    public string? Description { get; set; }
    public string? StashVersion { get; set; }
    public Dictionary<string, string>? PlatformExtras { get; set; }
}
