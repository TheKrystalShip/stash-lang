using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Stash.Registry.Configuration;
using Xunit;

namespace Stash.Tests.Registry;

/// <summary>
/// Tests for <see cref="CorsConfig"/> parsing and defaults, and for the new
/// <c>Cors</c> section in <c>RegistryConfig</c>.
/// </summary>
public sealed class CorsConfigTests : IDisposable
{
    private readonly string _tempDir;

    public CorsConfigTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"stash-corsconfig-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Default value tests
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CorsConfig_Defaults_EnabledIsFalse()
    {
        var config = new CorsConfig();
        Assert.False(config.Enabled);
    }

    [Fact]
    public void CorsConfig_Defaults_AllowedOriginsIsEmpty()
    {
        var config = new CorsConfig();
        Assert.Empty(config.AllowedOrigins);
    }

    [Fact]
    public void CorsConfig_Defaults_AllowCredentialsIsFalse()
    {
        var config = new CorsConfig();
        Assert.False(config.AllowCredentials);
    }

    [Fact]
    public void RegistryConfig_Defaults_HasCorsSectionWithOffDefault()
    {
        var config = new RegistryConfig();
        Assert.NotNull(config.Cors);
        Assert.False(config.Cors.Enabled);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Configuration binding tests
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CorsConfig_BindFromJson_ParsesEnabledTrue()
    {
        string path = WriteJson(new
        {
            Cors = new
            {
                Enabled = true,
                AllowedOrigins = new[] { "https://example.com" },
                AllowedMethods = new[] { "GET", "HEAD" },
                AllowedHeaders = new[] { "Content-Type", "Authorization" },
                AllowCredentials = false
            }
        });

        var cfg = new ConfigurationBuilder().AddJsonFile(path).Build();
        var cors = cfg.GetSection("Cors").Get<CorsConfig>() ?? new CorsConfig();

        Assert.True(cors.Enabled);
        Assert.Single(cors.AllowedOrigins);
        Assert.Equal("https://example.com", cors.AllowedOrigins[0]);
        Assert.Equal(new[] { "GET", "HEAD" }, cors.AllowedMethods);
        Assert.Equal(new[] { "Content-Type", "Authorization" }, cors.AllowedHeaders);
        Assert.False(cors.AllowCredentials);
    }

    [Fact]
    public void CorsConfig_BindFromJson_AllowCredentialsTrue()
    {
        string path = WriteJson(new
        {
            Cors = new
            {
                Enabled = true,
                AllowedOrigins = new[] { "https://example.com" },
                AllowedMethods = new[] { "GET" },
                AllowedHeaders = new[] { "Authorization" },
                AllowCredentials = true
            }
        });

        var cfg = new ConfigurationBuilder().AddJsonFile(path).Build();
        var cors = cfg.GetSection("Cors").Get<CorsConfig>() ?? new CorsConfig();

        Assert.True(cors.AllowCredentials);
    }

    [Fact]
    public void RegistryConfig_BindFromJson_ParsesCorsSection()
    {
        string path = WriteJson(new
        {
            Server = new { Port = 8080 },
            Cors = new
            {
                Enabled = true,
                AllowedOrigins = new[] { "https://ui.example.com" },
                AllowedMethods = new[] { "GET", "HEAD" },
                AllowedHeaders = new[] { "Content-Type", "Authorization", "If-None-Match", "If-Modified-Since" },
                AllowCredentials = false
            }
        });

        var cfg = new ConfigurationBuilder().AddJsonFile(path).Build();
        var config = cfg.Get<RegistryConfig>() ?? new RegistryConfig();

        Assert.True(config.Cors.Enabled);
        Assert.Single(config.Cors.AllowedOrigins);
        Assert.Equal("https://ui.example.com", config.Cors.AllowedOrigins[0]);
    }

    [Fact]
    public void CorsConfig_BindFromJson_ListValuesNotDuplicated()
    {
        // Guards against the .NET config-binder list-append quirk: binding a section whose
        // list values equal the C# defaults must produce exactly the JSON values, not
        // "defaults + JSON values".
        string path = WriteJson(new
        {
            Cors = new
            {
                Enabled = false,
                AllowedOrigins = new string[] { },
                AllowedMethods = new[] { "GET", "HEAD" },
                AllowedHeaders = new[]
                {
                    "Content-Type", "Authorization", "If-None-Match", "If-Modified-Since"
                },
                AllowCredentials = false
            }
        });

        var cfg = new ConfigurationBuilder().AddJsonFile(path).Build();
        var cors = cfg.GetSection("Cors").Get<CorsConfig>() ?? new CorsConfig();

        Assert.Equal(2, cors.AllowedMethods.Count);
        Assert.Equal(4, cors.AllowedHeaders.Count);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // appsettings.json round-trip
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ShippedAppsettings_CorsSection_DisabledByDefault()
    {
        // Locate appsettings.json shipped with Stash.Registry.
        string? path = FindShippedAppsettings();
        Assert.NotNull(path);

        var cfg = new ConfigurationBuilder().AddJsonFile(path!, optional: false).Build();
        var config = cfg.GetSection("Registry").Get<RegistryConfig>() ?? new RegistryConfig();

        Assert.False(config.Cors.Enabled);
        Assert.Empty(config.Cors.AllowedOrigins);
        Assert.False(config.Cors.AllowCredentials);
    }

    [Fact]
    public void ShippedAppsettings_CorsSection_HasExpectedMethodsAndHeaders()
    {
        string? path = FindShippedAppsettings();
        Assert.NotNull(path);

        var cfg = new ConfigurationBuilder().AddJsonFile(path!, optional: false).Build();
        var cors = cfg.GetSection("Registry:Cors").Get<CorsConfig>() ?? new CorsConfig();

        // Verify the shipped defaults match the spec (no duplicates, correct values).
        Assert.Equal(new[] { "GET", "HEAD" }, cors.AllowedMethods);
        Assert.Equal(
            new[] { "Content-Type", "Authorization", "If-None-Match", "If-Modified-Since" },
            cors.AllowedHeaders);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private string WriteJson(object obj)
    {
        string path = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(obj));
        return path;
    }

    private static string? FindShippedAppsettings()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null)
        {
            string candidate = Path.Combine(d.FullName, "Stash.Registry", "appsettings.json");
            if (File.Exists(candidate))
                return candidate;
            d = d.Parent;
        }
        return null;
    }
}
