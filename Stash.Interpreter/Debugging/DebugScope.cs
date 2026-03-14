namespace Stash.Debugging;

using StashEnv = Stash.Interpreting.Environment;

/// <summary>
/// Categorizes a scope in the scope chain for debugger variable inspection.
/// </summary>
public enum ScopeKind
{
    /// <summary>Variables local to the current function/block.</summary>
    Local,

    /// <summary>Variables captured from an enclosing function (closure).</summary>
    Closure,

    /// <summary>Global (top-level) variables.</summary>
    Global,
}

/// <summary>
/// Represents a single scope for debugger variable inspection.
/// Maps to DAP's Scope type.
/// </summary>
public class DebugScope
{
    /// <summary>The kind of scope (local, closure, or global).</summary>
    public ScopeKind Kind { get; }

    /// <summary>Display name for the scope (e.g., "Local", "Closure", "Global").</summary>
    public string Name { get; }

    /// <summary>The environment/scope containing the variable bindings.</summary>
    public StashEnv Environment { get; }

    /// <summary>
    /// Number of variables in this scope.
    /// </summary>
    public int VariableCount => System.Linq.Enumerable.Count(Environment.GetAllBindings());

    public DebugScope(ScopeKind kind, string name, StashEnv environment)
    {
        Kind = kind;
        Name = name;
        Environment = environment;
    }
}
