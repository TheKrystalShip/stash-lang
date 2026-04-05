namespace Stash.Debugging;

/// <summary>
/// Classifies the role of a scope in the debugger's scope-chain view.
/// </summary>
/// <remarks>
/// Used by <see cref="DebugScope"/> to label each scope tier and to determine the
/// display name shown in the DAP <c>scopes</c> response. The three values map
/// directly to the conventional scope categories rendered by DAP clients such as
/// VS Code's Variables pane.
/// </remarks>
public enum ScopeKind
{
    /// <summary>Variables declared in the innermost function or block currently executing.</summary>
    Local,

    /// <summary>
    /// Variables captured from one or more enclosing functions — the lexical closure
    /// of the currently executing function.
    /// </summary>
    Closure,

    /// <summary>Variables declared at the top-level (module/global) scope.</summary>
    Global,
}

/// <summary>
/// Represents a single named scope in the debugger's scope chain for variable inspection.
/// </summary>
/// <remarks>
/// <para>
/// A list of <see cref="DebugScope"/> objects is produced for each <see cref="CallFrame"/>
/// by walking the frame's <see cref="CallFrame.LocalScope"/> chain and classifying each
/// <see cref="IDebugScope"/> tier as
/// <see cref="ScopeKind.Local"/>, <see cref="ScopeKind.Closure"/>, or
/// <see cref="ScopeKind.Global"/>.
/// </para>
/// <para>
/// This corresponds to the DAP <c>Scope</c> type returned in <c>scopes</c> responses.
/// Each scope's variables are enumerated via
/// <see cref="IDebugScope.GetAllBindings"/>.
/// </para>
/// </remarks>
public class DebugScope
{
    /// <summary>
    /// Gets the classification of this scope (local, closure, or global).
    /// </summary>
    public ScopeKind Kind { get; }

    /// <summary>
    /// Gets the human-readable display name for this scope as shown in the debugger UI
    /// (for example, <c>"Local"</c>, <c>"Closure"</c>, or <c>"Global"</c>).
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the <see cref="IDebugScope"/> that backs
    /// this scope and holds its variable bindings.
    /// </summary>
    public IDebugScope Scope { get; }

    /// <summary>
    /// Gets the number of variables currently bound in this scope.
    /// </summary>
    /// <remarks>
    /// Computed by enumerating <see cref="IDebugScope.GetAllBindings"/>
    /// on demand; the value reflects the live state of the scope.
    /// </remarks>
    public int VariableCount => System.Linq.Enumerable.Count(Scope.GetAllBindings());

    /// <summary>
    /// Initialises a new <see cref="DebugScope"/> with the specified classification,
    /// display name, and backing environment.
    /// </summary>
    /// <param name="kind">The <see cref="ScopeKind"/> that classifies this scope.</param>
    /// <param name="name">The display name shown in the debugger Variables pane.</param>
    /// <param name="scope">
    /// The <see cref="IDebugScope"/> whose bindings this scope exposes.
    /// </param>
    public DebugScope(ScopeKind kind, string name, IDebugScope scope)
    {
        Kind = kind;
        Name = name;
        Scope = scope;
    }
}
