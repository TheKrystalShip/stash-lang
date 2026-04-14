namespace Stash.Runtime.Types;

using System;
using System.Collections.Generic;
using Stash.Runtime;

public sealed class StashBoolArray : StashTypedArray
{
    private bool[] _data;
    private int _count;
    private const int DefaultCapacity = 4;

    public override string ElementTypeName => "bool";
    public override int Count => _count;
    public override int Capacity => _data.Length;

    // From source list (validates all elements)
    public StashBoolArray(List<StashValue> source)
    {
        _data = new bool[Math.Max(source.Count, DefaultCapacity)];
        for (int i = 0; i < source.Count; i++)
        {
            StashValue v = source[i];
            if (!v.IsBool)
            {
                string actual = v.IsNull ? "null" : RuntimeValues.Stringify(v.ToObject());
                throw new RuntimeError($"Cannot create bool[] — element at index {i} is {TypeNameOf(v)} {actual} — expected bool.");
            }
            _data[i] = v.AsBool;
        }
        _count = source.Count;
    }

    // Zero-initialized with capacity (all false)
    public StashBoolArray(int capacity)
    {
        _data = new bool[Math.Max(capacity, DefaultCapacity)];
        _count = capacity;  // all false
    }

    // Internal copy constructor
    private StashBoolArray(bool[] data, int count)
    {
        _data = new bool[data.Length];
        Array.Copy(data, _data, count);
        _count = count;
    }

    public override StashValue Get(int index)
    {
        return _data[index] ? StashValue.True : StashValue.False;
    }

    public override void Set(int index, StashValue val)
    {
        if (!val.IsBool)
            throw new RuntimeError($"Cannot assign {TypeNameOf(val)} to element of bool[] at index {index} — expected bool.");
        _data[index] = val.AsBool;
    }

    public override void Add(StashValue val)
    {
        if (!val.IsBool)
            throw new RuntimeError($"Cannot add {TypeNameOf(val)} to bool[] — expected bool.");
        EnsureCapacity(_count + 1);
        _data[_count++] = val.AsBool;
    }

    public override StashValue RemoveLast()
    {
        if (_count == 0) throw new RuntimeError("Cannot pop from empty bool[].");
        return _data[--_count] ? StashValue.True : StashValue.False;
    }

    public override void Insert(int index, StashValue val)
    {
        if (!val.IsBool)
            throw new RuntimeError($"Cannot insert {TypeNameOf(val)} into bool[] — expected bool.");
        EnsureCapacity(_count + 1);
        Array.Copy(_data, index, _data, index + 1, _count - index);
        _data[index] = val.AsBool;
        _count++;
    }

    public override void RemoveAt(int index)
    {
        _count--;
        Array.Copy(_data, index + 1, _data, index, _count - index);
    }

    public override StashTypedArray Clone() => new StashBoolArray(_data, _count);
    public override void Clear() => _count = 0;
    public override StashTypedArray CreateEmpty() => new StashBoolArray(new List<StashValue>());

    private void EnsureCapacity(int min)
    {
        if (_data.Length >= min) return;
        int newCap = Math.Max(_data.Length * 2, min);
        var newData = new bool[newCap];
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
