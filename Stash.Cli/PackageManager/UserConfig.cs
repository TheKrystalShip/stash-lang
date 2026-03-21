using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stash.Cli.PackageManager;

public sealed class UserConfig
{
    private static readonly string _configDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".stash");
    private static readonly string _configPath = Path.Combine(_configDir, "config.json");

    [JsonPropertyName("defaultRegistry")]
    public string? DefaultRegistry { get; set; }

    [JsonPropertyName("registries")]
    public Dictionary<string, RegistryEntry> Registries { get; set; } = new(StringComparer.Ordinal);

    public static UserConfig Load()
    {
        if (!File.Exists(_configPath))
        {
            return new UserConfig();
        }

        string json = File.ReadAllText(_configPath);
        var config = JsonSerializer.Deserialize(json, CliJsonContext.Default.UserConfig) ?? new UserConfig();
        config.CleanLegacyEntries();
        return config;
    }

    public void Save()
    {
        Directory.CreateDirectory(_configDir);
        string json = JsonSerializer.Serialize(this, CliJsonContext.Default.UserConfig);
        File.WriteAllText(_configPath, json);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(_configPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    public string? GetToken(string registryUrl)
    {
        if (Registries.TryGetValue(registryUrl, out var entry))
        {
            return entry.Token;
        }

        return null;
    }

    public void SetToken(string registryUrl, string token)
    {
        if (!Registries.TryGetValue(registryUrl, out var entry))
        {
            entry = new RegistryEntry();
            Registries[registryUrl] = entry;
        }
        entry.Token = token;
        Save();
    }

    public void RemoveToken(string registryUrl)
    {
        if (Registries.TryGetValue(registryUrl, out var entry))
        {
            entry.Token = null;
            if (entry.Url == null)
            {
                Registries.Remove(registryUrl);
            }
            Save();
        }
    }

    public List<string> GetRegistryUrls()
    {
        return Registries.Keys
            .Where(key => !string.Equals(key, "default", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private void CleanLegacyEntries()
    {
        if (Registries.TryGetValue("default", out var entry))
        {
            if (entry.Url == null && entry.Token == null)
            {
                Registries.Remove("default");
                Save();
            }
        }
    }
}

public sealed class RegistryEntry
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("token")]
    public string? Token { get; set; }
}
