namespace Stash.Stdlib.Registration;

/// <summary>
/// Identifies the static kind of a namespace entry at registration time.
/// Kinds are mutually exclusive and are determined by which attribute was applied.
/// </summary>
public enum DeclarationKind
{
    /// <summary>
    /// A callable built-in function registered via <c>[StashFn]</c>.
    /// <c>ns.x</c> yields a first-class function reference; <c>ns.x(...)</c> invokes it.
    /// </summary>
    Function,

    /// <summary>
    /// A read-only, context-bound data member registered via <c>[StashMember]</c>.
    /// <c>ns.x</c> invokes the getter and yields the resulting value.
    /// <c>ns.x(...)</c> is a compile-time error (SA08xx).
    /// </summary>
    DataMember,

    /// <summary>
    /// A static constant registered via <c>[StashConst]</c>.
    /// <c>ns.x</c> yields the stored snapshot value.
    /// </summary>
    Constant,
}
