namespace Stash.Stdlib.Abstractions;

using System;
using Stash.Runtime;

/// <summary>
/// Marks a static method on a <see cref="StashNamespaceAttribute"/>-decorated class as a Stash
/// built-in function. The source generator emits an extraction wrapper that marshals the
/// <c>ReadOnlySpan&lt;StashValue&gt;</c> incoming arguments to the typed C# parameters of the
/// method body.
/// </summary>
/// <remarks>
/// <para>The Stash function name is the C# method name with the first character lower-cased
/// (e.g. <c>Abs</c> → <c>abs</c>, <c>LastExitCode</c> → <c>lastExitCode</c>). Multi-letter
/// acronyms must follow PascalCase (<c>UrlEncode</c>, not <c>URLEncode</c>) — the generator
/// emits a build error otherwise.</para>
/// <para>If <see cref="Raw"/> is <c>true</c>, the method must already match the
/// <c>(IInterpreterContext, ReadOnlySpan&lt;StashValue&gt;) -&gt; StashValue</c> signature and
/// the generator passes it through with no auto-marshalling. Use this only when profiling
/// shows the auto-marshal path is a bottleneck.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class StashFnAttribute : Attribute
{
    /// <summary>Optional override for the Stash function name (e.g. when the desired name is a C# reserved word).</summary>
    public string? Name { get; set; }

    /// <summary>If <c>true</c>, skips auto-marshalling. The method body becomes the <c>DirectHandler</c> as-is.</summary>
    public bool Raw { get; set; }

    /// <summary>
    /// Optional override for the inferred Stash return-type label. Use this for polymorphic
    /// functions whose body returns <c>StashValue</c> (default label <c>"any"</c>) but should
    /// advertise a more specific Stash type (e.g. <c>"number"</c>, <c>"int"</c>). The wrapping
    /// of the returned C# value stays based on the actual C# return type.
    /// </summary>
    public string? ReturnType { get; set; }

    /// <summary>
    /// Optional capability requirement for this individual function. The function is only
    /// registered when its required capability is present in the active <see cref="StashCapabilities"/>
    /// set. Defaults to <see cref="StashCapabilities.None"/> (always registered).
    /// Use this when the function's enclosing namespace is not capability-gated as a whole
    /// but a single function within it must be (e.g. a globally-visible <c>exit</c>).
    /// </summary>
    public StashCapabilities Capability { get; set; } = StashCapabilities.None;

    /// <summary>
    /// Error type names this function may throw. Use <c>StashErrorTypes.*</c> constants —
    /// never literal strings — to avoid drift and aid refactoring.
    /// </summary>
    public string[]? Throws { get; set; }
}
