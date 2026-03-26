using System;
using System.IO;
using System.Text.Json;
using Stash.Cli.PackageManager;
using Xunit;

namespace Stash.Tests.Registry;

public sealed class UserConfigTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configPath;

    public UserConfigTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"stash-userconfig-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _configPath = Path.Combine(_tempDir, "config.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private UserConfig LoadFromDisk()
    {
        if (!File.Exists(_configPath))
        {
            return new UserConfig();
        }

        string json = File.ReadAllText(_configPath);
        return JsonSerializer.Deserialize<UserConfig>(json) ?? new UserConfig();
    }

    private void SaveToDisk(UserConfig config)
    {
        string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_configPath, json);
    }

    [Fact]
    public void GetToken_NoConfig_ReturnsNull()
    {
        var config = new UserConfig();

        string? token = config.GetToken("default");

        Assert.Null(token);
    }

    [Fact]
    public void SetToken_GetToken_RoundTrips()
    {
        var config = new UserConfig();
        config.Registries["default"] = new RegistryEntry { Token = "tok-abc-123" };
        SaveToDisk(config);

        UserConfig loaded = LoadFromDisk();

        Assert.Equal("tok-abc-123", loaded.GetToken("default"));
    }

    [Fact]
    public void RemoveToken_RemovesToken()
    {
        var config = new UserConfig();
        config.Registries["default"] = new RegistryEntry { Token = "tok-xyz" };

        config.RemoveToken("default");

        Assert.Null(config.GetToken("default"));
    }

    [Fact]
    public void GetRegistryUrls_EmptyConfig_ReturnsEmpty()
    {
        var config = new UserConfig();

        var urls = config.GetRegistryUrls();

        Assert.Empty(urls);
    }

    [Fact]
    public void GetRegistryUrls_WithEntries_ReturnsThem()
    {
        var config = new UserConfig();
        config.Registries["http://localhost:8080/api/v1"] = new RegistryEntry { Token = "tok1" };
        config.Registries["https://registry.stash-lang.org/api/v1"] = new RegistryEntry { Token = "tok2" };

        var urls = config.GetRegistryUrls();

        Assert.Equal(2, urls.Count);
        Assert.Contains("http://localhost:8080/api/v1", urls);
        Assert.Contains("https://registry.stash-lang.org/api/v1", urls);
    }

    [Fact]
    public void GetRegistryUrls_SkipsLegacyDefaultKey()
    {
        var config = new UserConfig();
        config.Registries["default"] = new RegistryEntry { Token = "old-tok" };
        config.Registries["http://localhost:8080/api/v1"] = new RegistryEntry { Token = "tok1" };

        var urls = config.GetRegistryUrls();

        Assert.DoesNotContain("default", urls);
        Assert.Single(urls);
    }

    [Fact]
    public void DefaultRegistry_NullByDefault()
    {
        var config = new UserConfig();

        Assert.Null(config.DefaultRegistry);
    }

    [Fact]
    public void DefaultRegistry_PersistsAcrossSaveLoad()
    {
        var config = new UserConfig();
        config.DefaultRegistry = "https://example.com/api/v1";
        SaveToDisk(config);

        UserConfig loaded = LoadFromDisk();

        Assert.Equal("https://example.com/api/v1", loaded.DefaultRegistry);
    }

    [Fact]
    public void DefaultRegistry_ClearedWhenSetToNull()
    {
        var config = new UserConfig();
        config.DefaultRegistry = "https://example.com/api/v1";
        SaveToDisk(config);

        UserConfig loaded = LoadFromDisk();
        loaded.DefaultRegistry = null;
        SaveToDisk(loaded);

        UserConfig reloaded = LoadFromDisk();

        Assert.Null(reloaded.DefaultRegistry);
    }

    // ── ResolveRegistryUrl ───────────────────────────────────────────────

    [Fact]
    public void ResolveRegistryUrl_WithProvidedUrl_ReturnsProvidedUrl()
    {
        string result = UserConfig.ResolveRegistryUrl("https://my-registry.example.com");

        Assert.Equal("https://my-registry.example.com", result);
    }

    [Fact]
    public void ResolveRegistryUrl_WithEnvVar_ReturnsEnvUrl()
    {
        string? original = Environment.GetEnvironmentVariable("STASH_REGISTRY_URL");
        try
        {
            Environment.SetEnvironmentVariable("STASH_REGISTRY_URL", "https://ci.example.com");

            string result = UserConfig.ResolveRegistryUrl(null);

            Assert.Equal("https://ci.example.com", result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("STASH_REGISTRY_URL", original);
        }
    }

    [Fact]
    public void ResolveRegistryUrl_WithNoInput_ReturnsDefault()
    {
        string? original = Environment.GetEnvironmentVariable("STASH_REGISTRY_URL");
        try
        {
            Environment.SetEnvironmentVariable("STASH_REGISTRY_URL", null);

            string result = UserConfig.ResolveRegistryUrl(null);

            // Falls back to config DefaultRegistry (if set) or the built-in constant.
            // In a clean environment with no DefaultRegistry configured, this must be
            // the built-in default URL.
            Assert.False(string.IsNullOrEmpty(result));
        }
        finally
        {
            Environment.SetEnvironmentVariable("STASH_REGISTRY_URL", original);
        }
    }

    // ── ResolveToken ─────────────────────────────────────────────────────

    [Fact]
    public void ResolveToken_WithEnvVar_ReturnsEnvToken()
    {
        string? original = Environment.GetEnvironmentVariable("STASH_TOKEN");
        try
        {
            Environment.SetEnvironmentVariable("STASH_TOKEN", "ci-token-xyz");

            string? result = UserConfig.ResolveToken(UserConfig.DefaultRegistryUrl);

            Assert.Equal("ci-token-xyz", result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("STASH_TOKEN", original);
        }
    }

    [Fact]
    public void ResolveToken_WithoutEnvVar_FallsBackToConfig()
    {
        // Use a unique URL to avoid colliding with any real config entries.
        string fakeUrl = $"https://test-registry-{Guid.NewGuid():N}.example.com";
        const string testToken = "config-token-123";
        string? originalEnv = Environment.GetEnvironmentVariable("STASH_TOKEN");
        try
        {
            Environment.SetEnvironmentVariable("STASH_TOKEN", null);

            // Write a known token for the fake URL into the real config on disk.
            var setup = UserConfig.Load();
            setup.Registries[fakeUrl] = new RegistryEntry { Token = testToken };
            setup.Save();

            string? result = UserConfig.ResolveToken(fakeUrl);

            Assert.Equal(testToken, result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("STASH_TOKEN", originalEnv);
            // Remove the test entry so we don't pollute the developer's config.
            var cleanup = UserConfig.Load();
            cleanup.Registries.Remove(fakeUrl);
            cleanup.Save();
        }
    }

    [Fact]
    public void ResolveToken_WithoutAny_ReturnsNull()
    {
        string? original = Environment.GetEnvironmentVariable("STASH_TOKEN");
        try
        {
            Environment.SetEnvironmentVariable("STASH_TOKEN", null);

            // A URL that will never appear in any real config.
            string? result = UserConfig.ResolveToken("https://nonexistent-registry-00000000.example.com");

            Assert.Null(result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("STASH_TOKEN", original);
        }
    }
}
