namespace Stash.Tests.Stdlib.SourceGenerator.Fixtures;

using Stash.Runtime;
using Stash.Stdlib.Abstractions;

/// <summary>
/// Test-only fixture exercising <c>[StashMember]</c> registration with various configurations.
/// </summary>
[StashNamespace]
public static partial class StashMemberFixture
{
    /// <summary>A cached member (default stability) that returns a fixed string.</summary>
    [StashMember(ReturnType = "string")]
    public static string CachedMember(IInterpreterContext ctx) => "cached-value";

    /// <summary>A live member that reads a mutable host-side counter.</summary>
    [StashMember(Stability = Stability.Live, ReturnType = "int")]
    public static long LiveMember(IInterpreterContext ctx) => 42L;

    /// <summary>A member gated on Environment capability.</summary>
    [StashMember(Capability = StashCapabilities.Environment, ReturnType = "string")]
    public static string GatedMember(IInterpreterContext ctx) => "env-value";

    /// <summary>Always available function for comparison.</summary>
    [StashFn]
    public static long AlwaysFn() => 1L;
}
