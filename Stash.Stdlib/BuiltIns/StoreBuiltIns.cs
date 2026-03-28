namespace Stash.Stdlib.BuiltIns;

using System.Collections.Generic;
using System.Linq;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Models;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

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

    public static NamespaceDefinition Define()
    {
        var ns = new NamespaceBuilder("store");

        // store.set(key, value) — Stores 'value' under 'key' in the global in-memory store. Returns null.
        ns.Function("set", [Param("key", "string"), Param("value", "any")], (_, args) =>
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
        },
            returnType: "void",
            documentation: "Sets a key-value pair in the in-memory store.\n@param key The key (string)\n@param value The value to store");

        // store.get(key) — Returns the value associated with 'key', or null if the key does not exist.
        ns.Function("get", [Param("key", "string")], (_, args) =>
        {
            if (args[0] is not string key)
            {
                throw new RuntimeError("Argument to 'store.get' must be a string.");
            }

            lock (_store)
            {
                return _store.TryGetValue(key, out var value) ? value : null;
            }
        },
            returnType: "any",
            documentation: "Returns the value for key, or null if not found.\n@param key The key (string)\n@return The stored value or null");

        // store.has(key) — Returns true if 'key' exists in the store, false otherwise.
        ns.Function("has", [Param("key", "string")], (_, args) =>
        {
            if (args[0] is not string key)
            {
                throw new RuntimeError("Argument to 'store.has' must be a string.");
            }

            lock (_store)
            {
                return _store.ContainsKey(key);
            }
        },
            returnType: "bool",
            documentation: "Returns true if the key exists in the store.\n@param key The key to check\n@return true if the key exists");

        // store.remove(key) — Removes 'key' from the store. Returns true if it was present, false otherwise.
        ns.Function("remove", [Param("key", "string")], (_, args) =>
        {
            if (args[0] is not string key)
            {
                throw new RuntimeError("Argument to 'store.remove' must be a string.");
            }

            lock (_store)
            {
                return _store.Remove(key);
            }
        },
            returnType: "bool",
            documentation: "Removes a key from the store. Returns true if it existed.\n@param key The key to remove\n@return true if the key was removed");

        // store.keys() — Returns an array of all keys currently in the store.
        ns.Function("keys", [], (_, _) =>
        {
            lock (_store)
            {
                return _store.Keys.Cast<object?>().ToList();
            }
        },
            returnType: "array",
            documentation: "Returns an array of all keys in the store.\n@return Array of key strings");

        // store.values() — Returns an array of all values currently in the store.
        ns.Function("values", [], (_, _) =>
        {
            lock (_store)
            {
                return _store.Values.ToList();
            }
        },
            returnType: "array",
            documentation: "Returns an array of all values in the store.\n@return Array of values");

        // store.clear() — Removes all entries from the store. Returns null.
        ns.Function("clear", [], (_, _) =>
        {
            lock (_store)
            {
                _store.Clear();
            }

            return null;
        },
            returnType: "void",
            documentation: "Clears all entries from the store.");

        // store.size() — Returns the number of entries currently in the store.
        ns.Function("size", [], (_, _) =>
        {
            lock (_store)
            {
                return (long)_store.Count;
            }
        },
            returnType: "int",
            documentation: "Returns the number of entries in the store.\n@return The entry count");

        // store.all() — Returns a dictionary containing all key-value pairs in the store.
        ns.Function("all", [], (_, _) =>
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
        },
            returnType: "dict",
            documentation: "Returns all key-value pairs as a dictionary.\n@return A dictionary of all stored entries");

        // store.scope(prefix) — Returns a dictionary of all entries whose keys start with 'prefix'.
        ns.Function("scope", [Param("prefix", "string")], (_, args) =>
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
        },
            returnType: "dict",
            documentation: "Returns all entries whose keys start with the given prefix.\n@param prefix The key prefix\n@return Dictionary of matching entries");

        return ns.Build();
    }
}
