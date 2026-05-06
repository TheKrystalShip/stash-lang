namespace Stash.Stdlib.Abstractions;

using System;
using Stash.Runtime;

/// <summary>
/// Marks a static partial class as a Stash built-in namespace. The source generator inspects
/// the class members and emits a <c>Define()</c> method that registers all <see cref="StashFnAttribute"/>,
/// <see cref="StashConstAttribute"/>, <see cref="StashStructAttribute"/>, and <see cref="StashEnumAttribute"/>
/// members against the existing <c>NamespaceBuilder</c> API.
/// </summary>
/// <remarks>
/// <para>By convention the namespace name is derived from the class name with the <c>BuiltIns</c>
/// suffix stripped and lowercased, e.g. <c>MathBuiltIns</c> → <c>math</c>. Use <see cref="Name"/>
/// only as an escape hatch when the class naming convention can't satisfy the desired Stash name.</para>
/// <para>The class must be both <c>partial</c> and <c>static</c>; the generator emits the
/// <c>Define()</c> method into the partial.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class StashNamespaceAttribute : Attribute
{
    /// <summary>Optional override for the Stash namespace name. Leave unset to use the convention.</summary>
    public string? Name { get; set; }

    /// <summary>The capability required for this namespace to be registered. Defaults to <see cref="StashCapabilities.None"/>.</summary>
    public StashCapabilities Capability { get; set; } = StashCapabilities.None;
}
