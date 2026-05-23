namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Abstractions;
using Stash.Stdlib.Models;
using Stash.Runtime.Errors;

/// <summary>
/// Registers the <c>dict</c> namespace built-in functions for dictionary manipulation.
/// </summary>
[StashNamespace]
public static partial class DictBuiltIns
{
    /// <summary>Creates and returns a new empty dictionary.</summary>
    /// <returns>An empty dictionary</returns>
    [StashFn(ReturnType = "dict")]
    private static StashDictionary New()
    {
        return new StashDictionary();
    }

    /// <summary>Returns the value for key, or null if not found. If a default is provided it is returned instead of null for missing keys.</summary>
    /// <param name="dict">The dictionary</param>
    /// <param name="key">The key</param>
    /// <param name="rest">Optional default value when key is missing</param>
    /// <exception cref="TypeError">if `key` is null</exception>
    /// <returns>The value, default, or null</returns>
    [StashFn(ReturnType = "any")]
    private static StashValue Get(StashDictionary dict, StashValue key, params StashValue[] rest)
    {
        var keyObj = key.ToObject() ?? throw new TypeError("Dictionary key cannot be null.");
        var result = dict.Get(keyObj);
        if (result.IsNull && rest.Length > 0)
            return rest[0];
        return result;
    }

    /// <summary>Sets a key-value pair in the dictionary. Modifies in place.</summary>
    /// <param name="dict">The dictionary</param>
    /// <param name="key">The key</param>
    /// <param name="value">The value to set</param>
    /// <exception cref="RuntimeError">if `dict` is a read-only dictionary returned by a namespace member</exception>
    /// <exception cref="TypeError">if `key` is null</exception>
    [StashFn(ReturnType = "void")]
    private static void Set(StashDictionary dict, StashValue key, StashValue value)
    {
        if (dict.IsFrozen)
            throw new ReadOnlyError("Cannot mutate a read-only dict returned by a namespace member.");
        var keyObj = key.ToObject() ?? throw new TypeError("Dictionary key cannot be null.");
        dict.Set(keyObj, value);
    }

    /// <summary>Returns true if the dictionary contains the key.</summary>
    /// <param name="dict">The dictionary</param>
    /// <param name="key">The key to check</param>
    /// <exception cref="TypeError">if `key` is null</exception>
    /// <returns>true if the key exists</returns>
    [StashFn(ReturnType = "bool")]
    private static bool Has(StashDictionary dict, StashValue key)
    {
        var keyObj = key.ToObject() ?? throw new TypeError("Dictionary key cannot be null.");
        return dict.Has(keyObj);
    }

    /// <summary>Removes a key from the dictionary. Returns true if it existed.</summary>
    /// <param name="dict">The dictionary</param>
    /// <param name="key">The key to remove</param>
    /// <exception cref="RuntimeError">if `dict` is a read-only dictionary returned by a namespace member</exception>
    /// <exception cref="TypeError">if `key` is null</exception>
    /// <returns>true if the key was removed</returns>
    [StashFn(ReturnType = "bool")]
    private static bool Remove(StashDictionary dict, StashValue key)
    {
        if (dict.IsFrozen)
            throw new ReadOnlyError("Cannot mutate a read-only dict returned by a namespace member.");
        var keyObj = key.ToObject() ?? throw new TypeError("Dictionary key cannot be null.");
        return dict.Remove(keyObj);
    }

    /// <summary>Removes all entries from the dictionary.</summary>
    /// <param name="dict">The dictionary to clear</param>
    /// <exception cref="RuntimeError">if `dict` is a read-only dictionary returned by a namespace member</exception>
    [StashFn(ReturnType = "void")]
    private static void Clear(StashDictionary dict)
    {
        if (dict.IsFrozen)
            throw new ReadOnlyError("Cannot mutate a read-only dict returned by a namespace member.");
        dict.Clear();
    }

    /// <summary>Returns an array of all keys in the dictionary.</summary>
    /// <param name="dict">The dictionary</param>
    /// <returns>Array of keys</returns>
    [StashFn(ReturnType = "array")]
    private static List<StashValue> Keys(StashDictionary dict)
    {
        return dict.Keys();
    }

    /// <summary>Returns an array of all values in the dictionary.</summary>
    /// <param name="dict">The dictionary</param>
    /// <returns>Array of values</returns>
    [StashFn(ReturnType = "array")]
    private static List<StashValue> Values(StashDictionary dict)
    {
        return dict.Values();
    }

    /// <summary>Returns the number of entries in the dictionary.</summary>
    /// <param name="dict">The dictionary</param>
    /// <returns>Entry count</returns>
    [StashFn(ReturnType = "int")]
    private static long Size(StashDictionary dict)
    {
        return (long)dict.Count;
    }

    /// <summary>Returns an array of [key, value] pairs for each entry.</summary>
    /// <param name="dict">The dictionary</param>
    /// <returns>Array of [key, value] pairs</returns>
    [StashFn(ReturnType = "array")]
    private static List<StashValue> Pairs(StashDictionary dict)
    {
        return dict.Pairs();
    }

    /// <summary>Calls fn(key, value) for each entry. Returns null.</summary>
    /// <param name="dict">The dictionary</param>
    /// <param name="fn">The callback function</param>
    [StashFn(ReturnType = "void")]
    private static void ForEach(IInterpreterContext ctx, StashDictionary dict, IStashCallable fn)
    {
        foreach (var entry in dict.RawEntries())
        {
            ctx.InvokeCallbackDirect(fn, new StashValue[] { StashValue.FromObject(entry.Key), entry.Value });
        }
    }

    /// <summary>Returns a new dictionary merging dict1 and dict2. dict2 keys take precedence. When deep is true, nested dicts are merged recursively.</summary>
    /// <param name="dict1">The base dictionary</param>
    /// <param name="dict2">The overriding dictionary</param>
    /// <param name="rest">Optional boolean; when true performs a deep recursive merge</param>
    /// <returns>Merged dictionary</returns>
    [StashFn(ReturnType = "dict")]
    private static StashDictionary Merge(StashDictionary dict1, StashDictionary dict2, params StashValue[] rest)
    {
        bool deep = rest.Length > 0 && rest[0].IsBool && rest[0].AsBool;
        return MergeInternal(dict1, dict2, deep);
    }

    /// <summary>Returns a new dictionary with each value transformed by fn(key, value).</summary>
    /// <param name="dict">The dictionary</param>
    /// <param name="fn">The mapping function</param>
    /// <returns>Transformed dictionary</returns>
    [StashFn(ReturnType = "dict")]
    private static StashDictionary Map(IInterpreterContext ctx, StashDictionary dict, IStashCallable fn)
    {
        var result = new StashDictionary();
        foreach (var entry in dict.RawEntries())
        {
            result.Set(entry.Key, ctx.InvokeCallbackDirect(fn, new StashValue[] { StashValue.FromObject(entry.Key), entry.Value }));
        }
        return result;
    }

    /// <summary>Returns a new dictionary with only entries where fn(key, value) is truthy.</summary>
    /// <param name="dict">The dictionary</param>
    /// <param name="fn">The filter function</param>
    /// <returns>Filtered dictionary</returns>
    [StashFn(ReturnType = "dict")]
    private static StashDictionary Filter(IInterpreterContext ctx, StashDictionary dict, IStashCallable fn)
    {
        var result = new StashDictionary();
        foreach (var entry in dict.RawEntries())
        {
            var keep = ctx.InvokeCallbackDirect(fn, new StashValue[] { StashValue.FromObject(entry.Key), entry.Value });
            if (RuntimeValues.IsTruthy(keep.ToObject()))
            {
                result.Set(entry.Key, entry.Value);
            }
        }
        return result;
    }

    /// <summary>Constructs a dictionary from an array of [key, value] pairs.</summary>
    /// <param name="pairs">Array of [key, value] pairs</param>
    /// <exception cref="TypeError">if any element is not a two-element [key, value] array, or if any key is null</exception>
    /// <returns>New dictionary</returns>
    [StashFn(ReturnType = "dict")]
    private static StashDictionary FromPairs(List<StashValue> pairs)
    {
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
                throw new TypeError("'dict.fromPairs' requires each element to be a [key, value] pair.");
            }
            if (key is null) throw new TypeError("Dictionary key cannot be null in 'dict.fromPairs'.");
            result.Set(key, val);
        }
        return result;
    }

    /// <summary>Returns a new dictionary with only the specified keys.</summary>
    /// <param name="dict">The source dictionary</param>
    /// <param name="keys">Array of keys to include</param>
    /// <returns>New dictionary with picked keys</returns>
    [StashFn(ReturnType = "dict")]
    private static StashDictionary Pick(StashDictionary dict, List<StashValue> keys)
    {
        var result = new StashDictionary();
        foreach (var keySv in keys)
        {
            var key = keySv.ToObject();
            if (key != null && dict.Has(key))
                result.Set(key, dict.Get(key));
        }
        return result;
    }

    /// <summary>Returns a new dictionary excluding the specified keys.</summary>
    /// <param name="dict">The source dictionary</param>
    /// <param name="keys">Array of keys to omit</param>
    /// <returns>New dictionary without omitted keys</returns>
    [StashFn(ReturnType = "dict")]
    private static StashDictionary Omit(StashDictionary dict, List<StashValue> keys)
    {
        var result = new StashDictionary();
        foreach (var entry in dict.RawEntries())
        {
            bool omit = false;
            foreach (var keySv in keys)
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
        return result;
    }

    /// <summary>Returns a new dictionary merging defaults with dict. Dict values take precedence.</summary>
    /// <param name="dict">The priority dictionary</param>
    /// <param name="defaults">The defaults dictionary</param>
    /// <returns>Merged dictionary</returns>
    [StashFn(ReturnType = "dict")]
    private static StashDictionary Defaults(StashDictionary dict, StashDictionary defaults)
    {
        var result = new StashDictionary();
        foreach (var entry in defaults.RawEntries())
            result.Set(entry.Key, entry.Value);
        foreach (var entry in dict.RawEntries())
            result.Set(entry.Key, entry.Value);
        return result;
    }

    /// <summary>Returns true if fn(key, value) returns truthy for at least one entry.</summary>
    /// <param name="dict">The dictionary</param>
    /// <param name="fn">The predicate function</param>
    /// <returns>true if any entry matches</returns>
    [StashFn(ReturnType = "bool")]
    private static bool Any(IInterpreterContext ctx, StashDictionary dict, IStashCallable fn)
    {
        foreach (var entry in dict.RawEntries())
        {
            if (RuntimeValues.IsTruthy(ctx.InvokeCallbackDirect(fn, new StashValue[] { StashValue.FromObject(entry.Key), entry.Value }).ToObject()))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Returns true if fn(key, value) returns truthy for every entry.</summary>
    /// <param name="dict">The dictionary</param>
    /// <param name="fn">The predicate function</param>
    /// <returns>true if all entries match</returns>
    [StashFn(ReturnType = "bool")]
    private static bool Every(IInterpreterContext ctx, StashDictionary dict, IStashCallable fn)
    {
        foreach (var entry in dict.RawEntries())
        {
            if (!RuntimeValues.IsTruthy(ctx.InvokeCallbackDirect(fn, new StashValue[] { StashValue.FromObject(entry.Key), entry.Value }).ToObject()))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Returns the first [key, value] pair for which fn(key, value) is truthy, or null.</summary>
    /// <param name="dict">The dictionary</param>
    /// <param name="fn">The predicate function</param>
    /// <returns>[key, value] pair or null</returns>
    [StashFn(ReturnType = "array")]
    private static StashValue Find(IInterpreterContext ctx, StashDictionary dict, IStashCallable fn)
    {
        foreach (var entry in dict.RawEntries())
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
