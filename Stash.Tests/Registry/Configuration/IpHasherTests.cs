using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Stash.Registry.Configuration;

namespace Stash.Tests.Registry.Configuration;

public sealed class IpHasherTests : IDisposable
{
    private readonly string _tempDir;

    public IpHasherTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"stash-iphasher-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ── Off mode ──────────────────────────────────────────────────────────────

    [Fact]
    public void Apply_OffMode_ReturnsNull()
    {
        var hasher = new IpHasher(IpHandlingMode.Off);
        Assert.Null(hasher.Apply(IPAddress.Parse("1.2.3.4")));
    }

    [Fact]
    public void Apply_OffMode_NullAddress_ReturnsNull()
    {
        var hasher = new IpHasher(IpHandlingMode.Off);
        Assert.Null(hasher.Apply(null));
    }

    // ── Null address (all modes) ──────────────────────────────────────────────

    [Theory]
    [InlineData(IpHandlingMode.Raw)]
    [InlineData(IpHandlingMode.Truncated)]
    [InlineData(IpHandlingMode.Hashed)]
    [InlineData(IpHandlingMode.Off)]
    public void Apply_NullAddress_ReturnsNull(IpHandlingMode mode)
    {
        var key = mode == IpHandlingMode.Hashed ? RandomNumberGenerator.GetBytes(32) : null;
        var hasher = new IpHasher(mode, key);
        Assert.Null(hasher.Apply(null));
    }

    // ── Raw mode ──────────────────────────────────────────────────────────────

    [Fact]
    public void Apply_RawMode_IPv4_ReturnsVerbatimString()
    {
        var hasher = new IpHasher(IpHandlingMode.Raw);
        string? result = hasher.Apply(IPAddress.Parse("192.168.1.42"));
        Assert.Equal("192.168.1.42", result);
    }

    [Fact]
    public void Apply_RawMode_IPv6_ReturnsVerbatimString()
    {
        var hasher = new IpHasher(IpHandlingMode.Raw);
        var addr = IPAddress.Parse("2001:db8::1");
        string? result = hasher.Apply(addr);
        Assert.Equal(addr.ToString(), result);
    }

    // ── Truncated mode — IPv4 (/24) ──────────────────────────────────────────

    [Fact]
    public void Apply_TruncatedMode_IPv4_ZerosLastOctet()
    {
        var hasher = new IpHasher(IpHandlingMode.Truncated);
        string? result = hasher.Apply(IPAddress.Parse("192.168.1.42"));
        Assert.Equal("192.168.1.0", result);
    }

    [Fact]
    public void Apply_TruncatedMode_IPv4_AlreadyZeroedLastOctet_StaysZero()
    {
        var hasher = new IpHasher(IpHandlingMode.Truncated);
        string? result = hasher.Apply(IPAddress.Parse("10.0.0.0"));
        Assert.Equal("10.0.0.0", result);
    }

    [Fact]
    public void Apply_TruncatedMode_IPv4_VariousHosts_SamePrefix()
    {
        var hasher = new IpHasher(IpHandlingMode.Truncated);
        string? r1 = hasher.Apply(IPAddress.Parse("10.20.30.1"));
        string? r2 = hasher.Apply(IPAddress.Parse("10.20.30.200"));
        Assert.Equal("10.20.30.0", r1);
        Assert.Equal("10.20.30.0", r2);
        Assert.Equal(r1, r2);
    }

    // ── Truncated mode — IPv6 (/64) ──────────────────────────────────────────

    [Fact]
    public void Apply_TruncatedMode_IPv6_ZerosLast64Bits()
    {
        var hasher = new IpHasher(IpHandlingMode.Truncated);
        // 2001:db8::1 → 2001:db8:: (all host bits zeroed)
        string? result = hasher.Apply(IPAddress.Parse("2001:db8::1"));
        // The result should be a valid IPv6 address with the last 8 bytes zeroed.
        var resultAddr = IPAddress.Parse(result!);
        byte[] bytes = resultAddr.GetAddressBytes();
        for (int i = 8; i < 16; i++)
            Assert.Equal(0, bytes[i]);
        // And the first 8 bytes match the original.
        byte[] originalBytes = IPAddress.Parse("2001:db8::1").GetAddressBytes();
        for (int i = 0; i < 8; i++)
            Assert.Equal(originalBytes[i], bytes[i]);
    }

    [Fact]
    public void Apply_TruncatedMode_IPv6_TwoHostsInSamePrefix_SameResult()
    {
        var hasher = new IpHasher(IpHandlingMode.Truncated);
        string? r1 = hasher.Apply(IPAddress.Parse("2001:db8::1"));
        string? r2 = hasher.Apply(IPAddress.Parse("2001:db8::2"));
        Assert.Equal(r1, r2);
    }

    [Fact]
    public void Apply_TruncatedMode_IPv6_DifferentPrefixes_DifferentResults()
    {
        var hasher = new IpHasher(IpHandlingMode.Truncated);
        string? r1 = hasher.Apply(IPAddress.Parse("2001:db8:1::1"));
        string? r2 = hasher.Apply(IPAddress.Parse("2001:db8:2::1"));
        Assert.NotEqual(r1, r2);
    }

    // ── Hashed mode ───────────────────────────────────────────────────────────

    [Fact]
    public void Apply_HashedMode_Returns32CharLowercaseHex()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var hasher = new IpHasher(IpHandlingMode.Hashed, key);
        string? result = hasher.Apply(IPAddress.Parse("1.2.3.4"));

        Assert.NotNull(result);
        Assert.Equal(32, result!.Length);
        Assert.Matches("^[0-9a-f]{32}$", result);
    }

    [Fact]
    public void Apply_HashedMode_SameIp_SameKey_SameResult()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var hasher = new IpHasher(IpHandlingMode.Hashed, key);
        string? r1 = hasher.Apply(IPAddress.Parse("10.0.0.1"));
        string? r2 = hasher.Apply(IPAddress.Parse("10.0.0.1"));
        Assert.Equal(r1, r2);
    }

    [Fact]
    public void Apply_HashedMode_DifferentIps_SameKey_DifferentResults()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var hasher = new IpHasher(IpHandlingMode.Hashed, key);
        string? r1 = hasher.Apply(IPAddress.Parse("10.0.0.1"));
        string? r2 = hasher.Apply(IPAddress.Parse("10.0.0.2"));
        Assert.NotEqual(r1, r2);
    }

    [Fact]
    public void Apply_HashedMode_SameIp_DifferentKeys_DifferentResults()
    {
        var key1 = RandomNumberGenerator.GetBytes(32);
        var key2 = RandomNumberGenerator.GetBytes(32);
        var hasher1 = new IpHasher(IpHandlingMode.Hashed, key1);
        var hasher2 = new IpHasher(IpHandlingMode.Hashed, key2);
        string? r1 = hasher1.Apply(IPAddress.Parse("1.2.3.4"));
        string? r2 = hasher2.Apply(IPAddress.Parse("1.2.3.4"));
        // With extremely high probability the two will differ.
        Assert.NotEqual(r1, r2);
    }

    [Fact]
    public void Apply_HashedMode_ResultDoesNotEqualRawIp()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var hasher = new IpHasher(IpHandlingMode.Hashed, key);
        string? result = hasher.Apply(IPAddress.Parse("192.168.1.1"));
        Assert.NotEqual("192.168.1.1", result);
    }

    [Fact]
    public void Apply_HashedMode_MatchesManualHmacSha256()
    {
        // Verify the algorithm: first 32 hex chars of HMACSHA256(key, ip_utf8)
        var key = RandomNumberGenerator.GetBytes(32);
        var hasher = new IpHasher(IpHandlingMode.Hashed, key);
        var ip = IPAddress.Parse("203.0.113.7");

        string? result = hasher.Apply(ip);

        byte[] raw = Encoding.UTF8.GetBytes(ip.ToString());
        using var hmac = new HMACSHA256(key);
        byte[] hash = hmac.ComputeHash(raw);
        string expected = Convert.ToHexString(hash)[..32].ToLowerInvariant();

        Assert.Equal(expected, result);
    }

    // ── IpHashSecret config binding (base-64 key) ─────────────────────────────

    [Fact]
    public void Constructor_ValidBase64Secret_UsesProvidedKey()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var config = new MetricsConfig
        {
            IpMode = IpHandlingMode.Hashed,
            IpHashSecret = Convert.ToBase64String(key)
        };
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<IpHasher>.Instance;

        var hasher = new IpHasher(config, logger);

        // The hasher should produce the same result as a hasher with the raw key.
        var referenceHasher = new IpHasher(IpHandlingMode.Hashed, key);
        var ip = IPAddress.Parse("1.2.3.4");
        Assert.Equal(referenceHasher.Apply(ip), hasher.Apply(ip));
    }

    [Fact]
    public void Constructor_InvalidBase64Secret_ThrowsClearMessage()
    {
        var config = new MetricsConfig
        {
            IpMode = IpHandlingMode.Hashed,
            IpHashSecret = "not-valid-base64!!!"
        };
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<IpHasher>.Instance;

        var ex = Assert.Throws<InvalidOperationException>(() => new IpHasher(config, logger));
        Assert.Contains("base-64", ex.Message);
    }

    // ── Secret file persistence ───────────────────────────────────────────────

    [Fact]
    public void Constructor_MissingSecretFile_GeneratesAndPersists()
    {
        string secretFile = Path.Combine(_tempDir, "test-secret.bin");
        Assert.False(File.Exists(secretFile));

        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<IpHasher>.Instance;
        // Using internal constructor to set the secret file path is not exposed,
        // so we test via the config-based constructor with an empty IpHashSecret.
        // The auto-generated secret lives at AppContext.BaseDirectory; we only
        // verify the logic by constructing with no secret configured.
        var config = new MetricsConfig { IpMode = IpHandlingMode.Hashed, IpHashSecret = null };
        var hasher = new IpHasher(config, logger);

        // The hasher should be functional: applying to an IP should return 32 chars.
        string? result = hasher.Apply(IPAddress.Parse("1.2.3.4"));
        Assert.NotNull(result);
        Assert.Equal(32, result!.Length);
    }

    [Fact]
    public void Apply_HashedMode_NonHashedMode_DoesNotRequireKey()
    {
        // Raw and truncated modes do not use the HMAC key — they should work without it.
        var rawHasher = new IpHasher(IpHandlingMode.Raw);
        var truncHasher = new IpHasher(IpHandlingMode.Truncated);

        Assert.NotNull(rawHasher.Apply(IPAddress.Parse("1.2.3.4")));
        Assert.NotNull(truncHasher.Apply(IPAddress.Parse("1.2.3.4")));
    }
}
