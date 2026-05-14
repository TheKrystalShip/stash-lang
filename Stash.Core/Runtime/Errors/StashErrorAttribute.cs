namespace Stash.Runtime.Errors;

using System;

/// <summary>
/// Marks a <see cref="RuntimeError"/> subclass as a built-in Stash error type.
/// The source generator (<c>StashErrorRegistryGenerator</c>) scans for this attribute
/// and emits a static <c>BuiltInErrorRegistry</c> with lookup tables by name and CLR type.
/// </summary>
/// <remarks>
/// The Stash-facing canonical name defaults to the C# class name (e.g. <c>IOError</c> →
/// <c>"IOError"</c>). Override <see cref="Name"/> only when the C# class name cannot match
/// the desired Stash name — treat <c>[StashError(Name = "...")]</c> as a code smell
/// requiring justification in a comment.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class StashErrorAttribute : Attribute
{
    /// <summary>
    /// Optional override of the Stash-facing name. Defaults to the C# class name.
    /// Supply this only when the desired Stash name cannot be the C# class name.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Names of typed properties exposed to Stash code via <c>e.&lt;property&gt;</c>.
    /// Used by the source generator to emit struct field metadata in the registry.
    /// </summary>
    public string[]? Properties { get; init; }

    /// <summary>
    /// Stash-facing types for each property listed in <see cref="Properties"/>,
    /// in the same order. Use Stash type labels: <c>"int"</c>, <c>"string"</c>,
    /// <c>"string?"</c>, <c>"bool"</c>, <c>"float"</c>, etc.
    /// Must have the same length as <see cref="Properties"/> when both are set.
    /// </summary>
    public string[]? PropertyTypes { get; init; }

    /// <summary>
    /// Short description of when this error is thrown, used in the generated
    /// standard library reference. Plain text; no Markdown.
    /// </summary>
    public string? Description { get; init; }
}
