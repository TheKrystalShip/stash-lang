namespace Stash.Stdlib.Abstractions;

using System;

/// <summary>
/// Marks a <see cref="StashFnAttribute"/> method or <see cref="StashConstAttribute"/> field as
/// deprecated, pointing users at the qualified replacement name. Surfaces through the
/// existing <c>SA0830</c> analyzer diagnostic via <c>DeprecationInfo</c> on the registered
/// <c>NamespaceFunction</c> / <c>NamespaceConstant</c> metadata.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class StashDeprecatedAttribute : Attribute
{
    /// <summary>The fully qualified replacement name (e.g. <c>"env.chdir"</c>, <c>"Signal.Term"</c>).</summary>
    public string Replacement { get; }

    /// <param name="replacement">The fully qualified replacement name.</param>
    public StashDeprecatedAttribute(string replacement)
    {
        Replacement = replacement;
    }
}
