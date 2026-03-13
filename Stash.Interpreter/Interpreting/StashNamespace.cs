namespace Stash.Interpreting;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using Stash.Common;

/// <summary>
/// Represents a namespace — a named container for functions, structs, enums, and other members.
/// </summary>
public class StashNamespace
{
    public string Name { get; }
    private readonly Dictionary<string, object?> _members = new();

    public StashNamespace(string name)
    {
        Name = name;
    }

    public void Define(string name, object? value) => _members[name] = value;

    public bool HasMember(string name) => _members.ContainsKey(name);

    public object? GetMember(string name, SourceSpan? span)
    {
        if (_members.TryGetValue(name, out var value))
        {
            return value;
        }

        throw new RuntimeError($"Namespace '{Name}' has no member '{name}'.", span);
    }

    public IReadOnlyDictionary<string, object?> GetAllMembers() =>
        new ReadOnlyDictionary<string, object?>(_members);

    public override string ToString() => $"<namespace {Name}>";
}
