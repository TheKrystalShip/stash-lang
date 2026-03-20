namespace Stash.Interpreting.BuiltIns;

using System.Collections.Generic;
using Stash.Interpreting.Types;

/// <summary>
/// Registers the 'dict' namespace built-in functions.
/// </summary>
public static class DictBuiltIns
{
    public static void Register(Environment globals)
    {
        // ── dict namespace ───────────────────────────────────────────────
        var dict = new StashNamespace("dict");

        dict.Define("new", new BuiltInFunction("dict.new", 0, (_, args) =>
        {
            return new StashDictionary();
        }));

        dict.Define("get", new BuiltInFunction("dict.get", 2, (_, args) =>
        {
            if (args[0] is not StashDictionary d)
            {
                throw new RuntimeError("First argument to 'dict.get' must be a dictionary.");
            }

            var key = args[1] ?? throw new RuntimeError("Dictionary key cannot be null.");
            return d.Get(key);
        }));

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

        dict.Define("has", new BuiltInFunction("dict.has", 2, (_, args) =>
        {
            if (args[0] is not StashDictionary d)
            {
                throw new RuntimeError("First argument to 'dict.has' must be a dictionary.");
            }

            var key = (args[1] ?? throw new RuntimeError("Dictionary key cannot be null.")) ?? throw new RuntimeError("Dictionary key cannot be null.");
            return d.Has(key);
        }));

        dict.Define("remove", new BuiltInFunction("dict.remove", 2, (_, args) =>
        {
            if (args[0] is not StashDictionary d)
            {
                throw new RuntimeError("First argument to 'dict.remove' must be a dictionary.");
            }

            var key = args[1] ?? throw new RuntimeError("Dictionary key cannot be null.");
            return d.Remove(key);
        }));

        dict.Define("clear", new BuiltInFunction("dict.clear", 1, (_, args) =>
        {
            if (args[0] is not StashDictionary d)
            {
                throw new RuntimeError("First argument to 'dict.clear' must be a dictionary.");
            }

            d.Clear();
            return null;
        }));

        dict.Define("keys", new BuiltInFunction("dict.keys", 1, (_, args) =>
        {
            if (args[0] is not StashDictionary d)
            {
                throw new RuntimeError("First argument to 'dict.keys' must be a dictionary.");
            }

            return d.Keys();
        }));

        dict.Define("values", new BuiltInFunction("dict.values", 1, (_, args) =>
        {
            if (args[0] is not StashDictionary d)
            {
                throw new RuntimeError("First argument to 'dict.values' must be a dictionary.");
            }

            return d.Values();
        }));

        dict.Define("size", new BuiltInFunction("dict.size", 1, (_, args) =>
        {
            if (args[0] is not StashDictionary d)
            {
                throw new RuntimeError("First argument to 'dict.size' must be a dictionary.");
            }

            return (long)d.Count;
        }));

        dict.Define("pairs", new BuiltInFunction("dict.pairs", 1, (_, args) =>
        {
            if (args[0] is not StashDictionary d)
            {
                throw new RuntimeError("First argument to 'dict.pairs' must be a dictionary.");
            }

            return d.Pairs();
        }));

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
