using Microsoft.Extensions.Configuration;
using Stash.Registry.Configuration;

namespace Stash.Tests.Registry.Configuration;

public sealed class MetricsConfigTests
{
    // ── Default binding ───────────────────────────────────────────────────────

    [Fact]
    public void DefaultConfig_HasExpectedDefaults()
    {
        var config = new MetricsConfig();

        Assert.True(config.Enabled);
        Assert.Equal(IpHandlingMode.Hashed, config.IpMode);
        Assert.Null(config.IpHashSecret);
        Assert.Equal(30, config.Raw.RetentionDays);
        Assert.Equal(60, config.Rollup.IntervalMinutes);
    }

    [Fact]
    public void RegistryConfig_Metrics_ExposedSection()
    {
        var registryConfig = new RegistryConfig();

        Assert.NotNull(registryConfig.Metrics);
        Assert.Equal(IpHandlingMode.Hashed, registryConfig.Metrics.IpMode);
    }

    // ── ConfigurationBinder binding ───────────────────────────────────────────

    [Fact]
    public void Bind_ValidIpMode_Hashed_BindsCorrectly()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Metrics:IpMode"] = "hashed",
            ["Metrics:Raw:RetentionDays"] = "30",
            ["Metrics:Rollup:IntervalMinutes"] = "60"
        });

        Assert.Equal(IpHandlingMode.Hashed, config.Metrics.IpMode);
        Assert.Equal(30, config.Metrics.Raw.RetentionDays);
        Assert.Equal(60, config.Metrics.Rollup.IntervalMinutes);
    }

    [Fact]
    public void Bind_ValidIpMode_Raw_BindsCorrectly()
    {
        var config = BuildConfig(new Dictionary<string, string?> { ["Metrics:IpMode"] = "raw" });
        Assert.Equal(IpHandlingMode.Raw, config.Metrics.IpMode);
    }

    [Fact]
    public void Bind_ValidIpMode_Truncated_BindsCorrectly()
    {
        var config = BuildConfig(new Dictionary<string, string?> { ["Metrics:IpMode"] = "truncated" });
        Assert.Equal(IpHandlingMode.Truncated, config.Metrics.IpMode);
    }

    [Fact]
    public void Bind_ValidIpMode_Off_BindsCorrectly()
    {
        var config = BuildConfig(new Dictionary<string, string?> { ["Metrics:IpMode"] = "off" });
        Assert.Equal(IpHandlingMode.Off, config.Metrics.IpMode);
    }

    [Fact]
    public void Bind_IpModeCaseInsensitive_BindsCorrectly()
    {
        var config = BuildConfig(new Dictionary<string, string?> { ["Metrics:IpMode"] = "HASHED" });
        Assert.Equal(IpHandlingMode.Hashed, config.Metrics.IpMode);
    }

    [Fact]
    public void Bind_NonZeroRetentionDays_BindsCorrectly()
    {
        var config = BuildConfig(new Dictionary<string, string?> { ["Metrics:Raw:RetentionDays"] = "90" });
        Assert.Equal(90, config.Metrics.Raw.RetentionDays);
    }

    [Fact]
    public void Bind_CustomRollupInterval_BindsCorrectly()
    {
        var config = BuildConfig(new Dictionary<string, string?> { ["Metrics:Rollup:IntervalMinutes"] = "30" });
        Assert.Equal(30, config.Metrics.Rollup.IntervalMinutes);
    }

    // ── Validator ─────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_DefaultConfig_NoRawIpMode_Passes()
    {
        // Absence of the key means rawIpModeString is null → no validation needed.
        var config = new MetricsConfig();
        MetricsConfigValidator.Validate(config, rawIpModeString: null); // should not throw
    }

    [Fact]
    public void Validate_KnownIpMode_Passes()
    {
        var config = new MetricsConfig { IpMode = IpHandlingMode.Truncated };
        MetricsConfigValidator.Validate(config, rawIpModeString: "truncated"); // should not throw
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("UNKNOWN")]
    [InlineData("sha256")]
    [InlineData("none")]
    [InlineData("")]
    public void Validate_UnknownIpMode_ThrowsWithClearMessage(string badMode)
    {
        var config = new MetricsConfig();
        var ex = Assert.Throws<InvalidOperationException>(
            () => MetricsConfigValidator.Validate(config, rawIpModeString: badMode));

        Assert.Contains("IpMode", ex.Message);
        Assert.Contains(badMode, ex.Message);
        // The message must name the legal values.
        Assert.Contains("raw", ex.Message);
        Assert.Contains("hashed", ex.Message);
        Assert.Contains("off", ex.Message);
    }

    [Fact]
    public void Validate_NegativeRetentionDays_Throws()
    {
        var config = new MetricsConfig { Raw = new MetricsRawConfig { RetentionDays = -1 } };
        var ex = Assert.Throws<InvalidOperationException>(
            () => MetricsConfigValidator.Validate(config, rawIpModeString: null));

        Assert.Contains("RetentionDays", ex.Message);
    }

    [Fact]
    public void Validate_ZeroRetentionDays_Passes()
    {
        // RetentionDays=0 disables raw capture — this is a valid state.
        var config = new MetricsConfig { Raw = new MetricsRawConfig { RetentionDays = 0 } };
        MetricsConfigValidator.Validate(config, rawIpModeString: null); // should not throw
    }

    [Fact]
    public void Validate_ZeroIntervalMinutes_Throws()
    {
        var config = new MetricsConfig { Rollup = new MetricsRollupConfig { IntervalMinutes = 0 } };
        var ex = Assert.Throws<InvalidOperationException>(
            () => MetricsConfigValidator.Validate(config, rawIpModeString: null));

        Assert.Contains("IntervalMinutes", ex.Message);
    }

    [Fact]
    public void Validate_NegativeIntervalMinutes_Throws()
    {
        var config = new MetricsConfig { Rollup = new MetricsRollupConfig { IntervalMinutes = -5 } };
        var ex = Assert.Throws<InvalidOperationException>(
            () => MetricsConfigValidator.Validate(config, rawIpModeString: null));

        Assert.Contains("IntervalMinutes", ex.Message);
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static RegistryConfig BuildConfig(Dictionary<string, string?> values)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var registryConfig = new RegistryConfig();
        cfg.Bind(registryConfig);
        return registryConfig;
    }
}
