namespace Stash.Debugging;

using System.Collections.Generic;

/// <summary>
/// Abstraction over a variable scope for debugger inspection.
/// Implemented by <c>Environment</c> (tree-walk) and VM frame adapters (bytecode).
/// </summary>
public interface IDebugScope
{
    /// <summary>Returns all variable bindings in this scope.</summary>
    IEnumerable<KeyValuePair<string, object?>> GetAllBindings();

    /// <summary>The enclosing (parent) scope, or null for top-level/global.</summary>
    IDebugScope? EnclosingScope { get; }

    /// <summary>Walks the scope chain from this scope to the global scope.</summary>
    IEnumerable<IDebugScope> GetScopeChain()
    {
        IDebugScope? current = this;
        while (current is not null)
        {
            yield return current;
            current = current.EnclosingScope;
        }
    }

    /// <summary>Returns true if this scope contains a variable with the given name.</summary>
    bool Contains(string name) => false;

    /// <summary>Returns true if the named variable is a constant that cannot be reassigned.</summary>
    bool IsConstant(string name) => false;

    /// <summary>Attempts to assign a new value to the named variable. Returns true on success.</summary>
    bool TryAssign(string name, object? value) => false;
}
