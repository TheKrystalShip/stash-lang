namespace Stash.Stdlib.Abstractions;

using System;

/// <summary>
/// Optional per-field metadata on a <see cref="StashStructAttribute"/>-decorated type.
/// Overrides the Stash field name (default: C# property name with first character lower-cased)
/// or the Stash type label (default: inferred from the C# property type).
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class StashFieldAttribute : Attribute
{
    /// <summary>Optional override for the Stash field name.</summary>
    public string? Name { get; set; }

    /// <summary>Optional override for the Stash field type label (e.g. <c>"array"</c>, <c>"dict"</c>).</summary>
    public string? Type { get; set; }
}
