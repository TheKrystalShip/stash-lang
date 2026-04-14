namespace Stash.Runtime.Types;

using System;
using System.Collections.Generic;
using Stash.Runtime;

public sealed class StashStringArray : StashTypedArray
{
    private string[] _data;
    private int _count;
    private const int DefaultCapacity = 4;

    public override string ElementTypeName => "string";
    public override int Count => _count;
    public override int Capacity => _data.Length;

    // From source list (validates all elements)
    public StashStringArray(List<StashValue> source)
    {
        _data = new string[Math.Max(source.Count, DefaultCapacity)];
        for (int i = 0; i < source.Count; i++)
        {
            StashValue v = source[i];
            if (v.IsObj && v.AsObj is string s)
            {
                _data[i] = s;
            }
            else
            {
                string actual = v.IsNull ? "null" : RuntimeValues.Stringify(v.ToObject());
                throw new RuntimeError($"Cannot create string[] — element at index {i} is {TypeNameOf(v)} {actual} — expected string.");
            }
        }
        _count = source.Count;
    }

    // Empty-initialized with capacity (null strings)
    public StashStringArray(int capacity)
    {
        _data = new string[Math.Max(capacity, DefaultCapacity)];
        // Fill with empty strings for zero-initialization semantics
        for (int i = 0; i < capacity; i++)
            _data[i] = "";
        _count = capacity;
    }

    // Internal copy constructor
    private StashStringArray(string[] data, int count)
    {
        _data = new string[data.Length];
        Array.Copy(data, _data, count);
        _count = count;
    }

    public override StashValue Get(int index)
    {
        return StashValue.FromObj(_data[index]);
    }

    public override void Set(int index, StashValue val)
    {
        if (val.IsObj && val.AsObj is string s)
            _data[index] = s;
        else
            throw new RuntimeError($"Cannot assign {TypeNameOf(val)} to element of string[] at index {index} — expected string.");
    }

    public override void Add(StashValue val)
    {
        if (val.IsObj && val.AsObj is string s)
        {
            EnsureCapacity(_count + 1);
            _data[_count++] = s;
        }
        else
        {
            throw new RuntimeError($"Cannot add {TypeNameOf(val)} to string[] — expected string.");
        }
    }

    public override StashValue RemoveLast()
    {
        if (_count == 0) throw new RuntimeError("Cannot pop from empty string[].");
        return StashValue.FromObj(_data[--_count]);
    }

    public override void Insert(int index, StashValue val)
    {
        if (val.IsObj && val.AsObj is string s)
        {
            EnsureCapacity(_count + 1);
            Array.Copy(_data, index, _data, index + 1, _count - index);
            _data[index] = s;
            _count++;
        }
        else
        {
            throw new RuntimeError($"Cannot insert {TypeNameOf(val)} into string[] — expected string.");
        }
    }

    public override void RemoveAt(int index)
    {
        _count--;
        Array.Copy(_data, index + 1, _data, index, _count - index);
    }

    public override StashTypedArray Clone() => new StashStringArray(_data, _count);
    public override void Clear() => _count = 0;
    public override StashTypedArray CreateEmpty() => new StashStringArray(new List<StashValue>());

    private void EnsureCapacity(int min)
    {
        if (_data.Length >= min) return;
        int newCap = Math.Max(_data.Length * 2, min);
        var newData = new string[newCap];
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
