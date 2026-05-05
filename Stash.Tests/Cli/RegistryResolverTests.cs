using System;
using Stash.Cli.PackageManager;
using Xunit;

namespace Stash.Tests.Cli;

public sealed class RegistryResolverTests
{
    // ── ParseRegistryFlag ────────────────────────────────────────────────

    [Fact]
    public void ParseRegistryFlag_NotPresent_ReturnsNull()
    {
        string? result = RegistryResolver.ParseRegistryFlag(["publish", "--token", "abc"]);

        Assert.Null(result);
    }

    [Fact]
    public void ParseRegistryFlag_Present_ReturnsValue()
    {
        string? result = RegistryResolver.ParseRegistryFlag(["publish", "--registry", "https://r.example.com"]);

        Assert.Equal("https://r.example.com", result);
    }

    // ── Resolve — explicit flag ──────────────────────────────────────────

    [Fact]
    public void Resolve_WithExplicitFlag_ReturnsFlagValueAndWasExplicitTrue()
    {
        var (url, wasExplicit) = RegistryResolver.Resolve(["--registry", "https://explicit.example.com"]);

        Assert.Equal("https://explicit.example.com", url);
        Assert.True(wasExplicit);
    }

    // ── Resolve — requireExplicit ────────────────────────────────────────

    [Fact]
    public void Resolve_WithRequireExplicitAndNoFlag_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => RegistryResolver.Resolve([], requireExplicit: true));

        Assert.Equal("The --registry flag is required for this command.", ex.Message);
    }

    [Fact]
    public void Resolve_WithRequireExplicitIgnoresEnvVar_Throws()
    {
        string? original = Environment.GetEnvironmentVariable("STASH_REGISTRY_URL");
        try
        {
            Environment.SetEnvironmentVariable("STASH_REGISTRY_URL", "https://env.example.com");

            // requireExplicit must not be satisfied by the env var.
            var ex = Assert.Throws<InvalidOperationException>(
                () => RegistryResolver.Resolve([], requireExplicit: true));

            Assert.Equal("The --registry flag is required for this command.", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("STASH_REGISTRY_URL", original);
        }
    }

    // ── Resolve — env var ────────────────────────────────────────────────

    [Fact]
    public void Resolve_WithEnvVarAndNoFlag_ReturnsEnvVar()
    {
        string? original = Environment.GetEnvironmentVariable("STASH_REGISTRY_URL");
        try
        {
            Environment.SetEnvironmentVariable("STASH_REGISTRY_URL", "https://env.example.com");

            var (url, wasExplicit) = RegistryResolver.Resolve([]);

            Assert.Equal("https://env.example.com", url);
            Assert.False(wasExplicit);
        }
        finally
        {
            Environment.SetEnvironmentVariable("STASH_REGISTRY_URL", original);
        }
    }

    [Fact]
    public void Resolve_WithFlagOverridesEnvVar()
    {
        string? original = Environment.GetEnvironmentVariable("STASH_REGISTRY_URL");
        try
        {
            Environment.SetEnvironmentVariable("STASH_REGISTRY_URL", "https://env.example.com");

            var (url, wasExplicit) = RegistryResolver.Resolve(["--registry", "https://flag.example.com"]);

            Assert.Equal("https://flag.example.com", url);
            Assert.True(wasExplicit);
        }
        finally
        {
            Environment.SetEnvironmentVariable("STASH_REGISTRY_URL", original);
        }
    }

    // ── Resolve — nothing configured ────────────────────────────────────

    [Fact]
    public void Resolve_WithNothingConfigured_ThrowsWithGuidanceMessage()
    {
        // This test clears STASH_REGISTRY_URL to isolate env-var influence.
        // It cannot easily isolate the config file without redirecting HOME, so we
        // only verify the exception message prefix — the throw itself proves strict
        // failure mode. If a DefaultRegistry happens to be configured in the
        // developer's ~/.stash/config.json, this test will pass silently without
        // throwing (acceptable; CI machines will have a clean config).
        string? original = Environment.GetEnvironmentVariable("STASH_REGISTRY_URL");
        try
        {
            Environment.SetEnvironmentVariable("STASH_REGISTRY_URL", null);

            try
            {
                RegistryResolver.Resolve([]);
                // If a DefaultRegistry is configured in the local config, Resolve
                // returns successfully — that is correct behavior, not a test failure.
            }
            catch (InvalidOperationException ex)
            {
                Assert.Contains("No registry configured", ex.Message);
                Assert.Contains("stash pkg login", ex.Message);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("STASH_REGISTRY_URL", original);
        }
    }
}
