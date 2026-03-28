namespace Stash.Stdlib;

using System.Collections.Generic;
using Stash.Runtime;
using Stash.Runtime.Types;

/// <summary>
/// Shared argument validation helpers for built-in functions.
/// Each method validates type and returns the extracted value, or throws RuntimeError.
/// </summary>
public static class Args
{
    // --- Guards ---

    private static void GuardIndex(IReadOnlyList<object?> args, int index, string funcName)
    {
        if (index >= args.Count)
            throw new RuntimeError($"'{funcName}' expects at least {index + 1} argument(s), but got {args.Count}.");
    }

    // --- Core type extractors (most common patterns) ---

    public static string String(List<object?> args, int index, string funcName)
    {
        GuardIndex(args, index, funcName);
        if (args[index] is string s) return s;
        throw new RuntimeError($"{Ordinal(index)} argument to '{funcName}' must be a string.");
    }

    public static List<object?> List(List<object?> args, int index, string funcName)
    {
        GuardIndex(args, index, funcName);
        if (args[index] is List<object?> list) return list;
        throw new RuntimeError($"{Ordinal(index)} argument to '{funcName}' must be an array.");
    }

    public static StashDictionary Dict(List<object?> args, int index, string funcName)
    {
        GuardIndex(args, index, funcName);
        if (args[index] is StashDictionary dict) return dict;
        throw new RuntimeError($"{Ordinal(index)} argument to '{funcName}' must be a dictionary.");
    }

    public static IStashCallable Callable(List<object?> args, int index, string funcName)
    {
        GuardIndex(args, index, funcName);
        if (args[index] is IStashCallable fn) return fn;
        throw new RuntimeError($"{Ordinal(index)} argument to '{funcName}' must be a function.");
    }

    public static long Long(List<object?> args, int index, string funcName)
    {
        GuardIndex(args, index, funcName);
        if (args[index] is long l) return l;
        throw new RuntimeError($"{Ordinal(index)} argument to '{funcName}' must be an integer.");
    }

    public static double Double(List<object?> args, int index, string funcName)
    {
        GuardIndex(args, index, funcName);
        if (args[index] is double d) return d;
        throw new RuntimeError($"{Ordinal(index)} argument to '{funcName}' must be a float.");
    }

    public static bool Bool(List<object?> args, int index, string funcName)
    {
        GuardIndex(args, index, funcName);
        if (args[index] is bool b) return b;
        throw new RuntimeError($"{Ordinal(index)} argument to '{funcName}' must be a boolean.");
    }

    public static StashInstance Instance(List<object?> args, int index, string funcName)
    {
        GuardIndex(args, index, funcName);
        if (args[index] is StashInstance inst) return inst;
        throw new RuntimeError($"{Ordinal(index)} argument to '{funcName}' must be a struct instance.");
    }

    /// <summary>
    /// Validates that the argument is a StashInstance with the specified type name.
    /// Used for typed struct validation (e.g., "Process", "SshConnection").
    /// </summary>
    public static StashInstance Instance(List<object?> args, int index, string typeName, string funcName)
    {
        GuardIndex(args, index, funcName);
        if (args[index] is StashInstance inst && inst.TypeName == typeName) return inst;
        throw new RuntimeError($"{Ordinal(index)} argument to '{funcName}' must be a {typeName}.");
    }

    public static StashFuture Future(List<object?> args, int index, string funcName)
    {
        GuardIndex(args, index, funcName);
        if (args[index] is StashFuture future) return future;
        throw new RuntimeError($"{Ordinal(index)} argument to '{funcName}' must be a future.");
    }

    // --- Numeric helpers (consolidates 3 separate ToDouble implementations) ---

    /// <summary>
    /// Accepts both long and double, returning double. Used by math functions.
    /// </summary>
    public static double Numeric(List<object?> args, int index, string funcName)
    {
        GuardIndex(args, index, funcName);
        return args[index] switch
        {
            long l => (double)l,
            double d => d,
            _ => throw new RuntimeError($"{Ordinal(index)} argument to '{funcName}' must be a number.")
        };
    }

    /// <summary>
    /// Checks if the argument is a number (long or double) without extracting.
    /// Returns true for long or double, false otherwise.
    /// </summary>
    public static bool IsNumeric(object? value) => value is long or double;

    // --- Argument count validation ---

    /// <summary>
    /// Validates that the argument count is within the expected range (inclusive).
    /// </summary>
    public static void Count(List<object?> args, int min, int max, string funcName)
    {
        if (args.Count < min || args.Count > max)
        {
            string expected = min == max
                ? $"{min}"
                : $"{min} to {max}";
            throw new RuntimeError($"'{funcName}' expects {expected} arguments, got {args.Count}.");
        }
    }

    /// <summary>
    /// Validates that the argument count is at least the specified minimum.
    /// </summary>
    public static void CountMin(List<object?> args, int min, string funcName)
    {
        if (args.Count < min)
        {
            throw new RuntimeError($"'{funcName}' expects at least {min} argument{(min == 1 ? "" : "s")}, got {args.Count}.");
        }
    }

    // --- Null guard ---

    /// <summary>
    /// Returns the value if non-null, throws if null.
    /// </summary>
    public static object NotNull(List<object?> args, int index, string funcName)
    {
        GuardIndex(args, index, funcName);
        return args[index] ?? throw new RuntimeError($"{Ordinal(index)} argument to '{funcName}' must not be null.");
    }

    // --- Ordinal helper ---

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
