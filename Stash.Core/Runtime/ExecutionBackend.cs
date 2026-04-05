namespace Stash.Runtime;

/// <summary>
/// Specifies which execution backend the engine uses to run scripts.
/// </summary>
public enum ExecutionBackend
{
    /// <summary>Bytecode compiler + stack-based virtual machine (default, faster).</summary>
    Bytecode,

    /// <summary>Tree-walk interpreter (reference implementation, supports all features).</summary>
    TreeWalk,
}
