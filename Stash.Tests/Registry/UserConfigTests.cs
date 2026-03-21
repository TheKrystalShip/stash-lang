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
}
