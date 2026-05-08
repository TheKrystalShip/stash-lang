using System.Net;
using Stash.Common;
using Stash.Runtime;
using Stash.Runtime.Protocols;
using Stash.Runtime.Types;

namespace Stash.Tests.Core;

/// <summary>
/// Invariant tests for <see cref="IVMPrimitiveType"/> implementers — CI guards that prevent
/// a new runtime primitive type from silently drifting out of <see cref="PrimitiveTypes"/>.
/// </summary>
public class IVMPrimitiveTypeInvariantTests
{
    // =========================================================================
    // Helpers
    // =========================================================================

    private static IEnumerable<Type> GetImplementers() =>
        typeof(StashValue).Assembly
            .GetTypes()
            .Where(t => typeof(IVMPrimitiveType).IsAssignableFrom(t) && !t.IsInterface);

    private static string ReadPrimitiveTypeName(Type type) =>
        (string)type.GetProperty(nameof(IVMPrimitiveType.PrimitiveTypeName))!
            .GetGetMethod()!
            .Invoke(null, null)!;

    /// <summary>
    /// Constructs a test instance for each known <see cref="IVMPrimitiveType"/> implementer.
    /// If a new implementer is added without updating this switch, the test fails with a clear
    /// message — that is the desired CI guard.
    /// </summary>
    private static object ConstructInstance(Type type)
    {
        if (type == typeof(StashFuture))
            return StashFuture.Resolved(StashValue.Null);
        if (type == typeof(StashRange))
            return new StashRange(0, 10, 1);
        if (type == typeof(StashDuration))
            return new StashDuration(0);
        if (type == typeof(StashByteSize))
            return new StashByteSize(0);
        if (type == typeof(StashIpAddress))
            return new StashIpAddress(IPAddress.Loopback);
        if (type == typeof(StashSemVer))
            return new StashSemVer(1, 0, 0);
        if (type == typeof(StashSecret))
            return new StashSecret(StashValue.FromObj("x"));

        Assert.Fail(
            $"{type.Name} implements IVMPrimitiveType but IVMPrimitiveTypeInvariantTests." +
            $"ConstructInstance has no case for it. Add a construction path to keep CI green.");
        return null!; // unreachable — Assert.Fail throws
    }

    // =========================================================================
    // Tests
    // =========================================================================

    [Fact]
    public void PrimitiveTypes_Names_IncludesEveryIVMPrimitiveType()
    {
        foreach (var type in GetImplementers())
        {
            var name = ReadPrimitiveTypeName(type);

            Assert.True(
                PrimitiveTypes.Names.Contains(name),
                $"{type.Name} declares PrimitiveTypeName=\"{name}\" but PrimitiveTypes.Names is missing it");

            Assert.True(
                PrimitiveTypes.Descriptions.ContainsKey(name),
                $"{type.Name} declares PrimitiveTypeName=\"{name}\" but PrimitiveTypes.Descriptions is missing it");
        }
    }

    [Fact]
    public void PrimitiveTypes_VMTypeName_MatchesPrimitiveTypeName()
    {
        var implementers = GetImplementers()
            .Where(t => typeof(IVMTyped).IsAssignableFrom(t));

        foreach (var type in implementers)
        {
            var staticName = ReadPrimitiveTypeName(type);
            var instance = ConstructInstance(type);
            var instanceName = ((IVMTyped)instance).VMTypeName;

            Assert.Equal(
                staticName,
                instanceName);
        }
    }
}
