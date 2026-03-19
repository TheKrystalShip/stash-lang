namespace Stash.Interpreting.BuiltIns;

using System.Collections.Generic;
using System.Linq;
using Stash.Interpreting.Types;

/// <summary>
/// Registers the 'store' namespace — a process-scoped in-memory key-value store.
/// </summary>
public static class StoreBuiltIns
{
    private static readonly Dictionary<string, object?> _store = new();

    public static void Register(Environment globals)
    {
        var store = new StashNamespace("store");

        store.Define("set", new BuiltInFunction("store.set", 2, (_, args) =>
        {
            if (args[0] is not string key)
            {
                throw new RuntimeError("Argument to 'store.set' must be a string.");
            }

            lock (_store)
            {
                _store[key] = args[1];
            }

            return null;
        }));

        store.Define("get", new BuiltInFunction("store.get", 1, (_, args) =>
        {
            if (args[0] is not string key)
            {
                throw new RuntimeError("Argument to 'store.get' must be a string.");
            }

            lock (_store)
            {
                return _store.TryGetValue(key, out var value) ? value : null;
            }
        }));

        store.Define("has", new BuiltInFunction("store.has", 1, (_, args) =>
        {
            if (args[0] is not string key)
            {
                throw new RuntimeError("Argument to 'store.has' must be a string.");
            }

            lock (_store)
            {
                return _store.ContainsKey(key);
            }
        }));

        store.Define("remove", new BuiltInFunction("store.remove", 1, (_, args) =>
        {
            if (args[0] is not string key)
            {
                throw new RuntimeError("Argument to 'store.remove' must be a string.");
            }

            lock (_store)
            {
                return _store.Remove(key);
            }
        }));

        store.Define("keys", new BuiltInFunction("store.keys", 0, (_, args) =>
        {
            lock (_store)
            {
                return _store.Keys.Cast<object?>().ToList();
            }
        }));

        store.Define("values", new BuiltInFunction("store.values", 0, (_, args) =>
        {
            lock (_store)
            {
                return _store.Values.ToList();
            }
        }));

        store.Define("clear", new BuiltInFunction("store.clear", 0, (_, args) =>
        {
            lock (_store)
            {
                _store.Clear();
            }

            return null;
        }));

        store.Define("size", new BuiltInFunction("store.size", 0, (_, args) =>
        {
            lock (_store)
            {
                return (long)_store.Count;
            }
        }));

        store.Define("all", new BuiltInFunction("store.all", 0, (_, args) =>
        {
            var dict = new StashDictionary();
            lock (_store)
            {
                foreach (var entry in _store)
                {
                    dict.Set(entry.Key, entry.Value);
                }
            }

            return dict;
        }));

        store.Define("scope", new BuiltInFunction("store.scope", 1, (_, args) =>
        {
            if (args[0] is not string prefix)
            {
                throw new RuntimeError("Argument to 'store.scope' must be a string.");
            }

            var dict = new StashDictionary();
            lock (_store)
            {
                foreach (var entry in _store.Where(e => e.Key.StartsWith(prefix)))
                {
                    dict.Set(entry.Key, entry.Value);
                }
            }

            return dict;
        }));

        globals.Define("store", store);
    }
}
