namespace Stash.Runtime.Types;

using System;
using Stash.Common;
using Stash.Runtime;
using Stash.Runtime.Protocols;

/// <summary>
/// Represents a duration value in Stash (e.g., <c>5s</c>, <c>2h30m</c>, <c>500ms</c>).
/// Stores time as total milliseconds internally.
/// </summary>
public sealed class StashDuration : IComparable<StashDuration>, IEquatable<StashDuration>,
    IVMTyped, IVMFieldAccessible, IVMArithmetic, IVMComparable, IVMStringifiable
{
    public long TotalMilliseconds { get; }

    public StashDuration(long totalMilliseconds)
    {
        TotalMilliseconds = totalMilliseconds;
    }

    // --- Component properties (decompose into units) ---

    /// <summary>Days component (e.g., 90061500ms → 1 day).</summary>
    public long Days => Math.Abs(TotalMilliseconds) / (24 * 3600 * 1000L);

    /// <summary>Hours component after removing full days (0-23).</summary>
    public long Hours => (Math.Abs(TotalMilliseconds) / (3600 * 1000L)) % 24;

    /// <summary>Minutes component after removing full hours (0-59).</summary>
    public long Minutes => (Math.Abs(TotalMilliseconds) / (60 * 1000L)) % 60;

    /// <summary>Seconds component after removing full minutes (0-59).</summary>
    public long Seconds => (Math.Abs(TotalMilliseconds) / 1000L) % 60;

    /// <summary>Milliseconds component after removing full seconds (0-999).</summary>
    public long Milliseconds => Math.Abs(TotalMilliseconds) % 1000;

    // --- Total properties (convert to single unit) ---

    public double TotalSeconds => TotalMilliseconds / 1000.0;
    public double TotalMinutes => TotalMilliseconds / 60_000.0;
    public double TotalHours => TotalMilliseconds / 3_600_000.0;
    public double TotalDays => TotalMilliseconds / 86_400_000.0;

    // --- Arithmetic ---

    public StashDuration Add(StashDuration other) => new(TotalMilliseconds + other.TotalMilliseconds);
    public StashDuration Subtract(StashDuration other) => new(TotalMilliseconds - other.TotalMilliseconds);
    public StashDuration Multiply(double factor) => new((long)(TotalMilliseconds * factor));
    public StashDuration Divide(double divisor)
    {
        if (divisor == 0) throw new InvalidOperationException("Cannot divide duration by zero.");
        return new((long)(TotalMilliseconds / divisor));
    }
    public double DivideBy(StashDuration other)
    {
        if (other.TotalMilliseconds == 0) throw new InvalidOperationException("Cannot divide duration by zero duration.");
        return (double)TotalMilliseconds / other.TotalMilliseconds;
    }
    public StashDuration Negate() => new(-TotalMilliseconds);

    // --- Parsing ---

    /// <summary>
    /// Tries to parse a duration string like "2h30m", "500ms", "1.5s", "1d12h".
    /// Called by the lexer after scanning digits + unit suffix.
    /// </summary>
    public static bool TryParse(string text, out StashDuration? result)
    {
        result = null;
        if (string.IsNullOrEmpty(text)) return false;

        long totalMs = 0;
        int i = 0;
        bool anyParsed = false;

        while (i < text.Length)
        {
            // Scan number part (integer or float)
            int numStart = i;
            bool hasDecimal = false;
            while (i < text.Length && (char.IsDigit(text[i]) || (text[i] == '.' && !hasDecimal)))
            {
                if (text[i] == '.') hasDecimal = true;
                i++;
            }

            if (i == numStart) return false; // No number found

            string numStr = text[numStart..i];
            if (!double.TryParse(numStr, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double numValue))
                return false;

            // Scan unit part
            if (i >= text.Length) return false; // Number without unit

            int unitStart = i;
            while (i < text.Length && char.IsLetter(text[i])) i++;
            string unit = text[unitStart..i];

            double multiplier = unit switch
            {
                "ms" => 1,
                "s" => 1_000,
                "m" => 60_000,
                "h" => 3_600_000,
                "d" => 86_400_000,
                _ => -1
            };

            if (multiplier < 0) return false;

            totalMs += (long)(numValue * multiplier);
            anyParsed = true;
        }

        if (!anyParsed) return false;

        result = new StashDuration(totalMs);
        return true;
    }

    /// <summary>
    /// Returns a validation error message, or null if the text is valid.
    /// </summary>
    public static string? ValidateFormat(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "Empty duration literal.";

        int i = 0;
        while (i < text.Length)
        {
            int numStart = i;
            bool hasDecimal = false;
            while (i < text.Length && (char.IsDigit(text[i]) || (text[i] == '.' && !hasDecimal)))
            {
                if (text[i] == '.') hasDecimal = true;
                i++;
            }

            if (i == numStart)
                return $"Expected number in duration at position {i}.";

            int unitStart = i;
            while (i < text.Length && char.IsLetter(text[i])) i++;
            string unit = text[unitStart..i];

            if (unit.Length == 0)
                return "Duration number must be followed by a unit (ms, s, m, h, d).";

            if (unit is not ("ms" or "s" or "m" or "h" or "d"))
                return $"Unknown duration unit '{unit}'. Valid units: ms, s, m, h, d.";
        }

        return null;
    }

    // --- Comparison ---

    public int CompareTo(StashDuration? other)
    {
        if (other is null) return 1;
        return TotalMilliseconds.CompareTo(other.TotalMilliseconds);
    }

    // --- Equality ---

    public bool Equals(StashDuration? other)
    {
        if (other is null) return false;
        return TotalMilliseconds == other.TotalMilliseconds;
    }

    public override bool Equals(object? obj) => obj is StashDuration other && Equals(other);
    public override int GetHashCode() => TotalMilliseconds.GetHashCode();

    public static bool operator ==(StashDuration? left, StashDuration? right) =>
        left is null ? right is null : left.Equals(right);
    public static bool operator !=(StashDuration? left, StashDuration? right) => !(left == right);

    // --- ToString ---

    /// <summary>
    /// Returns a human-readable duration string.
    /// Examples: "500ms", "5s", "2m30s", "1h15m", "2d12h30m".
    /// Uses the largest units that fit, always showing at least one unit.
    /// </summary>
    public override string ToString()
    {
        long ms = TotalMilliseconds;
        if (ms < 0) return $"-{new StashDuration(-ms)}";
        if (ms == 0) return "0s";

        var parts = new System.Collections.Generic.List<string>();

        long d = ms / 86_400_000; ms %= 86_400_000;
        long h = ms / 3_600_000; ms %= 3_600_000;
        long m = ms / 60_000; ms %= 60_000;
        long s = ms / 1_000; ms %= 1_000;

        if (d > 0) parts.Add($"{d}d");
        if (h > 0) parts.Add($"{h}h");
        if (m > 0) parts.Add($"{m}m");
        if (s > 0) parts.Add($"{s}s");
        if (ms > 0) parts.Add($"{ms}ms");

        return string.Join("", parts);
    }

    // --- VM Protocol Implementations ---

    public string VMTypeName => "duration";

    public bool VMTryGetField(string name, out StashValue value, SourceSpan? span)
    {
        switch (name)
        {
            case "totalMs":      value = StashValue.FromInt(TotalMilliseconds);   return true;
            case "totalSeconds": value = StashValue.FromFloat(TotalSeconds);      return true;
            case "totalMinutes": value = StashValue.FromFloat(TotalMinutes);      return true;
            case "totalHours":   value = StashValue.FromFloat(TotalHours);        return true;
            case "totalDays":    value = StashValue.FromFloat(TotalDays);         return true;
            case "milliseconds": value = StashValue.FromInt(Milliseconds);        return true;
            case "seconds":      value = StashValue.FromInt(Seconds);             return true;
            case "minutes":      value = StashValue.FromInt(Minutes);             return true;
            case "hours":        value = StashValue.FromInt(Hours);               return true;
            case "days":         value = StashValue.FromInt(Days);                return true;
            default:             value = StashValue.Null;                         return false;
        }
    }

    public bool VMTryArithmetic(ArithmeticOp op, StashValue other, bool isLeftOperand,
                                out StashValue result, SourceSpan? span)
    {
        result = StashValue.Null;
        switch (op)
        {
            case ArithmeticOp.Add when isLeftOperand:
                if (other.IsObj && other.AsObj is StashDuration addDur)
                {
                    result = StashValue.FromObj(Add(addDur));
                    return true;
                }
                return false;

            case ArithmeticOp.Subtract when isLeftOperand:
                if (other.IsObj && other.AsObj is StashDuration subDur)
                {
                    result = StashValue.FromObj(Subtract(subDur));
                    return true;
                }
                return false;

            case ArithmeticOp.Multiply:
                if (other.IsInt || other.IsFloat)
                {
                    double factor = other.IsInt ? (double)other.AsInt : other.AsFloat;
                    result = StashValue.FromObj(Multiply(factor));
                    return true;
                }
                return false;

            case ArithmeticOp.Divide when isLeftOperand:
                if (other.IsObj && other.AsObj is StashDuration divDur)
                {
                    result = StashValue.FromFloat(DivideBy(divDur));
                    return true;
                }
                if (other.IsInt || other.IsFloat)
                {
                    double factor = other.IsInt ? (double)other.AsInt : other.AsFloat;
                    result = StashValue.FromObj(Divide(factor));
                    return true;
                }
                return false;

            case ArithmeticOp.Negate when isLeftOperand:
                result = StashValue.FromObj(Negate());
                return true;

            default:
                return false;
        }
    }

    public bool VMTryCompare(StashValue other, out int result, SourceSpan? span)
    {
        if (other.IsObj && other.AsObj is StashDuration otherDur)
        {
            result = CompareTo(otherDur);
            return true;
        }
        result = 0;
        return false;
    }

    public string VMToString() => ToString();
}
