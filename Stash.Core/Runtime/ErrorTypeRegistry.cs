namespace Stash.Runtime;

using System;
using System.Collections.Generic;

/// <summary>
/// Single source of truth for built-in error type names and matching semantics.
/// All type-check sites (ExecuteIs, ExecuteCatchMatch) must delegate to this class.
/// </summary>
public static class ErrorTypeRegistry
{
    public const string BaseTypeName = "Error";

    /// <summary>All known built-in error subtype names.</summary>
    private static readonly HashSet<string> _subtypes = new(StringComparer.Ordinal)
    {
        StashErrorTypes.ValueError,
        StashErrorTypes.TypeError,
        StashErrorTypes.ParseError,
        StashErrorTypes.IndexError,
        StashErrorTypes.IOError,
        StashErrorTypes.NotSupportedError,
        StashErrorTypes.TimeoutError,
        StashErrorTypes.CommandError,
        StashErrorTypes.LockError,
    };

    /// <summary>Returns true if the given type name is a known built-in error subtype.</summary>
    public static bool IsBuiltInSubtype(string typeName)
        => _subtypes.Contains(typeName);

    /// <summary>Returns true if the given type name is the base Error type.</summary>
    public static bool IsBaseType(string typeName)
        => string.Equals(typeName, BaseTypeName, StringComparison.Ordinal);

    /// <summary>
    /// Core matching predicate used by both <c>is</c> and <c>catch</c>.
    /// Returns true if a StashError with <paramref name="errorType"/> satisfies a type check
    /// against <paramref name="targetType"/>.
    /// </summary>
    public static bool Matches(string errorType, string targetType)
    {
        // Base type "Error" matches any StashError regardless of subtype
        if (IsBaseType(targetType)) return true;
        // Exact subtype match
        return string.Equals(errorType, targetType, StringComparison.Ordinal);
    }
}
