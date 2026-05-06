namespace Stash.Stdlib.Abstractions;

using System;

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
}
