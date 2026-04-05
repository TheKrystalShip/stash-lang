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
}
