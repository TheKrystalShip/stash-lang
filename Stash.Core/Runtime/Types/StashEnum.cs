namespace Stash.Runtime.Types;

using System.Collections.Generic;
using Stash.Common;
using Stash.Runtime.Protocols;

/// <summary>
/// Represents an enum declaration — a named type with a list of member names.
/// </summary>
public class StashEnum : IVMTyped, IVMFieldAccessible, IVMIterable, IVMStringifiable
{
    public string Name { get; }
    public List<string> Members { get; }
    private readonly Dictionary<string, StashEnumValue> _values;

    /// <summary>True when this enum was registered by the standard library, not defined in user script.</summary>
    public bool IsBuiltIn { get; init; }

    public StashEnum(string name, List<string> members)
    {
        Name = name;
        Members = members;
        _values = new Dictionary<string, StashEnumValue>();
        foreach (string member in members)
        {
            _values[member] = new StashEnumValue(name, member);
        }
    }

    public StashEnumValue? GetMember(string name)
    {
        return _values.TryGetValue(name, out var value) ? value : null;
    }

    public override string ToString() => $"<enum {Name}>";

    // --- Protocol implementations ---

    public string VMTypeName => "enum";

    public bool VMTryGetField(string name, out StashValue value, SourceSpan? span)
    {
        StashEnumValue? member = GetMember(name);
        if (member is not null)
        {
            value = StashValue.FromObj(member);
            return true;
        }
        value = default;
        return false;
    }

    public IVMIterator VMGetIterator(bool indexed)
    {
        return new StashEnumIterator(this);
    }

    public string VMToString() => ToString();
}

internal sealed class StashEnumIterator : IVMIterator
{
    private readonly List<StashValue> _members;
    private int _index;

    public StashEnumIterator(StashEnum enumDef)
    {
        _members = new List<StashValue>(enumDef.Members.Count);
        foreach (string m in enumDef.Members)
        {
            StashEnumValue? ev = enumDef.GetMember(m);
            _members.Add(ev != null ? StashValue.FromObj(ev) : StashValue.Null);
        }
        _index = -1;
    }

    public bool MoveNext()
    {
        _index++;
        return _index < _members.Count;
    }

    public StashValue Current => _members[_index];
    public StashValue CurrentKey => StashValue.FromInt(_index);
}
