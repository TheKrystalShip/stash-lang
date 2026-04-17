namespace Stash.Runtime.Protocols;

using Stash.Common;

/// <summary>
/// Supports bracket-based index access (value[index]).
/// </summary>
public interface IVMIndexable
{
    /// <summary>
    /// Get the value at the given index.
    /// </summary>
    StashValue VMGetIndex(StashValue index, SourceSpan? span);

    /// <summary>
    /// Set the value at the given index.
    /// </summary>
    void VMSetIndex(StashValue index, StashValue value, SourceSpan? span);
}
