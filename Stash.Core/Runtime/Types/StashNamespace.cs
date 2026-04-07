namespace Stash.Runtime.Types;

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Stash.Common;

/// <summary>
/// Represents a namespace — a named container for functions, structs, enums, and other members.
/// </summary>
public class StashNamespace
{
    public string Name { get; }
    public bool IsBuiltIn { get; init; }
    private Dictionary<string, StashValue>? _mutableMembers = new();
    private FrozenDictionary<string, StashValue>? _frozenMembers;

    public StashNamespace(string name)
    {
        Name = name;
    }

    public void Define(string name, object? value)
    {
        if (_mutableMembers is null)
        {
            throw new InvalidOperationException($"Namespace '{Name}' is frozen and cannot be modified.");
        }
        _mutableMembers[name] = StashValue.FromObject(value);
    }

    public void Freeze()
    {
        if (_mutableMembers is not null)
        {
            _frozenMembers = _mutableMembers.ToFrozenDictionary();
            _mutableMembers = null;
        }
    }

    public bool HasMember(string name)
    {
        if (_frozenMembers is not null)
        {
            return _frozenMembers.ContainsKey(name);
        }

        return _mutableMembers!.ContainsKey(name);
    }

    /// <summary>
    /// Returns the member as a StashValue directly — zero-allocation hot path.
    /// </summary>
    public StashValue GetMemberValue(string name, SourceSpan? span)
    {
        if (_frozenMembers is not null)
        {
            if (_frozenMembers.TryGetValue(name, out StashValue frozenValue))
            {
                return frozenValue;
            }
        }
        else if (_mutableMembers!.TryGetValue(name, out StashValue value))
        {
            return value;
        }

        throw new RuntimeError($"Namespace '{Name}' has no member '{name}'.", span);
    }

    public object? GetMember(string name, SourceSpan? span)
    {
        return GetMemberValue(name, span).ToObject();
    }

    /// <summary>
    /// Returns all members as StashValues — avoids FromObject round-trip.
    /// </summary>
    public IReadOnlyDictionary<string, StashValue> GetAllMemberValues()
    {
        if (_frozenMembers is not null) return _frozenMembers;
        return _mutableMembers!;
    }

    public IReadOnlyDictionary<string, object?> GetAllMembers()
    {
        if (_frozenMembers is not null)
            return _frozenMembers.ToDictionary(kv => kv.Key, kv => (object?)kv.Value.ToObject());
        return _mutableMembers!.ToDictionary(kv => kv.Key, kv => (object?)kv.Value.ToObject());
    }

    public override string ToString() => $"<namespace {Name}>";
}
