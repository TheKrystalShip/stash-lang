namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Abstractions;
using Stash.Stdlib.Models;

/// <summary>
/// Registers the <c>dict</c> namespace built-in functions for dictionary manipulation.
/// </summary>
[StashNamespace]
public static partial class DictBuiltIns
{
    /// <summary>Creates and returns a new empty dictionary.</summary>
    /// <returns>An empty dictionary</returns>
    [StashFn(Raw = true, ReturnType = "dict")]
    private static StashValue New(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        return StashValue.FromObj(new StashDictionary());
    }

    /// <summary>Returns the value for key, or null if not found. If a default is provided it is returned instead of null for missing keys.</summary>
    /// <param name="dict">The dictionary</param>
    /// <param name="key">The key</param>
    /// <param name="default">Optional default value when key is missing</param>
    /// <returns>The value, default, or null</returns>
    [StashFn(Raw = true, ReturnType = "any")]
    private static StashValue Get(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        if (args.Length < 2 || args.Length > 3)
            throw new RuntimeError("'dict.get' requires 2 or 3 arguments.");
        var d = SvArgs.Dict(args, 0, "dict.get");
        var key = args[1].ToObject() ?? throw new RuntimeError("Dictionary key cannot be null.", errorType: StashErrorTypes.TypeError);
        var result = d.Get(key);
        if (result.IsNull && args.Length == 3)
            return args[2];
        return result;
    }

    /// <summary>Sets a key-value pair in the dictionary. Modifies in place.</summary>
    /// <param name="dict">The dictionary</param>
    /// <param name="key">The key</param>
    /// <param name="value">The value to set</param>
    [StashFn(Raw = true, ReturnType = "void")]
    private static StashValue Set(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var d = SvArgs.Dict(args, 0, "dict.set");
        var key = args[1].ToObject() ?? throw new RuntimeError("Dictionary key cannot be null.", errorType: StashErrorTypes.TypeError);
        d.Set(key, args[2]);
        return StashValue.Null;
    }

    /// <summary>Returns true if the dictionary contains the key.</summary>
    /// <param name="dict">The dictionary</param>
    /// <param name="key">The key to check</param>
    /// <returns>true if the key exists</returns>
    [StashFn(Raw = true, ReturnType = "bool")]
    private static StashValue Has(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var d = SvArgs.Dict(args, 0, "dict.has");
        var key = args[1].ToObject() ?? throw new RuntimeError("Dictionary key cannot be null.", errorType: StashErrorTypes.TypeError);
        return StashValue.FromBool(d.Has(key));
    }

    /// <summary>Removes a key from the dictionary. Returns true if it existed.</summary>
    /// <param name="dict">The dictionary</param>
    /// <param name="key">The key to remove</param>
    /// <returns>true if the key was removed</returns>
    [StashFn(Raw = true, ReturnType = "bool")]
    private static StashValue Remove(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var d = SvArgs.Dict(args, 0, "dict.remove");
        var key = args[1].ToObject() ?? throw new RuntimeError("Dictionary key cannot be null.", errorType: StashErrorTypes.TypeError);
        return StashValue.FromBool(d.Remove(key));
    }

    /// <summary>Removes all entries from the dictionary.</summary>
    /// <param name="dict">The dictionary to clear</param>
    [StashFn(Raw = true, ReturnType = "void")]
    private static StashValue Clear(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        SvArgs.Dict(args, 0, "dict.clear").Clear();
        return StashValue.Null;
    }

    /// <summary>Returns an array of all keys in the dictionary.</summary>
    /// <param name="dict">The dictionary</param>
    /// <returns>Array of keys</returns>
    [StashFn(Raw = true, ReturnType = "array")]
    private static StashValue Keys(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var d = SvArgs.Dict(args, 0, "dict.keys");
        return StashValue.FromObj(d.Keys());
    }

    /// <summary>Returns an array of all values in the dictionary.</summary>
    /// <param name="dict">The dictionary</param>
    /// <returns>Array of values</returns>
    [StashFn(Raw = true, ReturnType = "array")]
    private static StashValue Values(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var d = SvArgs.Dict(args, 0, "dict.values");
        return StashValue.FromObj(d.Values());
    }

    /// <summary>Returns the number of entries in the dictionary.</summary>
    /// <param name="dict">The dictionary</param>
    /// <returns>Entry count</returns>
    [StashFn(Raw = true, ReturnType = "int")]
    private static StashValue Size(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var d = SvArgs.Dict(args, 0, "dict.size");
        return StashValue.FromInt((long)d.Count);
    }

    /// <summary>Returns an array of [key, value] pairs for each entry.</summary>
    /// <param name="dict">The dictionary</param>
    /// <returns>Array of [key, value] pairs</returns>
    [StashFn(Raw = true, ReturnType = "array")]
    private static StashValue Pairs(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var d = SvArgs.Dict(args, 0, "dict.pairs");
        return StashValue.FromObj(d.Pairs());
    }

    /// <summary>Calls fn(key, value) for each entry. Returns null.</summary>
    /// <param name="dict">The dictionary</param>
    /// <param name="fn">The callback function</param>
    [StashFn(Raw = true, ReturnType = "void")]
    private static StashValue ForEach(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var d = SvArgs.Dict(args, 0, "dict.forEach");
        var fn = SvArgs.Callable(args, 1, "dict.forEach");

        foreach (var entry in d.RawEntries())
        {
            ctx.InvokeCallbackDirect(fn, new StashValue[] { StashValue.FromObject(entry.Key), entry.Value });
        }
        return StashValue.Null;
    }

    /// <summary>Returns a new dictionary merging dict1 and dict2. dict2 keys take precedence. When deep is true, nested dicts are merged recursively.</summary>
    /// <param name="dict1">The base dictionary</param>
    /// <param name="dict2">The overriding dictionary</param>
    /// <param name="deep">Optional boolean; when true performs a deep recursive merge</param>
    /// <returns>Merged dictionary</returns>
    [StashFn(Raw = true, ReturnType = "dict")]
    private static StashValue Merge(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        if (args.Length < 2 || args.Length > 3)
            throw new RuntimeError("'dict.merge' requires 2 or 3 arguments.");
        var d1 = SvArgs.Dict(args, 0, "dict.merge");
        var d2 = SvArgs.Dict(args, 1, "dict.merge");
        bool deep = args.Length == 3 && SvArgs.Bool(args, 2, "dict.merge");
        return StashValue.FromObj(MergeInternal(d1, d2, deep));
    }

    /// <summary>Returns a new dictionary with each value transformed by fn(key, value).</summary>
    /// <param name="dict">The dictionary</param>
    /// <param name="fn">The mapping function</param>
    /// <returns>Transformed dictionary</returns>
    [StashFn(Raw = true, ReturnType = "dict")]
    private static StashValue Map(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var d = SvArgs.Dict(args, 0, "dict.map");
        var fn = SvArgs.Callable(args, 1, "dict.map");

        var result = new StashDictionary();
        foreach (var entry in d.RawEntries())
        {
            result.Set(entry.Key, ctx.InvokeCallbackDirect(fn, new StashValue[] { StashValue.FromObject(entry.Key), entry.Value }));
        }
        return StashValue.FromObj(result);
    }

    /// <summary>Returns a new dictionary with only entries where fn(key, value) is truthy.</summary>
    /// <param name="dict">The dictionary</param>
    /// <param name="fn">The filter function</param>
    /// <returns>Filtered dictionary</returns>
    [StashFn(Raw = true, ReturnType = "dict")]
    private static StashValue Filter(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var d = SvArgs.Dict(args, 0, "dict.filter");
        var fn = SvArgs.Callable(args, 1, "dict.filter");

        var result = new StashDictionary();
        foreach (var entry in d.RawEntries())
        {
            var keep = ctx.InvokeCallbackDirect(fn, new StashValue[] { StashValue.FromObject(entry.Key), entry.Value });
            if (RuntimeValues.IsTruthy(keep.ToObject()))
            {
                result.Set(entry.Key, entry.Value);
            }
        }
        return StashValue.FromObj(result);
    }

    /// <summary>Constructs a dictionary from an array of [key, value] pairs.</summary>
    /// <param name="pairs">Array of [key, value] pairs</param>
    /// <returns>New dictionary</returns>
    [StashFn(Raw = true, ReturnType = "dict")]
    private static StashValue FromPairs(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var pairs = SvArgs.StashList(args, 0, "dict.fromPairs");
        var result = new StashDictionary();
        foreach (var pairSv in pairs)
        {
            object? key;
            StashValue val;
            var pairObj = pairSv.AsObj;
            if (pairObj is List<StashValue> svp && svp.Count == 2)
            {
                key = svp[0].ToObject();
                val = svp[1];
            }
            else
            {
                throw new RuntimeError("'dict.fromPairs' requires each element to be a [key, value] pair.", errorType: StashErrorTypes.TypeError);
            }
            if (key is null) throw new RuntimeError("Dictionary key cannot be null in 'dict.fromPairs'.", errorType: StashErrorTypes.TypeError);
            result.Set(key, val);
        }
        return StashValue.FromObj(result);
    }

    /// <summary>Returns a new dictionary with only the specified keys.</summary>
    /// <param name="dict">The source dictionary</param>
    /// <param name="keys">Array of keys to include</param>
    /// <returns>New dictionary with picked keys</returns>
    [StashFn(Raw = true, ReturnType = "dict")]
    private static StashValue Pick(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var d = SvArgs.Dict(args, 0, "dict.pick");
        var keys = SvArgs.StashList(args, 1, "dict.pick");
        var result = new StashDictionary();
        foreach (var keySv in keys)
        {
            var key = keySv.ToObject();
            if (key != null && d.Has(key))
                result.Set(key, d.Get(key));
        }
        return StashValue.FromObj(result);
    }

    /// <summary>Returns a new dictionary excluding the specified keys.</summary>
    /// <param name="dict">The source dictionary</param>
    /// <param name="keys">Array of keys to omit</param>
    /// <returns>New dictionary without omitted keys</returns>
    [StashFn(Raw = true, ReturnType = "dict")]
    private static StashValue Omit(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var d = SvArgs.Dict(args, 0, "dict.omit");
        var keysToOmit = SvArgs.StashList(args, 1, "dict.omit");
        var result = new StashDictionary();
        foreach (var entry in d.RawEntries())
        {
            bool omit = false;
            foreach (var keySv in keysToOmit)
            {
                var k = keySv.ToObject();
                if (k != null && RuntimeValues.IsEqual(entry.Key, k))
                {
                    omit = true;
                    break;
                }
            }
            if (!omit) result.Set(entry.Key, entry.Value);
        }
        return StashValue.FromObj(result);
    }

    /// <summary>Returns a new dictionary merging defaults with dict. Dict values take precedence.</summary>
    /// <param name="dict">The priority dictionary</param>
    /// <param name="defaults">The defaults dictionary</param>
    /// <returns>Merged dictionary</returns>
    [StashFn(Raw = true, ReturnType = "dict")]
    private static StashValue Defaults(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var d = SvArgs.Dict(args, 0, "dict.defaults");
        var defaults = SvArgs.Dict(args, 1, "dict.defaults");
        var result = new StashDictionary();
        foreach (var entry in defaults.RawEntries())
            result.Set(entry.Key, entry.Value);
        foreach (var entry in d.RawEntries())
            result.Set(entry.Key, entry.Value);
        return StashValue.FromObj(result);
    }

    /// <summary>Returns true if fn(key, value) returns truthy for at least one entry.</summary>
    /// <param name="dict">The dictionary</param>
    /// <param name="fn">The predicate function</param>
    /// <returns>true if any entry matches</returns>
    [StashFn(Raw = true, ReturnType = "bool")]
    private static StashValue Any(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var d = SvArgs.Dict(args, 0, "dict.any");
        var fn = SvArgs.Callable(args, 1, "dict.any");

        foreach (var entry in d.RawEntries())
        {
            if (RuntimeValues.IsTruthy(ctx.InvokeCallbackDirect(fn, new StashValue[] { StashValue.FromObject(entry.Key), entry.Value }).ToObject()))
            {
                return StashValue.True;
            }
        }
        return StashValue.False;
    }

    /// <summary>Returns true if fn(key, value) returns truthy for every entry.</summary>
    /// <param name="dict">The dictionary</param>
    /// <param name="fn">The predicate function</param>
    /// <returns>true if all entries match</returns>
    [StashFn(Raw = true, ReturnType = "bool")]
    private static StashValue Every(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var d = SvArgs.Dict(args, 0, "dict.every");
        var fn = SvArgs.Callable(args, 1, "dict.every");

        foreach (var entry in d.RawEntries())
        {
            if (!RuntimeValues.IsTruthy(ctx.InvokeCallbackDirect(fn, new StashValue[] { StashValue.FromObject(entry.Key), entry.Value }).ToObject()))
            {
                return StashValue.False;
            }
        }
        return StashValue.True;
    }

    /// <summary>Returns the first [key, value] pair for which fn(key, value) is truthy, or null.</summary>
    /// <param name="dict">The dictionary</param>
    /// <param name="fn">The predicate function</param>
    /// <returns>[key, value] pair or null</returns>
    [StashFn(Raw = true, ReturnType = "array")]
    private static StashValue Find(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        var d = SvArgs.Dict(args, 0, "dict.find");
        var fn = SvArgs.Callable(args, 1, "dict.find");

        foreach (var entry in d.RawEntries())
        {
            if (RuntimeValues.IsTruthy(ctx.InvokeCallbackDirect(fn, new StashValue[] { StashValue.FromObject(entry.Key), entry.Value }).ToObject()))
            {
                return StashValue.FromObj(new List<StashValue>
                {
                    StashValue.FromObject(entry.Key),
                    entry.Value,
                });
            }
        }
        return StashValue.Null;
    }

    private static StashDictionary MergeInternal(StashDictionary d1, StashDictionary d2, bool deep)
    {
        var result = new StashDictionary();
        foreach (var entry in d1.RawEntries())
            result.Set(entry.Key, entry.Value);
        foreach (var entry in d2.RawEntries())
        {
            if (deep && entry.Value.IsObj && entry.Value.AsObj is StashDictionary nestedD2)
            {
                var existing = result.Get(entry.Key);
                if (existing.IsObj && existing.AsObj is StashDictionary nestedD1)
                {
                    result.Set(entry.Key, StashValue.FromObj(MergeInternal(nestedD1, nestedD2, deep: true)));
                    continue;
                }
            }
            result.Set(entry.Key, entry.Value);
        }
        return result;
    }
}
