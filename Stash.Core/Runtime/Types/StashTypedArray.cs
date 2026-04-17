namespace Stash.Runtime.Types;

using System;
using System.Collections.Generic;
using System.Text;
using Stash.Common;
using Stash.Runtime;
using Stash.Runtime.Protocols;

public abstract class StashTypedArray : IVMTyped, IVMIterable, IVMIndexable, IVMSized, IVMStringifiable
{
    public abstract string ElementTypeName { get; }
    public abstract int Count { get; }
    public abstract int Capacity { get; }
    public abstract StashValue Get(int index);
    public abstract void Set(int index, StashValue val);
    public abstract void Add(StashValue val);
    public abstract StashValue RemoveLast();
    public abstract void Insert(int index, StashValue val);
    public abstract void RemoveAt(int index);
    public abstract StashTypedArray Clone();
    public abstract void Clear();
    public abstract StashTypedArray CreateEmpty();  // factory for same-type empty array

    // Shared negative-index resolution
    public int ResolveIndex(int index)
    {
        if (index < 0) index += Count;
        return index;
    }

    // Shared bounds check
    public void CheckBounds(int index, string operation)
    {
        if (index < 0 || index >= Count)
            throw new RuntimeError($"Index {index} out of bounds for {ElementTypeName}[] of length {Count}.");
    }

    // Factory method to create the right subclass from a type name + source list
    public static StashTypedArray Create(string elementType, List<StashValue> source)
    {
        return elementType switch
        {
            "int" => new StashIntArray(source),
            "float" => new StashFloatArray(source),
            "string" => new StashStringArray(source),
            "bool" => new StashBoolArray(source),
            "byte" => new StashByteArray(source),
            _ => throw new RuntimeError($"Unknown typed array element type: '{elementType}'. Valid types are: int, float, string, bool, byte.")
        };
    }

    // Factory for creating with capacity (zero-initialized)
    public static StashTypedArray CreateWithCapacity(string elementType, int capacity)
    {
        return elementType switch
        {
            "int" => new StashIntArray(capacity),
            "float" => new StashFloatArray(capacity),
            "string" => new StashStringArray(capacity),
            "bool" => new StashBoolArray(capacity),
            "byte" => new StashByteArray(capacity),
            _ => throw new RuntimeError($"Unknown typed array element type: '{elementType}'. Valid types are: int, float, string, bool, byte.")
        };
    }

    // Stringify helper
    public string Stringify()
    {
        var sb = new StringBuilder();
        sb.Append(ElementTypeName);
        sb.Append('[');
        for (int i = 0; i < Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(RuntimeValues.Stringify(Get(i).ToObject()));
        }
        sb.Append(']');
        return sb.ToString();
    }

    // --- Protocol implementations ---

    public string VMTypeName => ElementTypeName + "[]";

    public IVMIterator VMGetIterator(bool indexed)
    {
        return new StashTypedArrayIterator(this);
    }

    public StashValue VMGetIndex(StashValue index, SourceSpan? span)
    {
        if (!index.IsInt)
            throw new RuntimeError($"Typed array index must be an integer.", span);
        int resolved = ResolveIndex((int)index.AsInt);
        CheckBounds(resolved, "get");
        return Get(resolved);
    }

    public void VMSetIndex(StashValue index, StashValue value, SourceSpan? span)
    {
        if (!index.IsInt)
            throw new RuntimeError($"Typed array index must be an integer.", span);
        int resolved = ResolveIndex((int)index.AsInt);
        CheckBounds(resolved, "set");
        Set(resolved, value);
    }

    public long VMLength => Count;

    public string VMToString() => Stringify();
}

internal sealed class StashTypedArrayIterator : IVMIterator
{
    private readonly List<StashValue> _snapshot;
    private int _index;

    public StashTypedArrayIterator(StashTypedArray array)
    {
        _snapshot = new List<StashValue>(array.Count);
        for (int i = 0; i < array.Count; i++)
            _snapshot.Add(array.Get(i));
        _index = -1;
    }

    public bool MoveNext()
    {
        _index++;
        return _index < _snapshot.Count;
    }

    public StashValue Current => _snapshot[_index];
    public StashValue CurrentKey => StashValue.FromInt(_index);
}
