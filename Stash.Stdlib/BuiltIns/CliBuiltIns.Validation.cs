namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Runtime.Errors;

// Validation pipeline for the cli namespace — P5.
//
// Applies per-argument constraints in the documented order (brief §Validation order):
//   1. (Token-level and type conversion — handled in Parse.cs)
//   2. (Choices membership — handled in Parse.cs via ValidateChoices)
//   3. min / max  — applies to int, float, bytesize, duration, semver
//   4. pattern    — applies to string only
//   5. User validate callback — (value) -> bool | string
//
// After all per-argument validation, missing-required checks run (in Parse.cs).
//
// Construction-time compatibility checks live in Schema.cs (enforced via CliSchemaError).
public static partial class CliBuiltIns
{
    // ── Tags that support min / max ────────────────────────────────────────────

    internal static readonly HashSet<string> NumericTypeTags = new(StringComparer.Ordinal)
    {
        "int", "float", "bytesize", "duration", "semver",
    };

    // ── Per-argument validation pipeline (step 3 → 5) ─────────────────────────

    /// <summary>
    /// Applies min/max/pattern/validate constraints against a successfully converted value.
    /// Steps 3–5 of the validation order; choices (step 2) is handled separately in Parse.cs.
    /// </summary>
    /// <param name="spec">The CliArgSpec StashInstance for this argument.</param>
    /// <param name="converted">The already-type-converted StashValue.</param>
    /// <param name="raw">The original raw string from argv (for error messages).</param>
    /// <param name="optionName">Human-readable option name (e.g. "--output" or "src") for error messages.</param>
    /// <param name="ctx">Interpreter context for IStashCallable dispatch.</param>
    internal static void ApplyConstraints(
        StashInstance spec,
        StashValue converted,
        string raw,
        string optionName,
        IInterpreterContext ctx)
    {
        string typeTag = GetStringFieldOrEmpty(spec, "typeTag");

        // ── Step 3a: min ───────────────────────────────────────────────────────
        StashValue minVal = spec.GetField("min", null);
        if (!minVal.IsNull)
        {
            if (!IsGreaterOrEqualTo(converted, minVal, typeTag))
            {
                string minStr = FormatBound(minVal);
                throw new CliValidationFailed(
                    $"Value '{raw}' for '{optionName}' is less than minimum {minStr}.",
                    option: optionName,
                    validationMessage: $"Value must be >= {minStr}.");
            }
        }

        // ── Step 3b: max ───────────────────────────────────────────────────────
        StashValue maxVal = spec.GetField("max", null);
        if (!maxVal.IsNull)
        {
            if (!IsLessOrEqualTo(converted, maxVal, typeTag))
            {
                string maxStr = FormatBound(maxVal);
                throw new CliValidationFailed(
                    $"Value '{raw}' for '{optionName}' exceeds maximum {maxStr}.",
                    option: optionName,
                    validationMessage: $"Value must be <= {maxStr}.");
            }
        }

        // ── Step 4: pattern ────────────────────────────────────────────────────
        StashValue patternVal = spec.GetField("pattern", null);
        if (!patternVal.IsNull && patternVal.IsObj && patternVal.AsObj is string patternStr)
        {
            // typeTag == "string" is guaranteed at schema time; enforce defensively here too.
            if (converted.IsObj && converted.AsObj is string strValue)
            {
                if (!Regex.IsMatch(strValue, patternStr))
                {
                    throw new CliValidationFailed(
                        $"Value '{raw}' for '{optionName}' does not match pattern /{patternStr}/.",
                        option: optionName,
                        validationMessage: $"Value must match pattern /{patternStr}/.");
                }
            }
        }

        // ── Step 5: user validate callback ────────────────────────────────────
        StashValue validateVal = spec.GetField("validate", null);
        if (!validateVal.IsNull && validateVal.IsObj && validateVal.AsObj is IStashCallable fn)
        {
            StashValue callbackResult = ctx.InvokeCallbackDirect(fn, new StashValue[] { converted });

            // Tri-return convention:
            //   true  → pass
            //   false → generic failure
            //   string → string is used as the message
            //   anything else → treated as generic failure
            if (callbackResult.IsBool)
            {
                if (!callbackResult.AsBool)
                {
                    throw new CliValidationFailed(
                        $"Value '{raw}' for '{optionName}' failed validation.",
                        option: optionName,
                        validationMessage: $"Validation failed for value '{raw}'.");
                }
                // true → pass, no action needed
            }
            else if (callbackResult.IsObj && callbackResult.AsObj is string failMsg)
            {
                // The spec says: "returning a string uses that string as CliValidationFailed.message".
                // StashError.VMTryGetField("message") returns the CLR Message property,
                // so we pass the user string directly as the CLR Message.
                throw new CliValidationFailed(
                    failMsg,
                    option: optionName,
                    validationMessage: failMsg);
            }
            else if (!callbackResult.IsBool)
            {
                // Non-bool, non-string return — treat as generic failure
                throw new CliValidationFailed(
                    $"Value '{raw}' for '{optionName}' failed validation (callback returned non-bool/non-string).",
                    option: optionName,
                    validationMessage: "Validate callback returned a non-bool, non-string value.");
            }
        }
    }

    // ── Comparison helpers ─────────────────────────────────────────────────────

    private static bool IsGreaterOrEqualTo(StashValue value, StashValue bound, string typeTag)
    {
        return typeTag switch
        {
            "int" when value.IsInt && (bound.IsInt || bound.IsFloat) =>
                bound.IsInt ? value.AsInt >= bound.AsInt : (double)value.AsInt >= bound.AsFloat,
            "float" when value.IsFloat && (bound.IsInt || bound.IsFloat) =>
                bound.IsFloat ? value.AsFloat >= bound.AsFloat : value.AsFloat >= (double)bound.AsInt,
            "float" when value.IsInt && (bound.IsInt || bound.IsFloat) =>
                bound.IsFloat ? (double)value.AsInt >= bound.AsFloat : value.AsInt >= bound.AsInt,
            "duration" when value.IsObj && value.AsObj is StashDuration dur =>
                CompareObjBound(dur, bound) >= 0,
            "bytesize" when value.IsObj && value.AsObj is StashByteSize bs =>
                CompareObjBound(bs, bound) >= 0,
            "semver" when value.IsObj && value.AsObj is StashSemVer sv =>
                CompareObjBound(sv, bound) >= 0,
            _ => true, // unreachable if schema-time checks are correct
        };
    }

    private static bool IsLessOrEqualTo(StashValue value, StashValue bound, string typeTag)
    {
        return typeTag switch
        {
            "int" when value.IsInt && (bound.IsInt || bound.IsFloat) =>
                bound.IsInt ? value.AsInt <= bound.AsInt : (double)value.AsInt <= bound.AsFloat,
            "float" when value.IsFloat && (bound.IsInt || bound.IsFloat) =>
                bound.IsFloat ? value.AsFloat <= bound.AsFloat : value.AsFloat <= (double)bound.AsInt,
            "float" when value.IsInt && (bound.IsInt || bound.IsFloat) =>
                bound.IsFloat ? (double)value.AsInt <= bound.AsFloat : value.AsInt <= bound.AsInt,
            "duration" when value.IsObj && value.AsObj is StashDuration dur =>
                CompareObjBound(dur, bound) <= 0,
            "bytesize" when value.IsObj && value.AsObj is StashByteSize bs =>
                CompareObjBound(bs, bound) <= 0,
            "semver" when value.IsObj && value.AsObj is StashSemVer sv =>
                CompareObjBound(sv, bound) <= 0,
            _ => true, // unreachable if schema-time checks are correct
        };
    }

    private static int CompareObjBound<T>(T value, StashValue bound) where T : IComparable<T>
    {
        if (bound.IsObj && bound.AsObj is T typedBound)
            return value.CompareTo(typedBound);
        // Fallback: bound is stored as string (schema time stores them as converted values)
        return 0;
    }

    private static string FormatBound(StashValue v)
    {
        if (v.IsInt) return v.AsInt.ToString();
        if (v.IsFloat) return v.AsFloat.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (v.IsObj) return v.AsObj?.ToString() ?? "null";
        return "null";
    }

    // ── Construction-time compatibility validation ─────────────────────────────

    /// <summary>
    /// Validates that min/max/pattern constraints are compatible with the spec's typeTag.
    /// Called from BuildSchema() after the spec is resolved.
    /// - min/max only valid for: int, float, bytesize, duration, semver
    /// - pattern only valid for: string
    /// Raises CliSchemaError on mismatch.
    /// </summary>
    internal static void ValidateConstraintCompatibility(
        StashInstance resolvedSpec,
        string typeTag,
        string propName)
    {
        // ── min / max type check ──────────────────────────────────────────────
        StashValue minVal = resolvedSpec.GetField("min", null);
        StashValue maxVal = resolvedSpec.GetField("max", null);

        if (!minVal.IsNull && !NumericTypeTags.Contains(typeTag))
        {
            throw new CliSchemaError(
                $"'cli.schema': 'min' constraint on '{propName}' is only valid for numeric type tags " +
                $"(int, float, bytesize, duration, semver), not '{typeTag}'.",
                field: propName,
                reason: $"min is not supported for type '{typeTag}'");
        }

        if (!maxVal.IsNull && !NumericTypeTags.Contains(typeTag))
        {
            throw new CliSchemaError(
                $"'cli.schema': 'max' constraint on '{propName}' is only valid for numeric type tags " +
                $"(int, float, bytesize, duration, semver), not '{typeTag}'.",
                field: propName,
                reason: $"max is not supported for type '{typeTag}'");
        }

        // ── pattern type check ────────────────────────────────────────────────
        StashValue patternVal = resolvedSpec.GetField("pattern", null);
        if (!patternVal.IsNull && patternVal.IsObj && patternVal.AsObj is string)
        {
            if (typeTag != "string")
            {
                throw new CliSchemaError(
                    $"'cli.schema': 'pattern' constraint on '{propName}' is only valid for type 'string', not '{typeTag}'.",
                    field: propName,
                    reason: $"pattern is not supported for type '{typeTag}'");
            }
        }
    }
}
