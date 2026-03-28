namespace Stash.Runtime.Types;

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using Stash.Common;

/// <summary>
/// Represents a namespace — a named container for functions, structs, enums, and other members.
/// </summary>
public class StashNamespace
{
    public string Name { get; }
    private Dictionary<string, object?>? _mutableMembers = new();
    private FrozenDictionary<string, object?>? _frozenMembers;

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
        _mutableMembers[name] = value;
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

    public object? GetMember(string name, SourceSpan? span)
    {
        if (_frozenMembers is not null)
        {
            if (_frozenMembers.TryGetValue(name, out var frozenValue))
            {
                return frozenValue;
            }
        }
        else if (_mutableMembers!.TryGetValue(name, out var value))
        {
            return value;
        }

        throw new RuntimeError($"Namespace '{Name}' has no member '{name}'.", span);
    }

    public IReadOnlyDictionary<string, object?> GetAllMembers()
    {
        if (_frozenMembers is not null)
        {
            return _frozenMembers;
        }

        return _mutableMembers!;
    }

    public override string ToString() => $"<namespace {Name}>";
}
