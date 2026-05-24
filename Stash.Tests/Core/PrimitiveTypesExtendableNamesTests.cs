using Stash.Common;

namespace Stash.Tests.Core;

/// <summary>
/// Unit tests for <see cref="PrimitiveTypes.ExtendableNames"/> — pins the exact set of
/// primitives flagged <see cref="PrimitiveCapability.Extendable"/> before any consumer
/// is migrated to read from this source.
/// </summary>
public class PrimitiveTypesExtendableNamesTests
{
    [Fact]
    public void ExtendableNames_ContainsExactlyTheFiveExtendablePrimitives()
    {
        string[] expected = ["string", "array", "dict", "int", "float"];

        Assert.Equal(expected.Length, PrimitiveTypes.ExtendableNames.Count);

        foreach (var name in expected)
        {
            Assert.True(
                PrimitiveTypes.ExtendableNames.Contains(name),
                $"ExtendableNames is missing '{name}'");
        }
    }

    [Fact]
    public void ExtendableNames_DoesNotContainNonExtendablePrimitives()
    {
        string[] nonExtendable =
        [
            "bool", "byte", "null", "struct", "enum", "function", "namespace",
            "int[]", "float[]", "string[]", "bool[]", "byte[]",
            // Runtime opaque primitives are not extendable
            "Future", "range", "duration", "bytes", "ip", "semver", "secret",
        ];

        foreach (var name in nonExtendable)
        {
            Assert.False(
                PrimitiveTypes.ExtendableNames.Contains(name),
                $"ExtendableNames unexpectedly contains '{name}'");
        }
    }
}
