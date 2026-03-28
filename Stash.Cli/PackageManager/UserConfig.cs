using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stash.Cli.PackageManager;

/// <summary>
/// Represents the user-level Stash configuration persisted at
/// <c>~/.stash/config.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// The configuration file uses the following JSON structure:
/// <code lang="json">
/// {
///   "defaultRegistry": "https://registry.stash-lang.dev",
///   "registries": {
///     "https://registry.stash-lang.dev": {
///       "token": "eyJ..."
///     }
///   }
/// }
/// </code>
/// </para>
/// <para>
/// On non-Windows platforms the config file is written with mode <c>0600</c>
/// (user read/write only) to protect stored authentication tokens.
/// </para>
/// <para>
/// Serialisation uses <see cref="CliJsonContext"/> for AOT-compatible JSON handling.
/// </para>
/// </remarks>
public sealed class UserConfig
{
    /// <summary>
    /// Absolute path to the <c>~/.stash</c> configuration directory.
    /// </summary>
    private static readonly string _configDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".stash");

    /// <summary>
    /// Absolute path to the configuration file (<c>~/.stash/config.json</c>).
    /// </summary>
    private static readonly string _configPath = Path.Combine(_configDir, "config.json");

    /// <summary>
    /// The built-in fallback registry URL used when no registry is specified via
    /// <c>--registry</c>, <c>STASH_REGISTRY_URL</c>, or <see cref="DefaultRegistry"/>.
    /// </summary>
    public const string DefaultRegistryUrl = "https://registry.stash-lang.dev";

    /// <summary>
    /// The URL of the registry used when no <c>--registry</c> flag is provided on
    /// the command line.
    /// </summary>
    [JsonPropertyName("defaultRegistry")]
    public string? DefaultRegistry { get; set; }

    /// <summary>
    /// A map of registry URL to its associated <see cref="RegistryEntry"/>, which
    /// stores the authentication token for that registry.
    /// </summary>
    [JsonPropertyName("registries")]
    public Dictionary<string, RegistryEntry> Registries { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Loads the user configuration from <c>~/.stash/config.json</c>, returning a
    /// default (empty) instance when the file does not exist.
    /// </summary>
    /// <returns>The loaded <see cref="UserConfig"/>, or a new default instance.</returns>
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

    /// <summary>
    /// Persists the current configuration to <c>~/.stash/config.json</c>,
    /// creating the <c>~/.stash</c> directory if it does not already exist.
    /// </summary>
    /// <remarks>
    /// On non-Windows systems the file permissions are set to <c>0600</c> after
    /// writing to protect stored authentication tokens.
    /// </remarks>
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

    /// <summary>
    /// Retrieves the authentication token stored for the specified registry URL.
    /// </summary>
    /// <param name="registryUrl">The registry URL whose token to look up.</param>
    /// <returns>
    /// The bearer token string, or <c>null</c> when no entry exists for
    /// <paramref name="registryUrl"/>.
    /// </returns>
    public string? GetToken(string registryUrl)
    {
        if (Registries.TryGetValue(registryUrl, out var entry))
        {
            return entry.Token;
        }

        return null;
    }

    /// <summary>
    /// Retrieves the full registry entry for the specified registry URL.
    /// </summary>
    public RegistryEntry? GetEntry(string registryUrl)
    {
        if (Registries.TryGetValue(registryUrl, out var entry))
        {
            return entry;
        }
        return null;
    }

    /// <summary>
    /// Resolves the token for the given registry URL, checking <c>STASH_TOKEN</c> env var first,
    /// then falling back to the config file.
    /// </summary>
    public static string? ResolveToken(string registryUrl)
    {
        string? envToken = Environment.GetEnvironmentVariable("STASH_TOKEN");
        if (!string.IsNullOrEmpty(envToken))
        {
            return envToken;
        }

        var config = Load();
        return config.GetToken(registryUrl);
    }

    /// <summary>
    /// Resolves the registry URL using the following priority:
    /// <list type="number">
    ///   <item><description><paramref name="providedUrl"/> (e.g. from <c>--registry</c> flag)</description></item>
    ///   <item><description><c>STASH_REGISTRY_URL</c> environment variable</description></item>
    ///   <item><description><see cref="DefaultRegistry"/> from <c>~/.stash/config.json</c></description></item>
    ///   <item><description><see cref="DefaultRegistryUrl"/> built-in constant</description></item>
    /// </list>
    /// </summary>
    public static string ResolveRegistryUrl(string? providedUrl)
    {
        if (!string.IsNullOrEmpty(providedUrl))
        {
            return providedUrl;
        }

        string? envUrl = Environment.GetEnvironmentVariable("STASH_REGISTRY_URL");
        if (!string.IsNullOrEmpty(envUrl))
        {
            return envUrl;
        }

        var config = Load();
        if (!string.IsNullOrEmpty(config.DefaultRegistry))
        {
            return config.DefaultRegistry;
        }

        return DefaultRegistryUrl;
    }

    /// <summary>
    /// Stores an access token and optional refresh token for the specified registry URL
    /// and immediately saves the configuration to disk.
    /// </summary>
    public void SetToken(string registryUrl, string token, DateTime? expiresAt = null,
        string? refreshToken = null, DateTime? refreshTokenExpiresAt = null, string? machineId = null)
    {
        if (!Registries.TryGetValue(registryUrl, out var entry))
        {
            entry = new RegistryEntry();
            Registries[registryUrl] = entry;
        }
        entry.Token = token;
        entry.ExpiresAt = expiresAt;
        entry.RefreshToken = refreshToken;
        entry.RefreshTokenExpiresAt = refreshTokenExpiresAt;
        entry.MachineId = machineId;
        Save();
    }

    /// <summary>
    /// Clears the authentication token for the specified registry URL and saves the
    /// configuration. The registry entry itself is removed when it contains no other
    /// data.
    /// </summary>
    /// <param name="registryUrl">The registry URL whose token should be removed.</param>
    public void RemoveToken(string registryUrl)
    {
        if (Registries.TryGetValue(registryUrl, out var entry))
        {
            entry.Token = null;
            entry.ExpiresAt = null;
            entry.RefreshToken = null;
            entry.RefreshTokenExpiresAt = null;
            entry.MachineId = null;
            if (entry.Url == null)
            {
                Registries.Remove(registryUrl);
            }
            Save();
        }
    }

    /// <summary>
    /// Returns the list of registry URLs stored in the configuration, excluding
    /// any legacy <c>"default"</c> key.
    /// </summary>
    /// <returns>A list of registry URL strings from <see cref="Registries"/>.</returns>
    public List<string> GetRegistryUrls()
    {
        return Registries.Keys
            .Where(key => !string.Equals(key, "default", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Removes stale legacy <c>"default"</c> registry entries that carry no useful
    /// data (no URL or token), saving the config if a cleanup was performed.
    /// </summary>
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

/// <summary>
/// Stores per-registry configuration, including an optional authentication token.
/// </summary>
/// <remarks>
/// Entries are keyed by registry URL in <see cref="UserConfig.Registries"/> and
/// serialised to <c>~/.stash/config.json</c>.
/// </remarks>
public sealed class RegistryEntry
{
    /// <summary>The canonical URL of the registry, stored for informational purposes.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>
    /// The short-lived access token used when communicating with this registry,
    /// or <c>null</c> when the user is not logged in.
    /// </summary>
    [JsonPropertyName("token")]
    public string? Token { get; set; }

    /// <summary>The UTC expiry time of the access token.</summary>
    [JsonPropertyName("expiresAt")]
    public DateTime? ExpiresAt { get; set; }

    /// <summary>The long-lived refresh token for obtaining new access tokens.</summary>
    [JsonPropertyName("refreshToken")]
    public string? RefreshToken { get; set; }

    /// <summary>The UTC expiry time of the refresh token.</summary>
    [JsonPropertyName("refreshTokenExpiresAt")]
    public DateTime? RefreshTokenExpiresAt { get; set; }

    /// <summary>The machine fingerprint hash that was used when the token was issued.</summary>
    [JsonPropertyName("machineId")]
    public string? MachineId { get; set; }
}
