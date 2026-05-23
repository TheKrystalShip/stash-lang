namespace Stash.Runtime.Types;

using System.Collections.Generic;
using System.Text;
using Stash.Common;
using Stash.Runtime;
using Stash.Runtime.Errors;
using Stash.Runtime.Protocols;

/// <summary>
/// A read-only view over a <see cref="List{T}">List&lt;StashValue&gt;</see> returned by a
/// <c>DataMember</c> getter. All write operations throw <see cref="ReadOnlyError"/> with the
/// canonical frozen-write message so that <c>cli.argv[0] = "x"</c> fails rather than silently
/// mutating a shared or cached array.
/// </summary>
/// <remarks>
/// Introduced in P2 of stdlib-namespace-members: the VM's DataMember read path wraps any
/// <c>List&lt;StashValue&gt;</c> returned by a getter in this type before pushing it onto
/// the stack, satisfying the side-effect contract ("all reference-typed returns are frozen at
/// the boundary regardless of stability mode").
/// </remarks>
public sealed class StashFrozenArray : IVMTyped, IVMIndexable, IVMIterable, IVMSized, IVMStringifiable
{
    private readonly List<StashValue> _items;

    /// <summary>Creates a frozen view of <paramref name="source"/>. Does NOT copy the list.</summary>
    public StashFrozenArray(List<StashValue> source)
    {
        _items = source;
    }

    public int Count => _items.Count;

    /// <summary>Returns the underlying list for read-only stdlib operations that need it.</summary>
    public List<StashValue> Items => _items;

    // --- Protocol implementations ---

    public string VMTypeName => "array";

    public StashValue VMGetIndex(StashValue index, SourceSpan? span)
    {
        if (!index.IsInt)
            throw new RuntimeError("Array index must be an integer.", span);
        long i = index.AsInt;
        if (i < 0) i += _items.Count;
        if (i < 0 || i >= _items.Count)
            throw new RuntimeError($"Index {index.AsInt} out of bounds for array of length {_items.Count}.", span);
        return _items[(int)i];
    }

    public void VMSetIndex(StashValue index, StashValue value, SourceSpan? span)
    {
        throw new ReadOnlyError("Cannot mutate a read-only array returned by a namespace member.", span);
    }

    public IVMIterator VMGetIterator(bool indexed)
    {
        return new FrozenArrayIterator(_items, indexed);
    }

    public long VMLength => _items.Count;

    public string VMToString() => RuntimeValues.Stringify(this);

    public override string ToString()
    {
        if (_items.Count == 0) return "[]";
        var sb = new StringBuilder("[");
        for (int i = 0; i < _items.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(RuntimeValues.Stringify(_items[i].ToObject()));
        }
        sb.Append(']');
        return sb.ToString();
    }

    private sealed class FrozenArrayIterator : IVMIterator
    {
        private readonly List<StashValue> _items;
        private readonly bool _indexed;
        private int _index;

        public FrozenArrayIterator(List<StashValue> items, bool indexed)
        {
            _items = items;
            _indexed = indexed;
            _index = -1;
        }

        public bool MoveNext()
        {
            _index++;
            return _index < _items.Count;
        }

        public StashValue Current =>
            _indexed ? _items[_index] : _items[_index];

        public StashValue CurrentKey =>
            _indexed ? StashValue.FromInt(_index) : StashValue.FromInt(_index);
    }
}
