using System;
using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace Stash.Registry.Contracts.Validation;

/// <summary>
/// Validates that a string value is a parseable token-expiry duration string and meets
/// the minimum lifetime floor of one hour.
/// </summary>
/// <remarks>
/// <para>
/// Supported suffix formats (case-insensitive):
/// <list type="table">
///   <listheader><term>Suffix</term><description>Unit</description></listheader>
///   <item><term><c>d</c></term><description>Days — e.g. <c>"30d"</c></description></item>
///   <item><term><c>h</c></term><description>Hours — e.g. <c>"24h"</c></description></item>
///   <item><term><c>m</c></term><description>Minutes — e.g. <c>"60m"</c></description></item>
/// </list>
/// </para>
/// <para>
/// The minimum lifetime is 1 hour (60 minutes). Values shorter than 60 minutes fail validation.
/// The ceiling check against <c>Security.MaxTokenLifetime</c> is config-dependent and remains
/// as an inline guard in the controller action.
/// </para>
/// <para>
/// This attribute implements the same parse logic as <c>AuthHelper.ParseTokenExpiry</c> in
/// <c>Stash.Registry/Endpoints/AuthHelper.cs</c>, inlined here to keep
/// <c>Stash.Registry.Contracts</c> dependency-free and to add the ≥ 1 h floor check.
/// </para>
/// <para>
/// Null values pass (combine with <c>[Required]</c> to reject them).
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field,
    AllowMultiple = false)]
public sealed class TokenExpiryAttribute : ValidationAttribute
{
    private const int MinMinutes = 60; // 1 hour floor

    /// <summary>
    /// Initialises a new <see cref="TokenExpiryAttribute"/> with the default error message.
    /// </summary>
    public TokenExpiryAttribute()
        : base("The token expiry must be a valid duration (e.g. '30d', '12h', '90m') and must be at least 1 hour.")
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

        if (!TryParseTotalMinutes(str, out int totalMinutes))
        {
            return new ValidationResult(
                $"The expiry value '{str}' is not a recognised format. Use formats like '30d', '12h', or '90m'.",
                [validationContext.MemberName!]);
        }

        if (totalMinutes < MinMinutes)
        {
            return new ValidationResult(
                $"The token expiry must be at least 1 hour (60 minutes), but '{str}' resolves to {totalMinutes} minute(s).",
                [validationContext.MemberName!]);
        }

        return ValidationResult.Success;
    }

    /// <summary>
    /// Attempts to parse a duration string into a total minute count.
    /// Returns <see langword="false"/> if the string is unrecognised.
    /// </summary>
    private static bool TryParseTotalMinutes(string expiry, out int totalMinutes)
    {
        totalMinutes = 0;
        string s = expiry.Trim();

        if (s.EndsWith("d", StringComparison.OrdinalIgnoreCase))
        {
            string digits = s[..^1];
            if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int days) || days <= 0)
                return false;
            totalMinutes = days * 24 * 60;
            return true;
        }

        if (s.EndsWith("h", StringComparison.OrdinalIgnoreCase))
        {
            string digits = s[..^1];
            if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int hours) || hours <= 0)
                return false;
            totalMinutes = hours * 60;
            return true;
        }

        if (s.EndsWith("m", StringComparison.OrdinalIgnoreCase))
        {
            string digits = s[..^1];
            if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int minutes) || minutes <= 0)
                return false;
            totalMinutes = minutes;
            return true;
        }

        // Plain integer → treated as days (matches AuthHelper.ParseTokenExpiry fallback)
        if (int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out int defaultDays) && defaultDays > 0)
        {
            totalMinutes = defaultDays * 24 * 60;
            return true;
        }

        return false;
    }
}
