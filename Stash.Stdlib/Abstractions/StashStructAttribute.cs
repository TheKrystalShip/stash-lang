namespace Stash.Stdlib.Abstractions;

using System;

/// <summary>
/// Marks a class, struct, or record as a Stash built-in struct type. Field names lower-case the
/// first character of the C# property name (or use <see cref="StashFieldAttribute"/> to override).
/// The Stash struct name is the C# type name verbatim.
/// </summary>
/// <remarks>
/// The decorated C# type is for declaration only — the generator produces a <c>StashStruct</c>
/// runtime type using the inspected field metadata, not by instantiating the C# type.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class StashStructAttribute : Attribute
{
    /// <summary>Optional override for the Stash struct name.</summary>
    public string? Name { get; set; }
}
