namespace Stash.Runtime.Types;

using System.Collections.Generic;
using System.Linq;
using Stash.Runtime;
using Stash.Runtime.Errors;
using Stash.Runtime.Protocols;
using Stash.Common;

/// <summary>
/// Represents a dictionary — an unordered collection of key-value pairs.
/// Keys are any non-null <see cref="StashValue"/>; equality and hashing use
/// <see cref="StashEquality.SameValueZero"/> so that, for example, integer key
/// <c>1</c> and float key <c>1.0</c> are the same dictionary key.
/// Non-primitive types (arrays, dicts, structs, secrets, functions, …) key by
/// reference identity per §Equality DE4.
/// </summary>
public class StashDictionary : IVMTyped, IVMFieldAccessible, IVMFieldMutable, IVMIterable, IVMIndexable, IVMSized, IVMStringifiable
{
    private readonly Dictionary<StashValue, StashValue> _entries = new(StashEquality.SameValueZero);
    private bool _frozen;

    public int Count => _entries.Count;

    /// <summary>Whether this dictionary is frozen (write operations throw).</summary>
    public bool IsFrozen => _frozen;

    /// <summary>
    /// Freezes the dictionary, making all subsequent write operations throw
    /// <see cref="ReadOnlyError"/>. Used by the DataMember read path to honour the
    /// side-effect contract: reference-typed returns are frozen at the boundary.
    /// </summary>
    public void Freeze() => _frozen = true;

    public void Set(StashValue key, StashValue value)
    {
        if (_frozen)
            throw new ReadOnlyError("Cannot mutate a frozen dictionary.");
        ValidateKey(key);
        _entries[key] = value;
    }

    // Convenience overload for C# string keys (field access, marshalling).
    public void Set(string key, StashValue value) => Set(StashValue.FromObj(key), value);

    public StashValue Get(StashValue key)
    {
        return _entries.TryGetValue(key, out var value) ? value : StashValue.Null;
    }

    // Convenience overload for C# string keys (field access, marshalling).
    public StashValue Get(string key) => Get(StashValue.FromObj(key));

    public bool Has(StashValue key)
    {
        return _entries.ContainsKey(key);
    }

    // Convenience overload for C# string keys (field access, marshalling).
    public bool Has(string key) => Has(StashValue.FromObj(key));

    public bool Remove(StashValue key)
    {
        if (_frozen)
            throw new ReadOnlyError("Cannot mutate a frozen dictionary.");
        return _entries.Remove(key);
    }

    // Convenience overload for C# string keys (field access, marshalling).
    public bool Remove(string key) => Remove(StashValue.FromObj(key));

    public void Clear()
    {
        if (_frozen)
            throw new ReadOnlyError("Cannot mutate a frozen dictionary.");
        _entries.Clear();
    }

    public StashArray Keys()
    {
        var keys = new StashArray(_entries.Count);
        foreach (var key in _entries.Keys)
        {
            keys.Add(key);
        }
        return keys;
    }

    /// <summary>Returns dict keys as raw StashValues for internal use (dict lookups, serialization).</summary>
    public List<StashValue> RawKeys()
    {
        var keys = new List<StashValue>(_entries.Count);
        foreach (var key in _entries.Keys)
        {
            keys.Add(key);
        }
        return keys;
    }

    public StashArray Values()
    {
        var values = new StashArray(_entries.Count);
        foreach (var value in _entries.Values)
        {
            values.Add(value);
        }
        return values;
    }

    public StashArray Pairs()
    {
        var pairs = new StashArray(_entries.Count);
        foreach (var kvp in _entries)
        {
            var fields = new Dictionary<string, StashValue> { { "key", kvp.Key }, { "value", kvp.Value } };
            pairs.Add(StashValue.FromObj(new StashInstance("Pair", fields)));
        }
        return pairs;
    }

    public IEnumerable<KeyValuePair<StashValue, StashValue>> RawEntries()
    {
        return _entries.ToList();
    }

    /// <summary>
    /// Yields the raw entries using the Dictionary's version-checked struct enumerator
    /// (not <c>ToList()</c> / <c>CopyTo</c>). This is the safe form to use when the
    /// enumeration must detect a concurrent structural mutation (Add/Remove on the owner
    /// thread) and throw <see cref="System.InvalidOperationException"/> rather than
    /// silently producing a torn snapshot.
    ///
    /// <para>
    /// Use this method inside bounded-retry snapshot loops (e.g.
    /// <c>RuntimeValues.DeepCloneDictionary</c>) that need cross-thread read safety
    /// for single-writer dictionaries.  Do not use it on the hot same-thread path —
    /// <c>RawEntries()</c> materialises eagerly and is simpler there.
    /// </para>
    /// </summary>
    public IEnumerable<KeyValuePair<StashValue, StashValue>> RawEntriesEnumerable()
    {
        foreach (var kv in _entries)
        {
            yield return kv;
        }
    }

    public IEnumerable<KeyValuePair<StashValue, StashValue>> GetAllEntries()
    {
        return _entries.ToList();
    }

    public IEnumerable<StashValue> IterableKeys()
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

    private static void ValidateKey(StashValue key)
    {
        if (key.IsNull)
            throw new RuntimeError("Dictionary key cannot be null.");
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
        return Get(index);
    }

    public void VMSetIndex(StashValue index, StashValue value, SourceSpan? span)
    {
        if (index.IsNull)
            throw new RuntimeError("Dictionary key cannot be null.", span);
        Set(index, value);
    }

    public long VMLength => Count;

    public string VMToString() => RuntimeValues.Stringify(this);
}

internal sealed class StashDictionaryIterator : IVMIterator
{
    private readonly IEnumerator<KeyValuePair<StashValue, StashValue>> _enumerator;
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

    public StashValue Current => _indexed ? _enumerator.Current.Value : _enumerator.Current.Key;
    public StashValue CurrentKey => _indexed ? _enumerator.Current.Key : StashValue.FromInt(_index);
}
