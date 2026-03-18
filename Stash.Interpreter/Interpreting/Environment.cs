namespace Stash.Interpreting;

using System.Collections.Generic;

/// <summary>
/// Stores variable bindings for a lexical scope. Each environment has an optional
/// reference to an enclosing (parent) environment, forming a scope chain.
/// </summary>
public class Environment
{
    /// <summary>The variable bindings in this scope, mapping names to their current values.</summary>
    private readonly Dictionary<string, object?> _values = new();
    /// <summary>Set of variable names that are constants and cannot be reassigned.</summary>
    private readonly HashSet<string> _constants = new();

    /// <summary>
    /// The enclosing (parent) scope, or <c>null</c> for the global scope.
    /// </summary>
    public Environment? Enclosing { get; }

    /// <summary>
    /// Creates a new global environment (no enclosing scope).
    /// </summary>
    public Environment()
    {
        Enclosing = null;
    }

    /// <summary>
    /// Creates a new environment enclosed by the given parent scope.
    /// </summary>
    public Environment(Environment enclosing)
    {
        Enclosing = enclosing;
    }

    /// <summary>
    /// Defines a new variable in this scope.
    /// </summary>
    public void Define(string name, object? value)
    {
        _values[name] = value;
        _constants.Remove(name);
    }

    /// <summary>
    /// Defines a new constant in this scope. Constants cannot be reassigned.
    /// </summary>
    public void DefineConstant(string name, object? value)
    {
        _values[name] = value;
        _constants.Add(name);
    }

    /// <summary>
    /// Looks up a variable by name, walking up the scope chain.
    /// </summary>
    public object? Get(string name, Common.SourceSpan? span = null)
    {
        var env = this;
        while (env is not null)
        {
            if (env._values.TryGetValue(name, out object? value))
            {
                return value;
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
            if (env._values.ContainsKey(name))
            {
                if (env._constants.Contains(name))
                {
                    throw new RuntimeError($"Cannot reassign constant '{name}'.", span);
                }
                env._values[name] = value;
                return;
            }
            env = env.Enclosing;
        }
        throw new RuntimeError($"Undefined variable '{name}'.", span);
    }

    /// <summary>
    /// Gets a variable from the ancestor environment at the given distance.
    /// Used by the resolver for O(1) variable lookup.
    /// </summary>
    public object? GetAt(int distance, string name)
    {
        return Ancestor(distance)._values[name];
    }

    /// <summary>
    /// Assigns a value in the ancestor environment at the given distance.
    /// </summary>
    public void AssignAt(int distance, string name, object? value, Common.SourceSpan? span = null)
    {
        var env = Ancestor(distance);

        if (env._constants.Contains(name))
        {
            throw new RuntimeError($"Cannot reassign constant '{name}'.", span);
        }

        env._values[name] = value;
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
    /// Gets all bindings defined in this scope (does not include parent scopes).
    /// </summary>
    public IEnumerable<KeyValuePair<string, object?>> GetAllBindings() => _values;

    /// <summary>
    /// Tries to look up a variable by name in this scope only (does not walk the chain).
    /// Returns true if found, false otherwise. Does not throw.
    /// Useful for debugger variable inspection without side effects.
    /// </summary>
    public bool TryGet(string name, out object? value)
    {
        return _values.TryGetValue(name, out value);
    }

    /// <summary>
    /// Checks whether a variable is defined in this scope (does not walk the chain).
    /// </summary>
    public bool Contains(string name) => _values.ContainsKey(name);

    /// <summary>
    /// Checks whether a variable is a constant in this scope.
    /// </summary>
    public bool IsConstant(string name) => _constants.Contains(name);

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
}
