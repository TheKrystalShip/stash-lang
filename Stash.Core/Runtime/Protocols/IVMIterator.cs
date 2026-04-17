namespace Stash.Runtime.Protocols;

/// <summary>
/// Iterator state for for-in loops. The VM calls MoveNext/Current in the loop.
/// Replaces the monolithic IteratorState class.
/// </summary>
public interface IVMIterator
{
    /// <summary>
    /// Advance to the next element. Returns false when exhausted.
    /// </summary>
    bool MoveNext();

    /// <summary>
    /// The current element value.
    /// </summary>
    StashValue Current { get; }

    /// <summary>
    /// The current index/key (for indexed iteration: for value, key in collection).
    /// </summary>
    StashValue CurrentKey { get; }
}
