namespace Stash.Runtime.Types;

using System.Collections.Generic;
using System.Linq;
using Stash.Runtime;
using Stash.Runtime.Protocols;
using Stash.Common;

/// <summary>
/// Represents a dictionary — an unordered collection of key-value pairs.
/// Keys must be value types: string, int (long), float (double), or bool.
/// </summary>
public class StashDictionary : IVMTyped, IVMFieldAccessible, IVMFieldMutable, IVMIterable, IVMIndexable, IVMSized, IVMStringifiable
{
    private readonly Dictionary<object, StashValue> _entries = new();

    public int Count => _entries.Count;

    public void Set(object key, StashValue value)
    {
        ValidateKey(key);
        _entries[key] = value;
    }

    public StashValue Get(object key)
    {
        return _entries.TryGetValue(key, out var value) ? value : StashValue.Null;
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

    public List<StashValue> Keys()
    {
        var keys = new List<StashValue>(_entries.Count);
        foreach (var key in _entries.Keys)
        {
            keys.Add(StashValue.FromObject(key));
        }
        return keys;
    }

    /// <summary>Returns dict keys as raw objects for internal use (dict lookups, serialization).</summary>
    public List<object> RawKeys()
    {
        var keys = new List<object>(_entries.Count);
        foreach (var key in _entries.Keys)
        {
            keys.Add(key);
        }
        return keys;
    }

    public List<StashValue> Values()
    {
        var values = new List<StashValue>(_entries.Count);
        foreach (var value in _entries.Values)
        {
            values.Add(value);
        }
        return values;
    }

    public List<StashValue> Pairs()
    {
        var pairs = new List<StashValue>(_entries.Count);
        foreach (var kvp in _entries)
        {
            var fields = new Dictionary<string, StashValue> { { "key", StashValue.FromObject(kvp.Key) }, { "value", kvp.Value } };
            pairs.Add(StashValue.FromObj(new StashInstance("Pair", fields)));
        }
        return pairs;
    }

    public IEnumerable<KeyValuePair<object, StashValue>> RawEntries()
    {
        return _entries.ToList();
    }

    public IEnumerable<KeyValuePair<object, StashValue>> GetAllEntries()
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

    // --- Protocol implementations ---

    public string VMTypeName => "dict";

    public bool VMTryGetField(string name, out StashValue value, SourceSpan? span)
    {
        if (Has(name))
        {
            value = Get(name);
            return true;
        }
        value = default;
        return false;
    }

    public void VMSetField(string name, StashValue value, SourceSpan? span)
    {
        Set(name, value);
    }

    public IVMIterator VMGetIterator(bool indexed)
    {
        return new StashDictionaryIterator(this, indexed);
    }

    public StashValue VMGetIndex(StashValue index, SourceSpan? span)
    {
        if (index.IsNull)
            throw new RuntimeError("Dictionary key cannot be null.", span);
        return Get(index.ToObject()!);
    }

    public void VMSetIndex(StashValue index, StashValue value, SourceSpan? span)
    {
        if (index.IsNull)
            throw new RuntimeError("Dictionary key cannot be null.", span);
        Set(index.ToObject()!, value);
    }

    public long VMLength => Count;

    public string VMToString() => RuntimeValues.Stringify(this);
}

internal sealed class StashDictionaryIterator : IVMIterator
{
    private readonly IEnumerator<KeyValuePair<object, StashValue>> _enumerator;
    private readonly bool _indexed;
    private int _index;

    public StashDictionaryIterator(StashDictionary dict, bool indexed)
    {
        _enumerator = dict.GetAllEntries().GetEnumerator();
        _indexed = indexed;
        _index = -1;
    }

    public bool MoveNext()
    {
        _index++;
        return _enumerator.MoveNext();
    }

    public StashValue Current => _indexed ? _enumerator.Current.Value : StashValue.FromObj(_enumerator.Current.Key);
    public StashValue CurrentKey => _indexed ? StashValue.FromObj(_enumerator.Current.Key) : StashValue.FromInt(_index);
}
