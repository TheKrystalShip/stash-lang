using System.IO;
using Microsoft.Extensions.Configuration;
using Stash.Registry.Configuration;
using Xunit;

namespace Stash.Tests.Registry;

public class AppsettingsDefaultsTests
{
    private static RegistryConfig LoadShippedConfig()
    {
        // Locate appsettings.json shipped with Stash.Registry by walking up from the test assembly.
        var dir = AppContext.BaseDirectory;
        string? path = null;
        var d = new DirectoryInfo(dir);
        while (d != null)
        {
            var candidate = Path.Combine(d.FullName, "Stash.Registry", "appsettings.json");
            if (File.Exists(candidate)) { path = candidate; break; }
            d = d.Parent;
        }
        Assert.NotNull(path);

        var cfg = new ConfigurationBuilder()
            .AddJsonFile(path!, optional: false)
            .Build();
        return cfg.GetSection("Registry").Get<RegistryConfig>() ?? new RegistryConfig();
    }

    [Fact]
    public void ShippedConfig_FlippingRateLimitingEnabled_ValidatesCleanly()
    {
        var config = LoadShippedConfig();
        config.RateLimiting.Enabled = true;

        // Should not throw — sensible defaults must allow Enabled=true with no other edits.
        RateLimitingConfigValidator.Validate(config.RateLimiting);
    }

    [Fact]
    public void ShippedConfig_AllRateLimitRulesHavePositiveAttemptsAndWindow()
    {
        var rl = LoadShippedConfig().RateLimiting;
        foreach (var (name, rule) in new[]
        {
            ("Auth", rl.Auth),
            ("Publish", rl.Publish),
            ("Download", rl.Download),
            ("Search", rl.Search),
        })
        {
            Assert.True(rule.MaxAttempts > 0, $"{name}.MaxAttempts must be > 0 (was {rule.MaxAttempts})");
            Assert.True(rule.WindowSeconds > 0, $"{name}.WindowSeconds must be > 0 (was {rule.WindowSeconds})");
        }
    }
}
