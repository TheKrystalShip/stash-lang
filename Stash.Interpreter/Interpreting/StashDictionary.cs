namespace Stash.Interpreting;

using System.Collections.Generic;

/// <summary>
/// Represents a dictionary — an unordered collection of key-value pairs.
/// Keys must be value types: string, int (long), float (double), or bool.
/// </summary>
public class StashDictionary
{
    private readonly Dictionary<object, object?> _entries = new();

    /// <summary>
    /// Gets the number of key-value pairs.
    /// </summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Sets a key-value pair. Validates that the key is a supported type.
    /// </summary>
    public void Set(object key, object? value)
    {
        ValidateKey(key);
        _entries[key] = value;
    }

    /// <summary>
    /// Gets the value for a key, or null if the key does not exist.
    /// </summary>
    public object? Get(object key)
    {
        return _entries.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>
    /// Checks if the dictionary contains a key.
    /// </summary>
    public bool Has(object key)
    {
        return _entries.ContainsKey(key);
    }

    /// <summary>
    /// Removes a key-value pair. Returns true if the key was found and removed.
    /// </summary>
    public bool Remove(object key)
    {
        return _entries.Remove(key);
    }

    /// <summary>
    /// Removes all key-value pairs.
    /// </summary>
    public void Clear()
    {
        _entries.Clear();
    }

    /// <summary>
    /// Returns all keys as a list.
    /// </summary>
    public List<object?> Keys()
    {
        var keys = new List<object?>();
        foreach (var key in _entries.Keys)
            keys.Add(key);
        return keys;
    }

    /// <summary>
    /// Returns all values as a list.
    /// </summary>
    public List<object?> Values()
    {
        var values = new List<object?>();
        foreach (var value in _entries.Values)
            values.Add(value);
        return values;
    }

    /// <summary>
    /// Returns all key-value pairs as a list of Pair struct instances with .key and .value fields.
    /// </summary>
    public List<object?> Pairs()
    {
        var pairs = new List<object?>();
        foreach (var kvp in _entries)
        {
            var fields = new Dictionary<string, object?> { { "key", kvp.Key }, { "value", kvp.Value } };
            pairs.Add(new StashInstance("Pair", fields));
        }
        return pairs;
    }

    /// <summary>
    /// Returns the raw key-value entries for internal use (avoids creating StashInstance wrappers).
    /// </summary>
    public IEnumerable<KeyValuePair<object, object?>> RawEntries()
    {
        return _entries;
    }

    /// <summary>
    /// Returns an enumerable of all keys for iteration support.
    /// </summary>
    public IEnumerable<object?> IterableKeys()
    {
        foreach (var key in _entries.Keys)
            yield return key;
    }

    public override string ToString()
    {
        return "<dict>";
    }

    private static void ValidateKey(object key)
    {
        if (key is string or long or double or bool)
            return;
        throw new RuntimeError($"Dictionary keys must be string, int, float, or bool. Got '{key.GetType().Name}'.");
    }
}
