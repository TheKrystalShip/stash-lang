namespace Stash.Tests.Stdlib.SourceGenerator.Fixtures;

using Stash.Runtime.Errors;
using Stash.Stdlib.Abstractions;

/// <summary>
/// Test-only fixture exercising [StashFn(ThrowsTypes=...)] and &lt;exception&gt; doc-comment throws metadata.
/// Used by <c>ThrowsMetadataTests</c>.
/// </summary>
[StashNamespace]
public static partial class ThrowsMetadataFixture
{
    /// <summary>Reads data from a file using only the attribute for throws metadata.</summary>
    /// <param name="path">The file path.</param>
    [StashFn(ThrowsTypes = new[] { typeof(IOError) })]
    public static string WithAttributeOnly(string path) => path;

    /// <summary>Parses a value using only the doc-comment for throws metadata.</summary>
    /// <param name="value">The value string.</param>
    /// <exception cref="ValueError">if the value is invalid</exception>
    [StashFn]
    public static long WithDocCommentOnly(string value) => 0L;

    /// <summary>Reads and parses a file; both sources agree on the throws type.</summary>
    /// <param name="path">The file path.</param>
    /// <exception cref="IOError">if file is missing</exception>
    [StashFn(ThrowsTypes = new[] { typeof(IOError) })]
    public static string WithBothAgree(string path) => path;

    /// <summary>Does something that may fail; attribute and doc-comment disagree on throws type.</summary>
    /// <param name="path">The path.</param>
    /// <exception cref="ValueError">if value is wrong</exception>
    [StashFn(ThrowsTypes = new[] { typeof(IOError) })]
    public static string WithBothDisagree(string path) => path;
}
