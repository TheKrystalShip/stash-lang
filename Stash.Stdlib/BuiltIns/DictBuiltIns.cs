namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Models;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

/// <summary>
/// Registers the <c>dict</c> namespace built-in functions for dictionary manipulation.
/// </summary>
/// <remarks>
/// <para>
/// Provides 21 functions including <c>dict.new</c>, <c>dict.get</c>, <c>dict.set</c>,
/// <c>dict.has</c>, <c>dict.remove</c>, <c>dict.keys</c>, <c>dict.values</c>,
/// <c>dict.map</c>, <c>dict.filter</c>, <c>dict.merge</c>, <c>dict.pick</c>,
/// <c>dict.omit</c>, <c>dict.defaults</c>, and more.
/// All functions are registered as <see cref="BuiltInFunction"/> instances on a
/// <see cref="StashNamespace"/> in the global <see cref="Environment"/>.
/// </para>
/// <para>
/// Mutating functions (e.g. <c>dict.set</c>, <c>dict.remove</c>, <c>dict.clear</c>)
/// modify the dictionary in place. Non-mutating functions return a new dictionary or value.
/// </para>
/// </remarks>
public static class DictBuiltIns
{
    /// <summary>
    /// Registers all <c>dict</c> namespace functions into the global environment.
    /// </summary>
    /// <param name="globals">The global <see cref="Environment"/> to register functions in.</param>
    public static NamespaceDefinition Define()
    {
        // ── dict namespace ───────────────────────────────────────────────
        var ns = new NamespaceBuilder("dict");

        // dict.new() — Creates and returns a new empty dictionary.
        ns.Function("new", [], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            return StashValue.FromObj(new StashDictionary());
        },
            returnType: "dict",
            documentation: "Creates and returns a new empty dictionary.\n@return An empty dictionary");

        // dict.get(dict, key, default?) — Returns the value associated with key in dict, or
        // null if the key does not exist. When an optional default is provided, it is returned
        // instead of null when the key is missing.
        // Throws RuntimeError if the first argument is not a dictionary or the key is null.
        ns.Function("get", [Param("dict", "dict"), Param("key", "any"), Param("default", "any")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 2 || args.Length > 3)
                throw new RuntimeError("'dict.get' requires 2 or 3 arguments.");
            var d = SvArgs.Dict(args, 0, "dict.get");
            var key = args[1].ToObject() ?? throw new RuntimeError("Dictionary key cannot be null.");
            var result = d.Get(key);
            if (result.IsNull && args.Length == 3)
                return args[2];
            return result;
        },
            returnType: "any",
            isVariadic: true,
            documentation: "Returns the value for key, or null if not found. If a default is provided it is returned instead of null for missing keys.\n@param dict The dictionary\n@param key The key\n@param default Optional default value when key is missing\n@return The value, default, or null");

        // dict.set(dict, key, value) — Sets the given key to value in dict in place.
        // Creates the key if it does not already exist. Returns null.
        // Throws RuntimeError if the first argument is not a dictionary or the key is null.
        ns.Function("set", [Param("dict", "dict"), Param("key", "any"), Param("value", "any")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var d = SvArgs.Dict(args, 0, "dict.set");
            var key = args[1].ToObject() ?? throw new RuntimeError("Dictionary key cannot be null.");
            d.Set(key, args[2]);
            return StashValue.Null;
        },
            returnType: "void",
            documentation: "Sets a key-value pair in the dictionary. Modifies in place.\n@param dict The dictionary\n@param key The key\n@param value The value to set");

        // dict.has(dict, key) — Returns true if dict contains key, false otherwise.
        // Throws RuntimeError if the first argument is not a dictionary or the key is null.
        ns.Function("has", [Param("dict", "dict"), Param("key", "any")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var d = SvArgs.Dict(args, 0, "dict.has");
            var key = args[1].ToObject() ?? throw new RuntimeError("Dictionary key cannot be null.");
            return StashValue.FromBool(d.Has(key));
        },
            returnType: "bool",
            documentation: "Returns true if the dictionary contains the key.\n@param dict The dictionary\n@param key The key to check\n@return true if the key exists");

        // dict.remove(dict, key) — Removes key from dict in place.
        // Returns true if the key was found and removed, false if it did not exist.
        // Throws RuntimeError if the first argument is not a dictionary or the key is null.
        ns.Function("remove", [Param("dict", "dict"), Param("key", "any")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var d = SvArgs.Dict(args, 0, "dict.remove");
            var key = args[1].ToObject() ?? throw new RuntimeError("Dictionary key cannot be null.");
            return StashValue.FromBool(d.Remove(key));
        },
            returnType: "bool",
            documentation: "Removes a key from the dictionary. Returns true if it existed.\n@param dict The dictionary\n@param key The key to remove\n@return true if the key was removed");

        // dict.clear(dict) — Removes all entries from dict in place. Returns null.
        // Throws RuntimeError if the first argument is not a dictionary.
        ns.Function("clear", [Param("dict", "dict")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            SvArgs.Dict(args, 0, "dict.clear").Clear();
            return StashValue.Null;
        },
            returnType: "void",
            documentation: "Removes all entries from the dictionary.\n@param dict The dictionary to clear");

        // dict.keys(dict) — Returns an array of all keys in dict (in insertion order).
        // Throws RuntimeError if the first argument is not a dictionary.
        ns.Function("keys", [Param("dict", "dict")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var d = SvArgs.Dict(args, 0, "dict.keys");
            return StashValue.FromObj(d.Keys());
        },
            returnType: "array",
            documentation: "Returns an array of all keys in the dictionary.\n@param dict The dictionary\n@return Array of keys");

        // dict.values(dict) — Returns an array of all values in dict (in insertion order).
        // Throws RuntimeError if the first argument is not a dictionary.
        ns.Function("values", [Param("dict", "dict")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var d = SvArgs.Dict(args, 0, "dict.values");
            return StashValue.FromObj(d.Values());
        },
            returnType: "array",
            documentation: "Returns an array of all values in the dictionary.\n@param dict The dictionary\n@return Array of values");

        // dict.size(dict) — Returns the number of entries in dict as an integer.
        // Throws RuntimeError if the first argument is not a dictionary.
        ns.Function("size", [Param("dict", "dict")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var d = SvArgs.Dict(args, 0, "dict.size");
            return StashValue.FromInt((long)d.Count);
        },
            returnType: "int",
            documentation: "Returns the number of entries in the dictionary.\n@param dict The dictionary\n@return Entry count");

        // dict.pairs(dict) — Returns an array of [key, value] pairs for each entry in dict
        // (in insertion order).
        // Throws RuntimeError if the first argument is not a dictionary.
        ns.Function("pairs", [Param("dict", "dict")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var d = SvArgs.Dict(args, 0, "dict.pairs");
            return StashValue.FromObj(d.Pairs());
        },
            returnType: "array",
            documentation: "Returns an array of [key, value] pairs for each entry.\n@param dict The dictionary\n@return Array of [key, value] pairs");

        // dict.forEach(dict, fn) — Calls fn(key, value) for each entry in dict.
        // Return values of fn are discarded. Returns null.
        // Throws RuntimeError if the first argument is not a dictionary or the second is not a function.
        ns.Function("forEach", [Param("dict", "dict"), Param("fn", "fn")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var d = SvArgs.Dict(args, 0, "dict.forEach");
            var fn = SvArgs.Callable(args, 1, "dict.forEach");

            foreach (var entry in d.RawEntries())
            {
                ctx.InvokeCallbackDirect(fn, new StashValue[] { StashValue.FromObject(entry.Key), entry.Value });
            }
            return StashValue.Null;
        },
            returnType: "void",
            documentation: "Calls fn(key, value) for each entry. Returns null.\n@param dict The dictionary\n@param fn The callback function");

        // dict.merge(dict1, dict2, deep?) — Returns a new dictionary containing all entries
        // from dict1 followed by all entries from dict2. Keys in dict2 overwrite keys in dict1.
        // When deep is true, nested dicts are merged recursively rather than replaced.
        // Does not modify either input dictionary.
        // Throws RuntimeError if either argument is not a dictionary.
        ns.Function("merge", [Param("dict1", "dict"), Param("dict2", "dict"), Param("deep", "bool")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 2 || args.Length > 3)
                throw new RuntimeError("'dict.merge' requires 2 or 3 arguments.");
            var d1 = SvArgs.Dict(args, 0, "dict.merge");
            var d2 = SvArgs.Dict(args, 1, "dict.merge");
            bool deep = args.Length == 3 && SvArgs.Bool(args, 2, "dict.merge");
            return StashValue.FromObj(MergeInternal(d1, d2, deep));
        },
            returnType: "dict",
            isVariadic: true,
            documentation: "Returns a new dictionary merging dict1 and dict2. dict2 keys take precedence. When deep is true, nested dicts are merged recursively.\n@param dict1 The base dictionary\n@param dict2 The overriding dictionary\n@param deep Optional boolean; when true performs a deep recursive merge\n@return Merged dictionary");

        // dict.map(dict, fn) — Returns a new dictionary where each value is the result of
        // calling fn(key, value) for the corresponding entry in dict. Keys are preserved.
        // Throws RuntimeError if the first argument is not a dictionary or the second is not a function.
        ns.Function("map", [Param("dict", "dict"), Param("fn", "fn")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var d = SvArgs.Dict(args, 0, "dict.map");
            var fn = SvArgs.Callable(args, 1, "dict.map");

            var result = new StashDictionary();
            foreach (var entry in d.RawEntries())
            {
                result.Set(entry.Key, ctx.InvokeCallbackDirect(fn, new StashValue[] { StashValue.FromObject(entry.Key), entry.Value }));
            }
            return StashValue.FromObj(result);
        },
            returnType: "dict",
            documentation: "Returns a new dictionary with each value transformed by fn(key, value).\n@param dict The dictionary\n@param fn The mapping function\n@return Transformed dictionary");

        // dict.filter(dict, fn) — Returns a new dictionary containing only the entries for
        // which fn(key, value) returns a truthy value. Does not modify the original dictionary.
        // Throws RuntimeError if the first argument is not a dictionary or the second is not a function.
        ns.Function("filter", [Param("dict", "dict"), Param("fn", "fn")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
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
        },
            returnType: "dict",
            documentation: "Returns a new dictionary with only entries where fn(key, value) is truthy.\n@param dict The dictionary\n@param fn The filter function\n@return Filtered dictionary");

        // ── Additional dictionary utilities ──────────────────────────────────

        // dict.fromPairs(pairs) — Constructs a new dictionary from an array of [key, value]
        // pairs. Each element must be a 2-element array. Keys cannot be null.
        // Throws RuntimeError if the argument is not an array, or any element is not a valid pair.
        ns.Function("fromPairs", [Param("pairs", "array")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
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
                    throw new RuntimeError("'dict.fromPairs' requires each element to be a [key, value] pair.");
                }
                if (key is null) throw new RuntimeError("Dictionary key cannot be null in 'dict.fromPairs'.");
                result.Set(key, val);
            }
            return StashValue.FromObj(result);
        },
            returnType: "dict",
            documentation: "Constructs a dictionary from an array of [key, value] pairs.\n@param pairs Array of [key, value] pairs\n@return New dictionary");

        // dict.pick(dict, keys) — Returns a new dictionary containing only the entries whose
        // keys appear in the keys array. Missing keys are silently ignored.
        // Throws RuntimeError if the first argument is not a dictionary or the second is not an array.
        ns.Function("pick", [Param("dict", "dict"), Param("keys", "array")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
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
        },
            returnType: "dict",
            documentation: "Returns a new dictionary with only the specified keys.\n@param dict The source dictionary\n@param keys Array of keys to include\n@return New dictionary with picked keys");

        // dict.omit(dict, keys) — Returns a new dictionary containing all entries from dict
        // except those whose keys appear in the keys array.
        // Throws RuntimeError if the first argument is not a dictionary or the second is not an array.
        ns.Function("omit", [Param("dict", "dict"), Param("keys", "array")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
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
        },
            returnType: "dict",
            documentation: "Returns a new dictionary excluding the specified keys.\n@param dict The source dictionary\n@param keys Array of keys to omit\n@return New dictionary without omitted keys");

        // dict.defaults(dict, defaults) — Returns a new dictionary that combines defaults with
        // dict, where dict entries take precedence over defaults for overlapping keys.
        // Entries in defaults not present in dict are included in the result.
        // Throws RuntimeError if either argument is not a dictionary.
        ns.Function("defaults", [Param("dict", "dict"), Param("defaults", "dict")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            var d = SvArgs.Dict(args, 0, "dict.defaults");
            var defaults = SvArgs.Dict(args, 1, "dict.defaults");
            var result = new StashDictionary();
            foreach (var entry in defaults.RawEntries())
                result.Set(entry.Key, entry.Value);
            foreach (var entry in d.RawEntries())
                result.Set(entry.Key, entry.Value);
            return StashValue.FromObj(result);
        },
            returnType: "dict",
            documentation: "Returns a new dictionary merging defaults with dict. Dict values take precedence.\n@param dict The priority dictionary\n@param defaults The defaults dictionary\n@return Merged dictionary");

        // dict.any(dict, fn) — Returns true if fn(key, value) returns a truthy value for at
        // least one entry in dict, false otherwise. Short-circuits on the first truthy result.
        // Throws RuntimeError if the first argument is not a dictionary or the second is not a function.
        ns.Function("any", [Param("dict", "dict"), Param("fn", "fn")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
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
        },
            returnType: "bool",
            documentation: "Returns true if fn(key, value) returns truthy for at least one entry.\n@param dict The dictionary\n@param fn The predicate function\n@return true if any entry matches");

        // dict.every(dict, fn) — Returns true if fn(key, value) returns a truthy value for
        // every entry in dict, false otherwise. Short-circuits on the first falsy result.
        // Throws RuntimeError if the first argument is not a dictionary or the second is not a function.
        ns.Function("every", [Param("dict", "dict"), Param("fn", "fn")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
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
        },
            returnType: "bool",
            documentation: "Returns true if fn(key, value) returns truthy for every entry.\n@param dict The dictionary\n@param fn The predicate function\n@return true if all entries match");

        // dict.find(dict, fn) — Returns the first [key, value] pair for which fn(key, value)
        // returns a truthy value, or null if no such entry exists.
        // Throws RuntimeError if the first argument is not a dictionary or the second is not a function.
        ns.Function("find", [Param("dict", "dict"), Param("fn", "fn")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
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
        },
            returnType: "array",
            documentation: "Returns the first [key, value] pair for which fn(key, value) is truthy, or null.\n@param dict The dictionary\n@param fn The predicate function\n@return [key, value] pair or null");

        return ns.Build();
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
