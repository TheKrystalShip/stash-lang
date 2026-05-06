namespace Stash.Stdlib.Abstractions;

using System;

/// <summary>
/// Optional per-parameter metadata for a <see cref="StashFnAttribute"/> method. Used to override
/// the Stash parameter name (when the C# name uses <c>@base</c> or other reserved-word escaping)
/// or to coerce the Stash type label (e.g. forcing a C# <c>double</c> parameter to be advertised
/// as Stash <c>number</c>, accepting both ints and floats).
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class StashParamAttribute : Attribute
{
    /// <summary>Optional override for the Stash parameter name.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// Optional Stash type label override. The notable case is <c>"number"</c> on a C# <c>double</c>
    /// parameter — the generator then emits <c>SvArgs.Numeric</c> instead of <c>SvArgs.Double</c>,
    /// allowing the function to accept both Stash <c>int</c> and <c>float</c> values.
    /// </summary>
    public string? Type { get; set; }
}
