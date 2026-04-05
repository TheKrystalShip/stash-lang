using System;
using System.Runtime.CompilerServices;

namespace Stash.Bytecode;

public readonly struct StashValue
{
    public readonly StashValueTag Tag;
    private readonly long _data;
    private readonly object? _obj;

    private StashValue(StashValueTag tag, long data, object? obj)
    {
        Tag = tag;
        _data = data;
        _obj = obj;
    }

    // Pre-allocated singletons
    public static readonly StashValue Null  = new(StashValueTag.Null,  0, null);
    public static readonly StashValue True  = new(StashValueTag.Bool,  1, null);
    public static readonly StashValue False = new(StashValueTag.Bool,  0, null);
    public static readonly StashValue Zero  = new(StashValueTag.Int,   0, null);
    public static readonly StashValue One   = new(StashValueTag.Int,   1, null);

    // Factory methods
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StashValue FromInt(long value) => new(StashValueTag.Int, value, null);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StashValue FromFloat(double value) => new(StashValueTag.Float, BitConverter.DoubleToInt64Bits(value), null);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StashValue FromBool(bool value) => value ? True : False;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StashValue FromObj(object value) => new(StashValueTag.Obj, 0, value);

    // Inline accessors
    public long AsInt
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _data;
    }

    public double AsFloat
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => BitConverter.Int64BitsToDouble(_data);
    }

    public bool AsBool
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _data != 0;
    }

    public object? AsObj
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _obj;
    }

    // Type check properties
    public bool IsNull    => Tag == StashValueTag.Null;
    public bool IsInt     => Tag == StashValueTag.Int;
    public bool IsFloat   => Tag == StashValueTag.Float;
    public bool IsBool    => Tag == StashValueTag.Bool;
    public bool IsObj     => Tag == StashValueTag.Obj;
    public bool IsNumeric => Tag == StashValueTag.Int || Tag == StashValueTag.Float;

    /// <summary>
    /// Converts this StashValue to a boxed object for the IStashCallable boundary.
    /// </summary>
    public object? ToObject()
    {
        return Tag switch
        {
            StashValueTag.Null  => null,
            StashValueTag.Bool  => _data != 0,
            StashValueTag.Int   => _data,
            StashValueTag.Float => AsFloat,
            StashValueTag.Obj   => _obj,
            _                   => null,
        };
    }

    /// <summary>
    /// Converts a boxed object to a StashValue. Used when receiving results from IStashCallable.
    /// </summary>
    public static StashValue FromObject(object? value)
    {
        return value switch
        {
            null   => Null,
            bool b => FromBool(b),
            long l => FromInt(l),
            double d => FromFloat(d),
            _ => FromObj(value),
        };
    }

    public override string ToString()
    {
        return Tag switch
        {
            StashValueTag.Null  => "null",
            StashValueTag.Bool  => AsBool ? "true" : "false",
            StashValueTag.Int   => AsInt.ToString(),
            StashValueTag.Float => AsFloat.ToString("G"),
            StashValueTag.Obj   => _obj?.ToString() ?? "null",
            _                   => "??",
        };
    }
}
