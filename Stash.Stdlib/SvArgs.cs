namespace Stash.Stdlib;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Stash.Runtime;
using Stash.Runtime.Types;

/// <summary>
/// StashValue-native argument extraction helpers for DirectHandler built-ins.
/// Mirrors <see cref="Args"/> but operates on ReadOnlySpan&lt;StashValue&gt; — zero boxing.
/// </summary>
public static class SvArgs
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Long(ReadOnlySpan<StashValue> args, int index, string funcName)
    {
        StashValue v = args[index];
        if (v.IsInt) return v.AsInt;
        throw new RuntimeError($"{Ordinal(index)} argument to '{funcName}' must be an integer.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Double(ReadOnlySpan<StashValue> args, int index, string funcName)
    {
        StashValue v = args[index];
        if (v.IsFloat) return v.AsFloat;
        throw new RuntimeError($"{Ordinal(index)} argument to '{funcName}' must be a float.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string String(ReadOnlySpan<StashValue> args, int index, string funcName)
    {
        StashValue v = args[index];
        if (v.IsObj && v.AsObj is string s) return s;
        throw new RuntimeError($"{Ordinal(index)} argument to '{funcName}' must be a string.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Bool(ReadOnlySpan<StashValue> args, int index, string funcName)
    {
        StashValue v = args[index];
        if (v.IsBool) return v.AsBool;
        throw new RuntimeError($"{Ordinal(index)} argument to '{funcName}' must be a boolean.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static List<StashValue> StashList(ReadOnlySpan<StashValue> args, int index, string funcName)
    {
        StashValue v = args[index];
        if (v.IsObj && v.AsObj is List<StashValue> l) return l;
        throw new RuntimeError($"{Ordinal(index)} argument to '{funcName}' must be an array.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StashDictionary Dict(ReadOnlySpan<StashValue> args, int index, string funcName)
    {
        StashValue v = args[index];
        if (v.IsObj && v.AsObj is StashDictionary d) return d;
        throw new RuntimeError($"{Ordinal(index)} argument to '{funcName}' must be a dictionary.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IStashCallable Callable(ReadOnlySpan<StashValue> args, int index, string funcName)
    {
        StashValue v = args[index];
        if (v.IsObj && v.AsObj is IStashCallable fn) return fn;
        throw new RuntimeError($"{Ordinal(index)} argument to '{funcName}' must be a function.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StashInstance Instance(ReadOnlySpan<StashValue> args, int index, string funcName)
    {
        StashValue v = args[index];
        if (v.IsObj && v.AsObj is StashInstance inst) return inst;
        throw new RuntimeError($"{Ordinal(index)} argument to '{funcName}' must be a struct instance.");
    }

    public static StashInstance Instance(ReadOnlySpan<StashValue> args, int index, string typeName, string funcName)
    {
        StashValue v = args[index];
        if (v.IsObj && v.AsObj is StashInstance inst && inst.TypeName == typeName) return inst;
        throw new RuntimeError($"{Ordinal(index)} argument to '{funcName}' must be a {typeName}.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StashIpAddress IpAddress(ReadOnlySpan<StashValue> args, int index, string funcName)
    {
        StashValue v = args[index];
        if (v.IsObj && v.AsObj is StashIpAddress ip) return ip;
        throw new RuntimeError($"{Ordinal(index)} argument to '{funcName}' must be an IP address.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StashFuture Future(ReadOnlySpan<StashValue> args, int index, string funcName)
    {
        StashValue v = args[index];
        if (v.IsObj && v.AsObj is StashFuture f) return f;
        throw new RuntimeError($"{Ordinal(index)} argument to '{funcName}' must be a future.");
    }

    /// <summary>
    /// Accepts both long and double, returning double. Used by math functions.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Numeric(ReadOnlySpan<StashValue> args, int index, string funcName)
    {
        StashValue v = args[index];
        if (v.IsFloat) return v.AsFloat;
        if (v.IsInt) return (double)v.AsInt;
        throw new RuntimeError($"{Ordinal(index)} argument to '{funcName}' must be a number.");
    }

    public static object NotNull(ReadOnlySpan<StashValue> args, int index, string funcName)
    {
        StashValue v = args[index];
        if (v.IsNull)
            throw new RuntimeError($"{Ordinal(index)} argument to '{funcName}' must not be null.");
        return v.ToObject()!;
    }

    public static StashEnumValue EnumValue(ReadOnlySpan<StashValue> args, int index, string typeName, string funcName)
    {
        StashValue v = args[index];
        if (v.IsObj && v.AsObj is StashEnumValue value && value.TypeName == typeName) return value;
        throw new RuntimeError($"{Ordinal(index)} argument to '{funcName}' must be a {typeName} enum value.");
    }

    /// <summary>
    /// Returns the raw StashValue at the given index — for functions that accept any type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StashValue Any(ReadOnlySpan<StashValue> args, int index)
    {
        return args[index];
    }

    private static string Ordinal(int index)
    {
        return index switch
        {
            0 => "First",
            1 => "Second",
            2 => "Third",
            3 => "Fourth",
            4 => "Fifth",
            _ => ((index + 1) % 100 is 11 or 12 or 13)
                ? $"{index + 1}th"
                : ((index + 1) % 10) switch { 1 => $"{index + 1}st", 2 => $"{index + 1}nd", 3 => $"{index + 1}rd", _ => $"{index + 1}th" }
        };
    }
}
