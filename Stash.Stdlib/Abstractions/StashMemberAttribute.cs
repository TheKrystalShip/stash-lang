namespace Stash.Stdlib.Abstractions;

using System;
using Stash.Runtime;

/// <summary>
/// Marks a static method on a <see cref="StashNamespaceAttribute"/>-decorated class as a
/// read-only Stash namespace member. The method must accept exactly one
/// <c>IInterpreterContext</c> parameter; the generator treats it as a getter delegate.
/// </summary>
/// <remarks>
/// <para>The Stash member name is the C# method name with the first character lower-cased
/// (e.g. <c>Argc</c> → <c>argc</c>). Multi-letter acronyms must follow PascalCase.</para>
/// <para><see cref="StashMemberAttribute"/> is mutually exclusive with both
/// <see cref="StashFnAttribute"/> and <see cref="StashConstAttribute"/>. The source generator
/// raises a build-time diagnostic when any combination appears on the same symbol.</para>
/// <para>Every <c>[StashMember]</c>-annotated method MUST carry a non-empty XML
/// <c>&lt;summary&gt;</c> doc comment; the generator emits a build error otherwise.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class StashMemberAttribute : Attribute
{
    /// <summary>
    /// Optional override for the Stash member name (e.g. when the desired name is a C# reserved word).
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Optional override for the inferred Stash return-type label. Use this for methods whose
    /// body returns <c>StashValue</c> (default label <c>"any"</c>) but should advertise a more
    /// specific Stash type (e.g. <c>"array"</c>, <c>"string"</c>).
    /// </summary>
    public string? ReturnType { get; set; }

    /// <summary>
    /// Optional capability requirement for this member. The member is only registered when its
    /// required capability is present in the active <see cref="StashCapabilities"/> set.
    /// Defaults to <see cref="StashCapabilities.None"/> (always registered).
    /// </summary>
    public StashCapabilities Capability { get; set; } = StashCapabilities.None;

    /// <summary>
    /// Error types this member's getter may throw. Each element must be a <c>[StashError]</c>-attributed
    /// class that inherits <c>RuntimeError</c>. The source generator validates attribute presence and
    /// emits the canonical name into the throws metadata for LSP hover and static analysis.
    /// </summary>
    public Type[]? Throws { get; set; }

    /// <summary>
    /// Evaluation strategy for this member. <see cref="Abstractions.Stability.Cached"/> (the default)
    /// invokes the getter once on first access and stores the result; subsequent accesses return the
    /// same reference. <see cref="Abstractions.Stability.Live"/> invokes the getter on every access.
    /// </summary>
    public Stability Stability { get; set; } = Stability.Cached;
}
