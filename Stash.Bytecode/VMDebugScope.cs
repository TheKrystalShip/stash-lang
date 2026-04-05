using System.Collections.Generic;
using System.Linq;
using Stash.Debugging;

namespace Stash.Bytecode;

/// <summary>
/// Adapter that exposes VM variable state as an <see cref="IDebugScope"/>.
/// Supports three modes:
/// <list type="bullet">
///   <item>Snapshot mode — immutable key/value array (used for closure scopes)</item>
///   <item>Live-stack mode — reads/writes directly to the VM's value stack (used for frame locals)</item>
///   <item>Dictionary mode — reads/writes to the VM's globals dictionary</item>
/// </list>
/// </summary>
internal sealed class VMDebugScope : IDebugScope
{
    // ── Snapshot mode ──
    private readonly KeyValuePair<string, object?>[]? _bindings;

    // ── Live-stack mode ──
    private readonly StashValue[]? _stack;
    private readonly int _baseSlot;
    private readonly string[]? _names;
    private readonly bool[]? _isConst;
    private readonly int _localCount;

    // ── Dictionary mode ──
    private readonly Dictionary<string, object?>? _globals;

    private readonly IDebugScope? _enclosing;

    /// <summary>Snapshot constructor (immutable bindings).</summary>
    public VMDebugScope(KeyValuePair<string, object?>[] bindings, IDebugScope? enclosing)
    {
        _bindings = bindings;
        _enclosing = enclosing;
    }

    /// <summary>Live-stack constructor (reads/writes VM stack slots directly).</summary>
    public VMDebugScope(StashValue[] stack, int baseSlot, int localCount,
                        string[]? names, bool[]? isConst, IDebugScope? enclosing)
    {
        _stack = stack;
        _baseSlot = baseSlot;
        _localCount = localCount;
        _names = names;
        _isConst = isConst;
        _enclosing = enclosing;
    }

    /// <summary>Dictionary constructor (reads/writes VM globals dictionary).</summary>
    public VMDebugScope(Dictionary<string, object?> globals, IDebugScope? enclosing)
    {
        _globals = globals;
        _enclosing = enclosing;
    }

    public IEnumerable<KeyValuePair<string, object?>> GetAllBindings()
    {
        if (_bindings is not null)
            return _bindings;

        if (_stack is not null)
        {
            var result = new KeyValuePair<string, object?>[_localCount];
            for (int i = 0; i < _localCount; i++)
            {
                string name = _names is not null && i < _names.Length ? _names[i] : $"local_{i}";
                object? value = (_baseSlot + i < _stack.Length) ? _stack[_baseSlot + i].ToObject() : null;
                result[i] = new KeyValuePair<string, object?>(name, value);
            }
            return result;
        }

        if (_globals is not null)
            return _globals;

        return Enumerable.Empty<KeyValuePair<string, object?>>();
    }

    public IDebugScope? EnclosingScope => _enclosing;

    public bool Contains(string name)
    {
        if (_stack is not null && _names is not null)
        {
            for (int i = 0; i < _localCount; i++)
            {
                if (_names[i] == name)
                    return true;
            }
            return false;
        }

        if (_globals is not null)
            return _globals.ContainsKey(name);

        if (_bindings is not null)
        {
            for (int i = 0; i < _bindings.Length; i++)
            {
                if (_bindings[i].Key == name)
                    return true;
            }
            return false;
        }

        return false;
    }

    public bool IsConstant(string name)
    {
        if (_stack is not null && _names is not null && _isConst is not null)
        {
            for (int i = 0; i < _localCount; i++)
            {
                if (_names[i] == name)
                    return _isConst[i];
            }
        }
        return false;
    }

    public bool TryAssign(string name, object? value)
    {
        if (_stack is not null && _names is not null)
        {
            for (int i = 0; i < _localCount; i++)
            {
                if (_names[i] == name)
                {
                    _stack[_baseSlot + i] = StashValue.FromObject(value);
                    return true;
                }
            }
            return false;
        }

        if (_globals is not null && _globals.ContainsKey(name))
        {
            _globals[name] = value;
            return true;
        }

        return false;
    }
}

