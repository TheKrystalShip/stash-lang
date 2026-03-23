namespace Stash.Interpreting.BuiltIns;

using System.Collections.Generic;
using Stash.Interpreting.Types;

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
    public static void Register(Environment globals)
    {
        // ── dict namespace ───────────────────────────────────────────────
        var dict = new StashNamespace("dict");

        // dict.new() — Creates and returns a new empty dictionary.
        dict.Define("new", new BuiltInFunction("dict.new", 0, (_, args) =>
        {
            return new StashDictionary();
        }));

        // dict.get(dict, key) — Returns the value associated with key in dict, or null
        // if the key does not exist.
        // Throws RuntimeError if the first argument is not a dictionary or the key is null.
        dict.Define("get", new BuiltInFunction("dict.get", 2, (_, args) =>
        {
            if (args[0] is not StashDictionary d)
            {
                throw new RuntimeError("First argument to 'dict.get' must be a dictionary.");
            }

            var key = args[1] ?? throw new RuntimeError("Dictionary key cannot be null.");
            return d.Get(key);
        }));

        // dict.set(dict, key, value) — Sets the given key to value in dict in place.
        // Creates the key if it does not already exist. Returns null.
        // Throws RuntimeError if the first argument is not a dictionary or the key is null.
        dict.Define("set", new BuiltInFunction("dict.set", 3, (_, args) =>
        {
            if (args[0] is not StashDictionary d)
            {
                throw new RuntimeError("First argument to 'dict.set' must be a dictionary.");
            }

            var key = args[1] ?? throw new RuntimeError("Dictionary key cannot be null.");
            d.Set(key, args[2]);
            return null;
        }));

        // dict.has(dict, key) — Returns true if dict contains key, false otherwise.
        // Throws RuntimeError if the first argument is not a dictionary or the key is null.
        dict.Define("has", new BuiltInFunction("dict.has", 2, (_, args) =>
        {
            if (args[0] is not StashDictionary d)
            {
                throw new RuntimeError("First argument to 'dict.has' must be a dictionary.");
            }

            var key = (args[1] ?? throw new RuntimeError("Dictionary key cannot be null.")) ?? throw new RuntimeError("Dictionary key cannot be null.");
            return d.Has(key);
        }));

        // dict.remove(dict, key) — Removes key from dict in place.
        // Returns true if the key was found and removed, false if it did not exist.
        // Throws RuntimeError if the first argument is not a dictionary or the key is null.
        dict.Define("remove", new BuiltInFunction("dict.remove", 2, (_, args) =>
        {
            if (args[0] is not StashDictionary d)
            {
                throw new RuntimeError("First argument to 'dict.remove' must be a dictionary.");
            }

            var key = args[1] ?? throw new RuntimeError("Dictionary key cannot be null.");
            return d.Remove(key);
        }));

        // dict.clear(dict) — Removes all entries from dict in place. Returns null.
        // Throws RuntimeError if the first argument is not a dictionary.
        dict.Define("clear", new BuiltInFunction("dict.clear", 1, (_, args) =>
        {
            if (args[0] is not StashDictionary d)
            {
                throw new RuntimeError("First argument to 'dict.clear' must be a dictionary.");
            }

            d.Clear();
            return null;
        }));

        // dict.keys(dict) — Returns an array of all keys in dict (in insertion order).
        // Throws RuntimeError if the first argument is not a dictionary.
        dict.Define("keys", new BuiltInFunction("dict.keys", 1, (_, args) =>
        {
            if (args[0] is not StashDictionary d)
            {
                throw new RuntimeError("First argument to 'dict.keys' must be a dictionary.");
            }

            return d.Keys();
        }));

        // dict.values(dict) — Returns an array of all values in dict (in insertion order).
        // Throws RuntimeError if the first argument is not a dictionary.
        dict.Define("values", new BuiltInFunction("dict.values", 1, (_, args) =>
        {
            if (args[0] is not StashDictionary d)
            {
                throw new RuntimeError("First argument to 'dict.values' must be a dictionary.");
            }

            return d.Values();
        }));

        // dict.size(dict) — Returns the number of entries in dict as an integer.
        // Throws RuntimeError if the first argument is not a dictionary.
        dict.Define("size", new BuiltInFunction("dict.size", 1, (_, args) =>
        {
            if (args[0] is not StashDictionary d)
            {
                throw new RuntimeError("First argument to 'dict.size' must be a dictionary.");
            }

            return (long)d.Count;
        }));

        // dict.pairs(dict) — Returns an array of [key, value] pairs for each entry in dict
        // (in insertion order).
        // Throws RuntimeError if the first argument is not a dictionary.
        dict.Define("pairs", new BuiltInFunction("dict.pairs", 1, (_, args) =>
        {
            if (args[0] is not StashDictionary d)
            {
                throw new RuntimeError("First argument to 'dict.pairs' must be a dictionary.");
            }

            return d.Pairs();
        }));

        // dict.forEach(dict, fn) — Calls fn(key, value) for each entry in dict.
        // Return values of fn are discarded. Returns null.
        // Throws RuntimeError if the first argument is not a dictionary or the second is not a function.
        dict.Define("forEach", new BuiltInFunction("dict.forEach", 2, (interp, args) =>
        {
            if (args[0] is not StashDictionary d)
            {
                throw new RuntimeError("First argument to 'dict.forEach' must be a dictionary.");
            }

            if (args[1] is not IStashCallable fn)
            {
                throw new RuntimeError("Second argument to 'dict.forEach' must be a function.");
            }

            foreach (var entry in d.RawEntries())
            {
                fn.Call(interp, new List<object?> { entry.Key, entry.Value });
            }
            return null;
        }));

        // dict.merge(dict1, dict2) — Returns a new dictionary containing all entries from
        // dict1 followed by all entries from dict2. Keys in dict2 overwrite keys in dict1.
        // Does not modify either input dictionary.
        // Throws RuntimeError if either argument is not a dictionary.
        dict.Define("merge", new BuiltInFunction("dict.merge", 2, (_, args) =>
        {
            if (args[0] is not StashDictionary d1)
            {
                throw new RuntimeError("First argument to 'dict.merge' must be a dictionary.");
            }

            if (args[1] is not StashDictionary d2)
            {
                throw new RuntimeError("Second argument to 'dict.merge' must be a dictionary.");
            }

            var result = new StashDictionary();
            foreach (var entry in d1.RawEntries())
            {
                result.Set(entry.Key, entry.Value);
            }
            foreach (var entry in d2.RawEntries())
            {
                result.Set(entry.Key, entry.Value);
            }
            return result;
        }));

        // dict.map(dict, fn) — Returns a new dictionary where each value is the result of
        // calling fn(key, value) for the corresponding entry in dict. Keys are preserved.
        // Throws RuntimeError if the first argument is not a dictionary or the second is not a function.
        dict.Define("map", new BuiltInFunction("dict.map", 2, (interp, args) =>
        {
            if (args[0] is not StashDictionary d)
            {
                throw new RuntimeError("First argument to 'dict.map' must be a dictionary.");
            }

            if (args[1] is not IStashCallable fn)
            {
                throw new RuntimeError("Second argument to 'dict.map' must be a function.");
            }

            var result = new StashDictionary();
            foreach (var entry in d.RawEntries())
            {
                result.Set(entry.Key, fn.Call(interp, new List<object?> { entry.Key, entry.Value }));
            }
            return result;
        }));

        // dict.filter(dict, fn) — Returns a new dictionary containing only the entries for
        // which fn(key, value) returns a truthy value. Does not modify the original dictionary.
        // Throws RuntimeError if the first argument is not a dictionary or the second is not a function.
        dict.Define("filter", new BuiltInFunction("dict.filter", 2, (interp, args) =>
        {
            if (args[0] is not StashDictionary d)
            {
                throw new RuntimeError("First argument to 'dict.filter' must be a dictionary.");
            }

            if (args[1] is not IStashCallable fn)
            {
                throw new RuntimeError("Second argument to 'dict.filter' must be a function.");
            }

            var result = new StashDictionary();
            foreach (var entry in d.RawEntries())
            {
                var keep = fn.Call(interp, new List<object?> { entry.Key, entry.Value });
                if (RuntimeValues.IsTruthy(keep))
                {
                    result.Set(entry.Key, entry.Value);
                }
            }
            return result;
        }));

        // ── Additional dictionary utilities ──────────────────────────────

        // dict.fromPairs(pairs) — Constructs a new dictionary from an array of [key, value]
        // pairs. Each element must be a 2-element array. Keys cannot be null.
        // Throws RuntimeError if the argument is not an array, or any element is not a valid pair.
        dict.Define("fromPairs", new BuiltInFunction("dict.fromPairs", 1, (_, args) =>
        {
            if (args[0] is not List<object?> pairs)
            {
                throw new RuntimeError("First argument to 'dict.fromPairs' must be an array.");
            }

            var result = new StashDictionary();
            foreach (var pair in pairs)
            {
                if (pair is not List<object?> p || p.Count != 2)
                {
                    throw new RuntimeError("'dict.fromPairs' requires each element to be a [key, value] pair.");
                }

                var key = p[0] ?? throw new RuntimeError("Dictionary key cannot be null in 'dict.fromPairs'.");
                result.Set(key, p[1]);
            }
            return result;
        }));

        // dict.pick(dict, keys) — Returns a new dictionary containing only the entries whose
        // keys appear in the keys array. Missing keys are silently ignored.
        // Throws RuntimeError if the first argument is not a dictionary or the second is not an array.
        dict.Define("pick", new BuiltInFunction("dict.pick", 2, (_, args) =>
        {
            if (args[0] is not StashDictionary d)
            {
                throw new RuntimeError("First argument to 'dict.pick' must be a dictionary.");
            }

            if (args[1] is not List<object?> keys)
            {
                throw new RuntimeError("Second argument to 'dict.pick' must be an array.");
            }

            var result = new StashDictionary();
            foreach (var key in keys)
            {
                if (key != null && d.Has(key))
                {
                    result.Set(key, d.Get(key));
                }
            }
            return result;
        }));

        // dict.omit(dict, keys) — Returns a new dictionary containing all entries from dict
        // except those whose keys appear in the keys array.
        // Throws RuntimeError if the first argument is not a dictionary or the second is not an array.
        dict.Define("omit", new BuiltInFunction("dict.omit", 2, (_, args) =>
        {
            if (args[0] is not StashDictionary d)
            {
                throw new RuntimeError("First argument to 'dict.omit' must be a dictionary.");
            }

            if (args[1] is not List<object?> keysToOmit)
            {
                throw new RuntimeError("Second argument to 'dict.omit' must be an array.");
            }

            var result = new StashDictionary();
            foreach (var entry in d.RawEntries())
            {
                bool omit = false;
                foreach (var key in keysToOmit)
                {
                    if (key != null && RuntimeValues.IsEqual(entry.Key, key))
                    {
                        omit = true;
                        break;
                    }
                }
                if (!omit)
                {
                    result.Set(entry.Key, entry.Value);
                }
            }
            return result;
        }));

        // dict.defaults(dict, defaults) — Returns a new dictionary that combines defaults with
        // dict, where dict entries take precedence over defaults for overlapping keys.
        // Entries in defaults not present in dict are included in the result.
        // Throws RuntimeError if either argument is not a dictionary.
        dict.Define("defaults", new BuiltInFunction("dict.defaults", 2, (_, args) =>
        {
            if (args[0] is not StashDictionary d)
            {
                throw new RuntimeError("First argument to 'dict.defaults' must be a dictionary.");
            }

            if (args[1] is not StashDictionary defaults)
            {
                throw new RuntimeError("Second argument to 'dict.defaults' must be a dictionary.");
            }

            var result = new StashDictionary();
            foreach (var entry in defaults.RawEntries())
            {
                result.Set(entry.Key, entry.Value);
            }

            foreach (var entry in d.RawEntries())
            {
                result.Set(entry.Key, entry.Value);
            }

            return result;
        }));

        // dict.any(dict, fn) — Returns true if fn(key, value) returns a truthy value for at
        // least one entry in dict, false otherwise. Short-circuits on the first truthy result.
        // Throws RuntimeError if the first argument is not a dictionary or the second is not a function.
        dict.Define("any", new BuiltInFunction("dict.any", 2, (interp, args) =>
        {
            if (args[0] is not StashDictionary d)
            {
                throw new RuntimeError("First argument to 'dict.any' must be a dictionary.");
            }

            if (args[1] is not IStashCallable fn)
            {
                throw new RuntimeError("Second argument to 'dict.any' must be a function.");
            }

            foreach (var entry in d.RawEntries())
            {
                if (RuntimeValues.IsTruthy(fn.Call(interp, new List<object?> { entry.Key, entry.Value })))
                {
                    return true;
                }
            }
            return false;
        }));

        // dict.every(dict, fn) — Returns true if fn(key, value) returns a truthy value for
        // every entry in dict, false otherwise. Short-circuits on the first falsy result.
        // Throws RuntimeError if the first argument is not a dictionary or the second is not a function.
        dict.Define("every", new BuiltInFunction("dict.every", 2, (interp, args) =>
        {
            if (args[0] is not StashDictionary d)
            {
                throw new RuntimeError("First argument to 'dict.every' must be a dictionary.");
            }

            if (args[1] is not IStashCallable fn)
            {
                throw new RuntimeError("Second argument to 'dict.every' must be a function.");
            }

            foreach (var entry in d.RawEntries())
            {
                if (!RuntimeValues.IsTruthy(fn.Call(interp, new List<object?> { entry.Key, entry.Value })))
                {
                    return false;
                }
            }
            return true;
        }));

        // dict.find(dict, fn) — Returns the first [key, value] pair for which fn(key, value)
        // returns a truthy value, or null if no such entry exists.
        // Throws RuntimeError if the first argument is not a dictionary or the second is not a function.
        dict.Define("find", new BuiltInFunction("dict.find", 2, (interp, args) =>
        {
            if (args[0] is not StashDictionary d)
            {
                throw new RuntimeError("First argument to 'dict.find' must be a dictionary.");
            }

            if (args[1] is not IStashCallable fn)
            {
                throw new RuntimeError("Second argument to 'dict.find' must be a function.");
            }

            foreach (var entry in d.RawEntries())
            {
                if (RuntimeValues.IsTruthy(fn.Call(interp, new List<object?> { entry.Key, entry.Value })))
                {
                    return new List<object?> { entry.Key, entry.Value };
                }
            }
            return null;
        }));

        globals.Define("dict", dict);
    }
}
