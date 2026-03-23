namespace Stash.Interpreting.BuiltIns;

using System.Collections.Generic;
using System.Linq;
using Stash.Interpreting.Types;

/// <summary>
/// Registers the <c>store</c> namespace built-in functions for a process-scoped in-memory key-value store.
/// </summary>
/// <remarks>
/// <para>
/// Provides a thread-safe, globally shared dictionary accessible throughout the lifetime of the
/// interpreter process. Supported operations include setting (<c>store.set</c>), getting
/// (<c>store.get</c>), existence checks (<c>store.has</c>), removal (<c>store.remove</c>),
/// enumeration (<c>store.keys</c>, <c>store.values</c>, <c>store.all</c>), prefix-scoped reads
/// (<c>store.scope</c>), size querying (<c>store.size</c>), and clearing (<c>store.clear</c>).
/// </para>
/// <para>All mutations are protected by a <see langword="lock"/> on the backing dictionary.</para>
/// </remarks>
public static class StoreBuiltIns
{
    /// <summary>The backing dictionary for all <c>store</c> namespace values, shared across all interpreter instances.</summary>
    private static readonly Dictionary<string, object?> _store = new();

    /// <summary>
    /// Registers all <c>store</c> namespace functions into the global environment.
    /// </summary>
    /// <param name="globals">The global <see cref="Environment"/> to register functions in.</param>
    public static void Register(Environment globals)
    {
        var store = new StashNamespace("store");

        // store.set(key, value) — Stores 'value' under 'key' in the global in-memory store. Returns null.
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

        // store.get(key) — Returns the value associated with 'key', or null if the key does not exist.
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

        // store.has(key) — Returns true if 'key' exists in the store, false otherwise.
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

        // store.remove(key) — Removes 'key' from the store. Returns true if it was present, false otherwise.
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

        // store.keys() — Returns an array of all keys currently in the store.
        store.Define("keys", new BuiltInFunction("store.keys", 0, (_, args) =>
        {
            lock (_store)
            {
                return _store.Keys.Cast<object?>().ToList();
            }
        }));

        // store.values() — Returns an array of all values currently in the store.
        store.Define("values", new BuiltInFunction("store.values", 0, (_, args) =>
        {
            lock (_store)
            {
                return _store.Values.ToList();
            }
        }));

        // store.clear() — Removes all entries from the store. Returns null.
        store.Define("clear", new BuiltInFunction("store.clear", 0, (_, args) =>
        {
            lock (_store)
            {
                _store.Clear();
            }

            return null;
        }));

        // store.size() — Returns the number of entries currently in the store.
        store.Define("size", new BuiltInFunction("store.size", 0, (_, args) =>
        {
            lock (_store)
            {
                return (long)_store.Count;
            }
        }));

        // store.all() — Returns a dictionary containing all key-value pairs in the store.
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

        // store.scope(prefix) — Returns a dictionary of all entries whose keys start with 'prefix'.
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
