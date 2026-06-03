using System;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Stash.Registry.Contracts.Validation;

/// <summary>
/// Validates that a string value conforms to the Stash scope-name grammar:
/// a lowercase letter followed by up to 38 lowercase letters, digits, or hyphens
/// (max 39 characters total). This is the same rule enforced by
/// <c>PackageManifest.IsValidScopeName</c> in <c>Stash.Core</c>, inlined here
/// to keep <c>Stash.Registry.Contracts</c> dependency-free.
/// </summary>
/// <remarks>
/// Applies to <c>RegisterRequest.Username</c>, <c>CreateOrgRequest.Name</c>,
/// <c>CreateTeamRequest.Name</c>, and <c>ClaimScopeRequest.Scope</c> — any value
/// that must satisfy the scope-segment grammar documented in the registry spec.
/// Null and empty values pass (combine with <c>[Required]</c> to reject those).
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field,
    AllowMultiple = false)]
public sealed class ScopeGrammarAttribute : ValidationAttribute
{
    // The scope-segment grammar: ^[a-z][a-z0-9-]{0,38}$
    // Must match PackagingRegexes.ScopeSegment() in Stash.Core/Common/PackageManifest.cs.
    private static readonly Regex ScopeSegmentRegex =
        new Regex(@"^[a-z][a-z0-9-]{0,38}$", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// Initialises a new <see cref="ScopeGrammarAttribute"/> with the default error message.
    /// </summary>
    public ScopeGrammarAttribute()
        : base("The value must start with a lowercase letter and contain only lowercase letters, digits, or hyphens (max 39 characters).")
    {
    }

    /// <inheritdoc />
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null)
            return ValidationResult.Success;

        string? str = value as string;
        if (str is null)
            return new ValidationResult("The value must be a string.", [validationContext.MemberName!]);

        if (string.IsNullOrEmpty(str))
            return ValidationResult.Success;

        if (!ScopeSegmentRegex.IsMatch(str))
        {
            return new ValidationResult(
                ErrorMessage,
                [validationContext.MemberName!]);
        }

        return ValidationResult.Success;
    }
}
