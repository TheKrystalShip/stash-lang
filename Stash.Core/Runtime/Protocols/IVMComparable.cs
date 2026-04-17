namespace Stash.Runtime.Protocols;

using Stash.Common;

/// <summary>
/// Supports ordering comparisons (&lt;, &gt;, &lt;=, &gt;=).
/// </summary>
public interface IVMComparable
{
    /// <summary>
    /// Compare this value to another. Returns negative, zero, or positive via out parameter.
    /// Returns false if the values are not comparable.
    /// </summary>
    bool VMTryCompare(StashValue other, out int result, SourceSpan? span);
}
