namespace Stash.Stdlib.Abstractions;

using System;

/// <summary>
/// Marks a <c>const</c> or <c>static readonly</c> field on a <see cref="StashNamespaceAttribute"/>-decorated
/// class as a Stash built-in constant. The Stash name defaults to the C# field name verbatim
/// (case preserved); use <see cref="Name"/> to override (e.g. <c>"MAX_VALUE"</c>).
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class StashConstAttribute : Attribute
{
    /// <summary>Optional override for the Stash constant name.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// Optional override for the display value used by LSP hover. Defaults to the result of
    /// <c>value.ToString(CultureInfo.InvariantCulture)</c> — override when the runtime literal
    /// is more precise than the desired hover label.
    /// </summary>
    public string? Display { get; set; }
}
