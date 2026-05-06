namespace Stash.Tests.Stdlib.SourceGenerator.Fixtures;

using Stash.Runtime;
using Stash.Stdlib.Abstractions;

/// <summary>
/// Test-only fixture exercising [StashParam(Name=...)] on variadic parameters.
/// Used by <c>ParamsNameOverrideTests</c>.
/// </summary>
[StashNamespace]
public static partial class ParamsNameFixture
{
    /// <summary>Returns the count of items; variadic param has no name override.</summary>
    /// <param name="rest">The items.</param>
    [StashFn]
    public static long NoOverride(params StashValue[] rest) => rest.Length;

    /// <summary>Returns the count of items; variadic param is renamed via [StashParam].</summary>
    /// <param name="rest">The items.</param>
    [StashFn]
    public static long WithOverride([StashParam(Name = "paths")] params StashValue[] rest) => rest.Length;
}
