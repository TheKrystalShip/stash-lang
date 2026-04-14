namespace Stash.Runtime.Types;

using System;
using System.Collections.Generic;
using System.Text;
using Stash.Runtime;

public abstract class StashTypedArray
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
}
