using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace Stash.Common;

/// <summary>
/// A stack-allocated string builder that avoids heap allocations for the common case
/// (strings under the initial buffer size). Overflows to <see cref="ArrayPool{T}.Shared"/>
/// when needed. Must be disposed to return rented arrays.
/// </summary>
public ref struct ValueStringBuilder
{
    private char[]? _arrayToReturnToPool;
    private Span<char> _chars;
    private int _pos;

    public ValueStringBuilder(Span<char> initialBuffer)
    {
        _arrayToReturnToPool = null;
        _chars = initialBuffer;
        _pos = 0;
    }

    public int Length => _pos;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Append(char c)
    {
        int pos = _pos;
        Span<char> chars = _chars;
        if ((uint)pos < (uint)chars.Length)
        {
            chars[pos] = c;
            _pos = pos + 1;
        }
        else
        {
            GrowAndAppend(c);
        }
    }

    public void Append(string? s)
    {
        if (s is null) return;

        int pos = _pos;
        if (pos > _chars.Length - s.Length)
        {
            Grow(s.Length);
        }

        s.AsSpan().CopyTo(_chars[pos..]);
        _pos += s.Length;
    }

    public void Append(ReadOnlySpan<char> value)
    {
        int pos = _pos;
        if (pos > _chars.Length - value.Length)
        {
            Grow(value.Length);
        }

        value.CopyTo(_chars[pos..]);
        _pos += value.Length;
    }

    public void Clear()
    {
        _pos = 0;
    }

    public override string ToString()
    {
        return _chars[.._pos].ToString();
    }

    public ReadOnlySpan<char> AsSpan() => _chars[.._pos];

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void GrowAndAppend(char c)
    {
        Grow(1);
        Append(c);
    }

    private void Grow(int additionalCapacityBeyondPos)
    {
        int newCapacity = (int)Math.Max(
            (uint)(_pos + additionalCapacityBeyondPos),
            Math.Min((uint)_chars.Length * 2, 0x3FFFFFDF));

        if (newCapacity < _pos + additionalCapacityBeyondPos)
            newCapacity = _pos + additionalCapacityBeyondPos;

        char[] poolArray = ArrayPool<char>.Shared.Rent(newCapacity);
        _chars[.._pos].CopyTo(poolArray);

        char[]? toReturn = _arrayToReturnToPool;
        _arrayToReturnToPool = poolArray;
        _chars = poolArray;

        if (toReturn is not null)
        {
            ArrayPool<char>.Shared.Return(toReturn);
        }
    }

    public void Dispose()
    {
        char[]? toReturn = _arrayToReturnToPool;
        _arrayToReturnToPool = null;
        if (toReturn is not null)
        {
            ArrayPool<char>.Shared.Return(toReturn);
        }
    }
}
