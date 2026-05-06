namespace Stash.Stdlib.Abstractions;

using System;

/// <summary>
/// Marks a C# enum as a Stash built-in enum type. The enum name and member names are taken
/// verbatim. Backing integer values are preserved as the runtime ordinal mapping.
/// </summary>
[AttributeUsage(AttributeTargets.Enum, AllowMultiple = false, Inherited = false)]
public sealed class StashEnumAttribute : Attribute
{
    /// <summary>Optional override for the Stash enum name.</summary>
    public string? Name { get; set; }
}
