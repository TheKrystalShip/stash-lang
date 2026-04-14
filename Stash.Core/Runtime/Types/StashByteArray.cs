namespace Stash.Runtime.Types;

using System;
using System.Collections.Generic;
using System.Text;
using Stash.Runtime;

public sealed class StashByteArray : StashTypedArray
{
    private byte[] _data;
    private int _count;
    private const int DefaultCapacity = 4;

    public override string ElementTypeName => "byte";
    public override int Count => _count;
    public override int Capacity => _data.Length;

    // From source list (validates all elements; int→byte narrowing allowed with range check)
    public StashByteArray(List<StashValue> source)
    {
        _data = new byte[Math.Max(source.Count, DefaultCapacity)];
        for (int i = 0; i < source.Count; i++)
        {
            _data[i] = ExtractByte(source[i], i);
        }
        _count = source.Count;
    }

    // Zero-initialized with capacity
    public StashByteArray(int capacity)
    {
        _data = new byte[Math.Max(capacity, DefaultCapacity)];
        _count = capacity;  // all zeros
    }

    // From raw byte array (no copy — caller transfers ownership)
    public StashByteArray(byte[] data)
    {
        _data = data;
        _count = data.Length;
    }

    // Internal copy constructor
    private StashByteArray(byte[] data, int count)
    {
        _data = new byte[data.Length];
        Array.Copy(data, _data, count);
        _count = count;
    }

    public override StashValue Get(int index)
    {
        return StashValue.FromByte(_data[index]);
    }

    public override void Set(int index, StashValue val)
    {
        _data[index] = ExtractByteForSet(val, index);
    }

    public override void Add(StashValue val)
    {
        byte b = ExtractByteForAdd(val);
        EnsureCapacity(_count + 1);
        _data[_count++] = b;
    }

    public override StashValue RemoveLast()
    {
        if (_count == 0) throw new RuntimeError("Cannot pop from empty byte[].");
        return StashValue.FromByte(_data[--_count]);
    }

    public override void Insert(int index, StashValue val)
    {
        byte b = ExtractByteForInsert(val);
        EnsureCapacity(_count + 1);
        Array.Copy(_data, index, _data, index + 1, _count - index);
        _data[index] = b;
        _count++;
    }

    public override void RemoveAt(int index)
    {
        _count--;
        Array.Copy(_data, index + 1, _data, index, _count - index);
    }

    public override StashTypedArray Clone() => new StashByteArray(_data, _count);
    public override void Clear() => _count = 0;
    public override StashTypedArray CreateEmpty() => new StashByteArray(new List<StashValue>());

    /// <summary>
    /// Returns a ReadOnlySpan over the active elements. Used by stdlib functions for zero-copy I/O.
    /// </summary>
    public ReadOnlySpan<byte> AsSpan() => _data.AsSpan(0, _count);

    /// <summary>
    /// Returns the raw backing array and active count. Used by stdlib for bulk operations.
    /// </summary>
    public byte[] GetBackingArray(out int count)
    {
        count = _count;
        return _data;
    }

    // --- Hex-based Stringify override ---

    public new string Stringify()
    {
        var sb = new StringBuilder();
        sb.Append("byte[");
        if (_count > 64)
        {
            // Truncate for large arrays
            for (int i = 0; i < 16; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append("0x");
                sb.Append(_data[i].ToString("x2"));
            }
            sb.Append($", ... ({_count} bytes)");
        }
        else
        {
            for (int i = 0; i < _count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append("0x");
                sb.Append(_data[i].ToString("x2"));
            }
        }
        sb.Append(']');
        return sb.ToString();
    }

    // --- Byte extraction with int→byte narrowing ---

    private static byte ExtractByte(StashValue val, int elementIndex)
    {
        if (val.IsByte) return val.AsByte;
        if (val.IsInt)
        {
            long n = val.AsInt;
            if (n < 0 || n > 255)
                throw new RuntimeError($"Cannot create byte[] — element at index {elementIndex} has value {n} which is out of byte range [0, 255].");
            return (byte)n;
        }
        string actual = val.IsNull ? "null" : RuntimeValues.Stringify(val.ToObject());
        throw new RuntimeError($"Cannot create byte[] — element at index {elementIndex} is {TypeNameOf(val)} {actual} — expected byte or int (0-255).");
    }

    private static byte ExtractByteForSet(StashValue val, int index)
    {
        if (val.IsByte) return val.AsByte;
        if (val.IsInt)
        {
            long n = val.AsInt;
            if (n < 0 || n > 255)
                throw new RuntimeError($"Cannot assign value {n} to element of byte[] at index {index} — out of byte range [0, 255].");
            return (byte)n;
        }
        throw new RuntimeError($"Cannot assign {TypeNameOf(val)} to element of byte[] at index {index} — expected byte or int (0-255).");
    }

    private static byte ExtractByteForAdd(StashValue val)
    {
        if (val.IsByte) return val.AsByte;
        if (val.IsInt)
        {
            long n = val.AsInt;
            if (n < 0 || n > 255)
                throw new RuntimeError($"Cannot add value {n} to byte[] — out of byte range [0, 255].");
            return (byte)n;
        }
        throw new RuntimeError($"Cannot add {TypeNameOf(val)} to byte[] — expected byte or int (0-255).");
    }

    private static byte ExtractByteForInsert(StashValue val)
    {
        if (val.IsByte) return val.AsByte;
        if (val.IsInt)
        {
            long n = val.AsInt;
            if (n < 0 || n > 255)
                throw new RuntimeError($"Cannot insert value {n} into byte[] — out of byte range [0, 255].");
            return (byte)n;
        }
        throw new RuntimeError($"Cannot insert {TypeNameOf(val)} into byte[] — expected byte or int (0-255).");
    }

    private static string TypeNameOf(StashValue v)
    {
        if (v.IsNull) return "null";
        if (v.IsInt) return "int";
        if (v.IsFloat) return "float";
        if (v.IsBool) return "bool";
        if (v.IsByte) return "byte";
        if (v.IsObj && v.AsObj is string) return "string";
        return v.ToObject()?.GetType().Name ?? "null";
    }

    private void EnsureCapacity(int min)
    {
        if (_data.Length >= min) return;
        int newCap = Math.Max(_data.Length * 2, min);
        var newData = new byte[newCap];
        Array.Copy(_data, newData, _count);
        _data = newData;
    }
}
