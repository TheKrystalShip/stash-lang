namespace Stash.Tests.Stdlib.SourceGenerator.Fixtures;

using Stash.Runtime;
using Stash.Stdlib.Abstractions;

/// <summary>
/// Test-only fixture exercising per-function capability gating
/// (<c>[StashFn(Capability = ...)]</c>). The namespace itself has no
/// capability requirement; only <see cref="GatedFn"/> is gated on
/// <see cref="StashCapabilities.Environment"/>.
/// </summary>
[StashNamespace]
public static partial class CapabilityFixture
{
    /// <summary>Always available.</summary>
    [StashFn]
    public static long Always() => 1L;

    /// <summary>Only registered when Environment capability is granted.</summary>
    [StashFn(Capability = StashCapabilities.Environment)]
    public static long GatedFn() => 2L;
}
