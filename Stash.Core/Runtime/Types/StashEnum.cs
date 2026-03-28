namespace Stash.Runtime.Types;

using System.Collections.Generic;

/// <summary>
/// Represents an enum declaration — a named type with a list of member names.
/// </summary>
public class StashEnum
{
    public string Name { get; }
    public List<string> Members { get; }
    private readonly Dictionary<string, StashEnumValue> _values;

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
}
