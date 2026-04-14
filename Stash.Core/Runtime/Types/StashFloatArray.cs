namespace Stash.Runtime.Types;

using System;
using System.Collections.Generic;
using Stash.Runtime;

public sealed class StashFloatArray : StashTypedArray
{
    private double[] _data;
    private int _count;
    private const int DefaultCapacity = 4;

    public override string ElementTypeName => "float";
    public override int Count => _count;
    public override int Capacity => _data.Length;

    // From source list (validates all elements; int→float promotion allowed)
    public StashFloatArray(List<StashValue> source)
    {
        _data = new double[Math.Max(source.Count, DefaultCapacity)];
        for (int i = 0; i < source.Count; i++)
        {
            StashValue v = source[i];
            if (v.IsFloat)
            {
                _data[i] = v.AsFloat;
            }
            else if (v.IsInt)
            {
                _data[i] = (double)v.AsInt;
            }
            else
            {
                string actual = v.IsNull ? "null" : RuntimeValues.Stringify(v.ToObject());
                throw new RuntimeError($"Cannot create float[] — element at index {i} is {TypeNameOf(v)} {actual} — expected float.");
            }
        }
        _count = source.Count;
    }

    // Zero-initialized with capacity
    public StashFloatArray(int capacity)
    {
        _data = new double[Math.Max(capacity, DefaultCapacity)];
        _count = capacity;  // all zeros
    }

    // Internal copy constructor
    private StashFloatArray(double[] data, int count)
    {
        _data = new double[data.Length];
        Array.Copy(data, _data, count);
        _count = count;
    }

    public override StashValue Get(int index)
    {
        return StashValue.FromFloat(_data[index]);
    }

    public override void Set(int index, StashValue val)
    {
        if (val.IsFloat)
            _data[index] = val.AsFloat;
        else if (val.IsInt)
            _data[index] = (double)val.AsInt;
        else
            throw new RuntimeError($"Cannot assign {TypeNameOf(val)} to element of float[] at index {index} — expected float.");
    }

    public override void Add(StashValue val)
    {
        if (val.IsFloat)
        {
            EnsureCapacity(_count + 1);
            _data[_count++] = val.AsFloat;
        }
        else if (val.IsInt)
        {
            EnsureCapacity(_count + 1);
            _data[_count++] = (double)val.AsInt;
        }
        else
        {
            throw new RuntimeError($"Cannot add {TypeNameOf(val)} to float[] — expected float.");
        }
    }

    public override StashValue RemoveLast()
    {
        if (_count == 0) throw new RuntimeError("Cannot pop from empty float[].");
        return StashValue.FromFloat(_data[--_count]);
    }

    public override void Insert(int index, StashValue val)
    {
        double stored;
        if (val.IsFloat)
            stored = val.AsFloat;
        else if (val.IsInt)
            stored = (double)val.AsInt;
        else
            throw new RuntimeError($"Cannot insert {TypeNameOf(val)} into float[] — expected float.");

        EnsureCapacity(_count + 1);
        Array.Copy(_data, index, _data, index + 1, _count - index);
        _data[index] = stored;
        _count++;
    }

    public override void RemoveAt(int index)
    {
        _count--;
        Array.Copy(_data, index + 1, _data, index, _count - index);
    }

    public override StashTypedArray Clone() => new StashFloatArray(_data, _count);
    public override void Clear() => _count = 0;
    public override StashTypedArray CreateEmpty() => new StashFloatArray(new List<StashValue>());

    private void EnsureCapacity(int min)
    {
        if (_data.Length >= min) return;
        int newCap = Math.Max(_data.Length * 2, min);
        var newData = new double[newCap];
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
