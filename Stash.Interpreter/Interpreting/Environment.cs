namespace Stash.Interpreting;

using System.Collections.Generic;

/// <summary>
/// Stores variable bindings for a lexical scope. Each environment has an optional
/// reference to an enclosing (parent) environment, forming a scope chain.
/// Global scopes use dictionary storage; local scopes use slot-based arrays for performance.
/// </summary>
public class Environment
{
    /// <summary>Dictionary storage for the global scope. Null for local scopes.</summary>
    private readonly Dictionary<string, object?>? _values;
    /// <summary>Constant names for the global scope. Null for local scopes.</summary>
    private readonly HashSet<string>? _constants;

    /// <summary>Slot array for fast indexed variable access in local scopes. Null for global scope.</summary>
    private object?[]? _slots;
    /// <summary>Parallel array mapping slot indices to variable names. Used for chain-walk fallback and debugger.</summary>
    private string[]? _slotNames;
    /// <summary>How many slots are currently used.</summary>
    private int _slotCount;
    /// <summary>Which slot indices are constants (lazily allocated).</summary>
    private HashSet<int>? _constSlots;

    /// <summary>
    /// The enclosing (parent) scope, or <c>null</c> for the global scope.
    /// </summary>
    public Environment? Enclosing { get; }

    /// <summary>
    /// Creates a new global environment (no enclosing scope). Uses dictionary storage.
    /// </summary>
    public Environment()
    {
        Enclosing = null;
        _values = new Dictionary<string, object?>();
        _constants = new HashSet<string>();
    }

    /// <summary>
    /// Creates a new local environment enclosed by the given parent scope. Uses slot storage.
    /// </summary>
    public Environment(Environment enclosing)
    {
        Enclosing = enclosing;
        _slots = new object?[4];
        _slotNames = new string[4];
    }

    /// <summary>
    /// Defines a new variable in this scope.
    /// </summary>
    public void Define(string name, object? value)
    {
        if (_values is not null)
        {
            _values[name] = value;
            _constants!.Remove(name);
        }
        else if (_slots is not null)
        {
            EnsureSlotCapacity();
            _slotNames![_slotCount] = name;
            _slots[_slotCount++] = value;
        }
    }

    /// <summary>
    /// Defines a new constant in this scope. Constants cannot be reassigned.
    /// </summary>
    public void DefineConstant(string name, object? value)
    {
        if (_values is not null)
        {
            _values[name] = value;
            _constants!.Add(name);
        }
        else if (_slots is not null)
        {
            EnsureSlotCapacity();
            _slotNames![_slotCount] = name;
            _constSlots ??= new HashSet<int>();
            _constSlots.Add(_slotCount);
            _slots[_slotCount++] = value;
        }
    }

    /// <summary>
    /// Looks up a variable by name, walking up the scope chain.
    /// Handles both dictionary-based (global) and slot-based (local) environments.
    /// </summary>
    public object? Get(string name, Common.SourceSpan? span = null)
    {
        var env = this;
        while (env is not null)
        {
            if (env._values is not null)
            {
                if (env._values.TryGetValue(name, out object? value))
                {
                    return value;
                }
            }
            else if (env._slots is not null)
            {
                for (int i = 0; i < env._slotCount; i++)
                {
                    if (env._slotNames![i] == name)
                    {
                        return env._slots[i];
                    }
                }
            }
            env = env.Enclosing;
        }
        throw new RuntimeError($"Undefined variable '{name}'.", span);
    }

    /// <summary>
    /// Assigns a value to an existing variable, walking up the scope chain.
    /// Throws if the variable is a constant or undefined.
    /// </summary>
    public void Assign(string name, object? value, Common.SourceSpan? span = null)
    {
        var env = this;
        while (env is not null)
        {
            if (env._values is not null)
            {
                if (env._values.ContainsKey(name))
                {
                    if (env._constants!.Contains(name))
                    {
                        throw new RuntimeError($"Cannot reassign constant '{name}'.", span);
                    }
                    env._values[name] = value;
                    return;
                }
            }
            else if (env._slots is not null)
            {
                for (int i = 0; i < env._slotCount; i++)
                {
                    if (env._slotNames![i] == name)
                    {
                        if (env._constSlots is not null && env._constSlots.Contains(i))
                        {
                            throw new RuntimeError($"Cannot reassign constant '{name}'.", span);
                        }
                        env._slots[i] = value;
                        return;
                    }
                }
            }
            env = env.Enclosing;
        }
        throw new RuntimeError($"Undefined variable '{name}'.", span);
    }

    /// <summary>
    /// Gets a variable from the ancestor environment at the given distance.
    /// Used by the resolver for O(1) variable lookup (name-based).
    /// </summary>
    public object? GetAt(int distance, string name)
    {
        var env = Ancestor(distance);
        if (env._values is not null)
        {
            return env._values[name];
        }
        // Slot-based: scan names
        for (int i = 0; i < env._slotCount; i++)
        {
            if (env._slotNames![i] == name)
            {
                return env._slots![i];
            }
        }
        throw new RuntimeError($"Undefined variable '{name}'.");
    }

    /// <summary>
    /// Assigns a value in the ancestor environment at the given distance (name-based).
    /// </summary>
    public void AssignAt(int distance, string name, object? value, Common.SourceSpan? span = null)
    {
        var env = Ancestor(distance);

        if (env._values is not null)
        {
            if (env._constants!.Contains(name))
            {
                throw new RuntimeError($"Cannot reassign constant '{name}'.", span);
            }
            env._values[name] = value;
            return;
        }
        // Slot-based: scan names
        for (int i = 0; i < env._slotCount; i++)
        {
            if (env._slotNames![i] == name)
            {
                if (env._constSlots is not null && env._constSlots.Contains(i))
                {
                    throw new RuntimeError($"Cannot reassign constant '{name}'.", span);
                }
                env._slots![i] = value;
                return;
            }
        }
        throw new RuntimeError($"Undefined variable '{name}'.", span);
    }

    /// <summary>
    /// Gets a variable from the ancestor environment at the given distance using a slot index.
    /// Fastest lookup path — used when the resolver has pre-computed slot indices.
    /// </summary>
    public object? GetAtSlot(int distance, int slot)
    {
        return Ancestor(distance)._slots![slot];
    }

    /// <summary>
    /// Assigns a value in the ancestor environment at the given distance using a slot index.
    /// </summary>
    public void SetAtSlot(int distance, int slot, string name, object? value, Common.SourceSpan? span = null)
    {
        var env = Ancestor(distance);

        if (env._constSlots is not null && env._constSlots.Contains(slot))
        {
            throw new RuntimeError($"Cannot reassign constant '{name}'.", span);
        }

        env._slots![slot] = value;
    }

    private Environment Ancestor(int distance)
    {
        Environment env = this;

        for (int i = 0; i < distance; i++)
        {
            env = env.Enclosing!;
        }

        return env;
    }

    /// <summary>
    /// Grows slot arrays when capacity is exceeded.
    /// </summary>
    private void EnsureSlotCapacity()
    {
        if (_slotCount >= _slots!.Length)
        {
            int newCapacity = _slots.Length * 2;
            System.Array.Resize(ref _slots, newCapacity);
            System.Array.Resize(ref _slotNames, newCapacity);
        }
    }

    /// <summary>
    /// Gets all bindings defined in this scope (does not include parent scopes).
    /// </summary>
    public IEnumerable<KeyValuePair<string, object?>> GetAllBindings()
    {
        if (_values is not null)
        {
            return _values;
        }
        // Reconstruct from slots for local scopes
        var bindings = new Dictionary<string, object?>(_slotCount);
        for (int i = 0; i < _slotCount; i++)
        {
            bindings[_slotNames![i]] = _slots![i];
        }
        return bindings;
    }

    /// <summary>
    /// Tries to look up a variable by name in this scope only (does not walk the chain).
    /// Returns true if found, false otherwise. Does not throw.
    /// Useful for debugger variable inspection without side effects.
    /// </summary>
    public bool TryGet(string name, out object? value)
    {
        if (_values is not null)
        {
            return _values.TryGetValue(name, out value);
        }
        for (int i = 0; i < _slotCount; i++)
        {
            if (_slotNames![i] == name)
            {
                value = _slots![i];
                return true;
            }
        }
        value = null;
        return false;
    }

    /// <summary>
    /// Checks whether a variable is defined in this scope (does not walk the chain).
    /// </summary>
    public bool Contains(string name)
    {
        if (_values is not null)
        {
            return _values.ContainsKey(name);
        }
        for (int i = 0; i < _slotCount; i++)
        {
            if (_slotNames![i] == name)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Checks whether a variable is a constant in this scope.
    /// </summary>
    public bool IsConstant(string name)
    {
        if (_constants is not null)
        {
            return _constants.Contains(name);
        }
        if (_constSlots is null)
        {
            return false;
        }
        for (int i = 0; i < _slotCount; i++)
        {
            if (_slotNames![i] == name)
            {
                return _constSlots.Contains(i);
            }
        }
        return false;
    }

    /// <summary>
    /// Enumerates this scope and all enclosing scopes up to (and including) the global scope.
    /// Useful for debugger scope chain inspection — the caller can categorize each
    /// environment as local, closure, or global based on position in the chain.
    /// </summary>
    public IEnumerable<Environment> GetScopeChain()
    {
        var current = this;
        while (current is not null)
        {
            yield return current;
            current = current.Enclosing;
        }
    }

    /// <summary>
    /// Private constructor for creating snapshot copies with a specific enclosing scope.
    /// </summary>
    private Environment(Environment? enclosing, bool isGlobal)
    {
        Enclosing = enclosing;
        if (isGlobal)
        {
            _values = new Dictionary<string, object?>();
            _constants = new HashSet<string>();
        }
        else
        {
            _slots = new object?[4];
            _slotNames = new string[4];
        }
    }

    /// <summary>
    /// Creates a deep copy of the given environment chain for snapshot isolation.
    /// The global scope (root of the chain) is shared by reference — it contains
    /// frozen namespaces and immutable built-in bindings. Local scopes are deep-copied
    /// with their slot values recursively cloned via <see cref="RuntimeValues.DeepCopy"/>.
    /// </summary>
    public static Environment Snapshot(Environment source)
    {
        // Global scope is intentionally shared by reference — it contains frozen
        // built-in namespaces and constant definitions. User code that defines
        // mutable globals must use task-local state to avoid races.
        if (source._values is not null)
        {
            return source;
        }

        // First, snapshot the enclosing chain recursively
        Environment? snapshotEnclosing = source.Enclosing is not null
            ? Snapshot(source.Enclosing)
            : null;

        // Create a new local environment linked to the snapshotted enclosing
        var snapshot = new Environment(snapshotEnclosing, isGlobal: false);

        // Deep-copy slot data
        if (source._slots is not null)
        {
            int count = source._slotCount;
            snapshot._slots = new object?[source._slots.Length];
            snapshot._slotNames = new string[source._slotNames!.Length];
            snapshot._slotCount = count;

            for (int i = 0; i < count; i++)
            {
                snapshot._slots[i] = RuntimeValues.DeepCopy(source._slots[i]);
                snapshot._slotNames[i] = source._slotNames[i];
            }

            // Copy constant slot markers
            if (source._constSlots is not null)
            {
                snapshot._constSlots = new HashSet<int>(source._constSlots);
            }
        }

        return snapshot;
    }
}
