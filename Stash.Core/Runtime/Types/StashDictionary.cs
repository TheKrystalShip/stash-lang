namespace Stash.Runtime.Types;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Represents a dictionary — an unordered collection of key-value pairs.
/// Keys must be value types: string, int (long), float (double), or bool.
/// </summary>
public class StashDictionary
{
    private readonly Dictionary<object, object?> _entries = new();

    public int Count => _entries.Count;

    public void Set(object key, object? value)
    {
        ValidateKey(key);
        _entries[key] = value;
    }

    public object? Get(object key)
    {
        return _entries.TryGetValue(key, out var value) ? value : null;
    }

    public bool Has(object key)
    {
        return _entries.ContainsKey(key);
    }

    public bool Remove(object key)
    {
        return _entries.Remove(key);
    }

    public void Clear()
    {
        _entries.Clear();
    }

    public List<object?> Keys()
    {
        var keys = new List<object?>();
        foreach (var key in _entries.Keys)
        {
            keys.Add(key);
        }
        return keys;
    }

    public List<object?> Values()
    {
        var values = new List<object?>();
        foreach (var value in _entries.Values)
        {
            values.Add(value);
        }
        return values;
    }

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

    public IEnumerable<KeyValuePair<object, object?>> RawEntries()
    {
        return _entries.ToList();
    }

    public IEnumerable<KeyValuePair<object, object?>> GetAllEntries()
    {
        return _entries.ToList();
    }

    public IEnumerable<object?> IterableKeys()
    {
        foreach (var key in _entries.Keys)
        {
            yield return key;
        }
    }

    public override string ToString()
    {
        return $"<dict({Count})>";
    }

    private static void ValidateKey(object key)
    {
        if (key is string or long or double or bool)
        {
            return;
        }

        throw new RuntimeError($"Dictionary keys must be string, int, float, or bool. Got '{key.GetType().Name}'.");
    }
}
