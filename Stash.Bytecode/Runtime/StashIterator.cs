using System.Collections.Generic;
using Stash.Runtime;
using Stash.Runtime.Types;

namespace Stash.Bytecode;

/// <summary>
/// Wraps an IEnumerator&lt;StashValue&gt; with a current-element index counter.
/// Used internally by the VM to execute for-in loops.
/// </summary>
internal sealed class StashIterator
{
    private readonly IEnumerator<StashValue> _enumerator;

    public int Index { get; private set; } = -1;

    /// <summary>When non-null, this iterator is iterating over a dict's keys; the dict is stored here for value lookup.</summary>
    public StashDictionary? Dictionary { get; }

    public StashIterator(IEnumerator<StashValue> enumerator) => _enumerator = enumerator;

    public StashIterator(IEnumerator<StashValue> enumerator, StashDictionary dict)
    {
        _enumerator = enumerator;
        Dictionary = dict;
    }

    public bool MoveNext()
    {
        Index++;
        return _enumerator.MoveNext();
    }

    public StashValue Current => _enumerator.Current;
}
