using System.Collections.Generic;
using Stash.Runtime.Types;

namespace Stash.Bytecode;

/// <summary>
/// Wraps an <see cref="IEnumerator{T}"/> with a current-element index counter.
/// Used internally by the VM to execute for-in loops.
/// </summary>
internal sealed class StashIterator
{
    private readonly IEnumerator<object?> _inner;

    public int Index { get; private set; } = -1;

    /// <summary>When non-null, this iterator is iterating over a dict's keys; the dict is stored here for value lookup.</summary>
    public StashDictionary? Dictionary { get; }

    public StashIterator(IEnumerator<object?> inner) => _inner = inner;

    public StashIterator(IEnumerator<object?> inner, StashDictionary dict)
    {
        _inner = inner;
        Dictionary = dict;
    }

    public bool MoveNext()
    {
        Index++;
        return _inner.MoveNext();
    }

    public object? Current => _inner.Current;
}
