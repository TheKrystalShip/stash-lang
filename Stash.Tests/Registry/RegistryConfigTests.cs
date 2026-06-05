using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Stash.Registry.Configuration;

namespace Stash.Tests.Registry;

public sealed class RegistryConfigTests : IDisposable
{
    private readonly string _tempDir;

    public RegistryConfigTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"stash-config-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void Load_NonExistentFile_ReturnsDefaults()
    {
        string path = Path.Combine(_tempDir, "nonexistent.json");

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(path, optional: true)
            .Build();
        RegistryConfig config = configuration.Get<RegistryConfig>() ?? new RegistryConfig();

        Assert.NotNull(config);
        Assert.Equal(8080, config.Server.Port);
    }

    [Fact]
    public void Load_ValidJson_ParsesCorrectly()
    {
        string path = Path.Combine(_tempDir, "appsettings.json");
        var json = new
        {
            Server = new { Host = "127.0.0.1", Port = 9090, BasePath = "/api/v2" },
            Auth = new { Type = "ldap", RegistrationEnabled = false },
            Security = new { MaxPackageSize = "50MB", UnpublishWindow = "24h" }
        };
        File.WriteAllText(path, JsonSerializer.Serialize(json));

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(path)
            .Build();
        RegistryConfig config = configuration.Get<RegistryConfig>() ?? new RegistryConfig();

        Assert.Equal("127.0.0.1", config.Server.Host);
        Assert.Equal(9090, config.Server.Port);
        Assert.Equal("/api/v2", config.Server.BasePath);
        Assert.Equal("ldap", config.Auth.Type);
        Assert.False(config.Auth.RegistrationEnabled);
        Assert.Equal("50MB", config.Security.MaxPackageSize);
        Assert.Equal("24h", config.Security.UnpublishWindow);
    }

    [Fact]
    public void DefaultConfig_HasExpectedDefaults()
    {
        var config = new RegistryConfig();

        Assert.Equal("0.0.0.0", config.Server.Host);
        Assert.Equal(8080, config.Server.Port);
        Assert.Equal("", config.Server.BasePath);
        Assert.Equal("filesystem", config.Storage.Type);
        Assert.Equal("sqlite", config.Database.Type);
        Assert.Equal("local", config.Auth.Type);
        Assert.False(config.Auth.RegistrationEnabled);
        Assert.Equal("10MB", config.Security.MaxPackageSize);
        Assert.Equal("72h", config.Security.UnpublishWindow);
    }

    [Fact]
    public void DefaultConfig_AuditRetentionDays_IsZero()
    {
        // RetentionDays=0 is the safe default: never-delete for a compliance log.
        var config = new RegistryConfig();

        Assert.Equal(0, config.Audit.RetentionDays);
    }

    [Fact]
    public void DefaultConfig_AuditTamperEvidence_DisabledByDefault()
    {
        // TamperEvidence is an opt-in; must default to off.
        var config = new RegistryConfig();

        Assert.False(config.Audit.TamperEvidence.Enabled);
        Assert.Null(config.Audit.TamperEvidence.HashSecret);
    }

    [Fact]
    public void Load_AuditSection_ParsesRetentionDaysAndTamperEvidence()
    {
        string path = Path.Combine(_tempDir, "appsettings_audit.json");
        var json = new
        {
            Audit = new
            {
                RetentionDays = 365,
                TamperEvidence = new { Enabled = true, HashSecret = "c2VjcmV0" }
            }
        };
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(json));

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(path)
            .Build();
        RegistryConfig config = configuration.Get<RegistryConfig>() ?? new RegistryConfig();

        Assert.Equal(365, config.Audit.RetentionDays);
        Assert.True(config.Audit.TamperEvidence.Enabled);
        Assert.Equal("c2VjcmV0", config.Audit.TamperEvidence.HashSecret);
    }

    [Fact]
    public void MaxPackageSizeBytes_ParsesMB()
    {
        var config = new RegistryConfig();
        config.Security.MaxPackageSize = "10MB";

        Assert.Equal(10L * 1024 * 1024, config.Security.MaxPackageSizeBytes);
    }

    [Fact]
    public void MaxPackageSizeBytes_ParsesKB()
    {
        var config = new RegistryConfig();
        config.Security.MaxPackageSize = "512KB";

        Assert.Equal(512L * 1024, config.Security.MaxPackageSizeBytes);
    }

    [Fact]
    public void UnpublishWindowTimeSpan_ParsesHours()
    {
        var config = new RegistryConfig();
        config.Security.UnpublishWindow = "72h";

        Assert.Equal(TimeSpan.FromHours(72), config.Security.UnpublishWindowTimeSpan);
    }

    [Fact]
    public void UnpublishWindowTimeSpan_ParsesDays()
    {
        var config = new RegistryConfig();
        config.Security.UnpublishWindow = "7d";

        Assert.Equal(TimeSpan.FromDays(7), config.Security.UnpublishWindowTimeSpan);
    }
}
