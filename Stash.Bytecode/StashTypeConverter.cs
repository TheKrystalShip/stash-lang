namespace Stash.Bytecode;

using System;
using System.Collections.Generic;
using Stash.Runtime;
using Stash.Runtime.Types;

/// <summary>
/// Helper methods for converting between Stash runtime values and CLR types.
/// </summary>
public static class StashTypeConverter
{
    /// <summary>Converts a Stash runtime value to its string representation.</summary>
    public static string Stringify(object? value) => RuntimeValues.Stringify(value);

    /// <summary>
    /// Converts a Stash dictionary to a .NET dictionary.
    /// Keys are converted to strings via <see cref="Stringify"/>.
    /// </summary>
    public static Dictionary<string, object?> ToDictionary(object? value)
    {
        if (value is not StashDictionary dict)
        {
            throw new ArgumentException($"Expected a Stash dictionary, got {value?.GetType().Name ?? "null"}.");
        }

        var result = new Dictionary<string, object?>();
        foreach (var entry in dict.RawEntries())
        {
            result[RuntimeValues.Stringify(entry.Key)] = entry.Value;
        }
        return result;
    }

    /// <summary>Converts a Stash struct instance to a .NET dictionary of field name → value.</summary>
    public static Dictionary<string, object?> ToFieldDictionary(object? value)
    {
        if (value is not StashInstance instance)
        {
            throw new ArgumentException($"Expected a Stash struct instance, got {value?.GetType().Name ?? "null"}.");
        }

        return new Dictionary<string, object?>(instance.GetFields());
    }

    /// <summary>Converts a Stash array to a .NET list.</summary>
    public static List<object?> ToList(object? value)
    {
        if (value is not List<object?> list)
        {
            throw new ArgumentException($"Expected a Stash array, got {value?.GetType().Name ?? "null"}.");
        }

        return new List<object?>(list);
    }

    /// <summary>Creates a Stash dictionary from a .NET dictionary.</summary>
    public static StashDictionary CreateDictionary(IDictionary<string, object?> values)
    {
        var dict = new StashDictionary();
        foreach (var kvp in values)
        {
            dict.Set(kvp.Key, kvp.Value);
        }
        return dict;
    }
}
