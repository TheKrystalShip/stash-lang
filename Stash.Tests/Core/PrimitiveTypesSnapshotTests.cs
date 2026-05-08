using Stash.Common;
using Stash.Runtime.Types;

namespace Stash.Tests.Core;

/// <summary>
/// Snapshot tests for <see cref="PrimitiveTypes"/> — guards the key set and description
/// text after the Phase 2 migration to IVMPrimitiveType-based discovery.
/// </summary>
public class PrimitiveTypesSnapshotTests
{
    // =========================================================================
    // Names
    // =========================================================================

    [Fact]
    public void Names_ContainsAllExpectedKeys()
    {
        string[] expected =
        [
            // Language-level primitives (17)
            "int", "float", "string", "bool", "byte", "null",
            "array", "dict", "struct", "enum", "function", "namespace",
            "int[]", "float[]", "string[]", "bool[]", "byte[]",
            // Runtime opaque primitives discovered from IVMPrimitiveType (7)
            "Future", "range", "duration", "bytes", "ip", "semver", "secret",
        ];

        foreach (var key in expected)
            Assert.True(PrimitiveTypes.Names.Contains(key), $"Names does not contain '{key}'");

        Assert.Equal(expected.Length, PrimitiveTypes.Names.Count);
    }

    // =========================================================================
    // Descriptions — runtime primitives byte-identical to prior literals
    // =========================================================================

    [Fact]
    public void Descriptions_RuntimePrimitives_MatchExpectedText()
    {
        Assert.Equal(
            "Represents an asynchronous computation that may not have completed yet. Returned by async functions. Use `await` to get the resolved value.",
            StashFuture.PrimitiveTypeDescription);
        Assert.Equal(
            StashFuture.PrimitiveTypeDescription,
            PrimitiveTypes.Descriptions["Future"].Description);

        Assert.Equal(
            "Range type. Lazy integer sequences like `1..10`.",
            StashRange.PrimitiveTypeDescription);
        Assert.Equal(
            StashRange.PrimitiveTypeDescription,
            PrimitiveTypes.Descriptions["range"].Description);

        Assert.Equal(
            "Duration type. Time spans like `5s`, `1h30m`, `7d`.",
            StashDuration.PrimitiveTypeDescription);
        Assert.Equal(
            StashDuration.PrimitiveTypeDescription,
            PrimitiveTypes.Descriptions["duration"].Description);

        Assert.Equal(
            "Byte size type. Storage sizes like `512b`, `1kb`, `4mb`.",
            StashByteSize.PrimitiveTypeDescription);
        Assert.Equal(
            StashByteSize.PrimitiveTypeDescription,
            PrimitiveTypes.Descriptions["bytes"].Description);

        Assert.Equal(
            "IP address type. IPv4 or IPv6 addresses like `192.168.1.1` or `::1`.",
            StashIpAddress.PrimitiveTypeDescription);
        Assert.Equal(
            StashIpAddress.PrimitiveTypeDescription,
            PrimitiveTypes.Descriptions["ip"].Description);

        Assert.Equal(
            "Semantic version type. Versions like `1.2.3` or `2.0.0-beta.1`.",
            StashSemVer.PrimitiveTypeDescription);
        Assert.Equal(
            StashSemVer.PrimitiveTypeDescription,
            PrimitiveTypes.Descriptions["semver"].Description);

        Assert.Equal(
            "Secret type. Auto-redacts when printed or interpolated. Use `reveal()` to access the underlying value.",
            StashSecret.PrimitiveTypeDescription);
        Assert.Equal(
            StashSecret.PrimitiveTypeDescription,
            PrimitiveTypes.Descriptions["secret"].Description);
    }
}
