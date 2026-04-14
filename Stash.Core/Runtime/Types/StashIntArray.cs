namespace Stash.Runtime.Types;

using System;
using System.Collections.Generic;
using Stash.Runtime;

public sealed class StashIntArray : StashTypedArray
{
    private long[] _data;
    private int _count;
    private const int DefaultCapacity = 4;

    public override string ElementTypeName => "int";
    public override int Count => _count;
    public override int Capacity => _data.Length;

    // From source list (validates all elements)
    public StashIntArray(List<StashValue> source)
    {
        _data = new long[Math.Max(source.Count, DefaultCapacity)];
        for (int i = 0; i < source.Count; i++)
        {
            StashValue v = source[i];
            if (!v.IsInt)
            {
                string actual = v.IsNull ? "null" : RuntimeValues.Stringify(v.ToObject());
                throw new RuntimeError($"Cannot create int[] — element at index {i} is {TypeNameOf(v)} {actual} — expected int.");
            }
            _data[i] = v.AsInt;
        }
        _count = source.Count;
    }

    // Zero-initialized with capacity
    public StashIntArray(int capacity)
    {
        _data = new long[Math.Max(capacity, DefaultCapacity)];
        _count = capacity;  // all zeros
    }

    // Internal copy constructor
    private StashIntArray(long[] data, int count)
    {
        _data = new long[data.Length];
        Array.Copy(data, _data, count);
        _count = count;
    }

    public override StashValue Get(int index)
    {
        return StashValue.FromInt(_data[index]);
    }

    public override void Set(int index, StashValue val)
    {
        if (!val.IsInt)
            throw new RuntimeError($"Cannot assign {TypeNameOf(val)} to element of int[] at index {index} — expected int.");
        _data[index] = val.AsInt;
    }

    public override void Add(StashValue val)
    {
        if (!val.IsInt)
            throw new RuntimeError($"Cannot add {TypeNameOf(val)} to int[] — expected int.");
        EnsureCapacity(_count + 1);
        _data[_count++] = val.AsInt;
    }

    public override StashValue RemoveLast()
    {
        if (_count == 0) throw new RuntimeError("Cannot pop from empty int[].");
        return StashValue.FromInt(_data[--_count]);
    }

    public override void Insert(int index, StashValue val)
    {
        if (!val.IsInt)
            throw new RuntimeError($"Cannot insert {TypeNameOf(val)} into int[] — expected int.");
        EnsureCapacity(_count + 1);
        Array.Copy(_data, index, _data, index + 1, _count - index);
        _data[index] = val.AsInt;
        _count++;
    }

    public override void RemoveAt(int index)
    {
        _count--;
        Array.Copy(_data, index + 1, _data, index, _count - index);
    }

    public override StashTypedArray Clone() => new StashIntArray(_data, _count);
    public override void Clear() => _count = 0;
    public override StashTypedArray CreateEmpty() => new StashIntArray(new List<StashValue>());

    private void EnsureCapacity(int min)
    {
        if (_data.Length >= min) return;
        int newCap = Math.Max(_data.Length * 2, min);
        var newData = new long[newCap];
        Array.Copy(_data, newData, _count);
        _data = newData;
    }

    private static string TypeNameOf(StashValue v)
    {
        if (v.IsNull) return "null";
        if (v.IsInt) return "int";
        if (v.IsFloat) return "float";
        if (v.IsBool) return "bool";
        if (v.IsObj && v.AsObj is string) return "string";
        return v.ToObject()?.GetType().Name ?? "null";
    }
}
