namespace Stash.Runtime;

using System;

/// <summary>
/// Single source of truth for the base error type name and the matching
/// predicate used by both <c>is</c> and <c>catch</c>.
/// </summary>
public static class ErrorTypeRegistry
{
    public const string BaseTypeName = "Error";

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
