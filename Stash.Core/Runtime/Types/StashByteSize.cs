namespace Stash.Runtime.Types;

using System;
using System.Globalization;
using Stash.Common;
using Stash.Runtime;
using Stash.Runtime.Protocols;

/// <summary>
/// Represents a byte-size value in Stash (e.g., <c>100B</c>, <c>1.5MB</c>, <c>2GB</c>).
/// Stores size as total bytes internally.
/// </summary>
public sealed class StashByteSize : IComparable<StashByteSize>, IEquatable<StashByteSize>,
    IVMTyped, IVMFieldAccessible, IVMArithmetic, IVMComparable, IVMStringifiable
{
    public long TotalBytes { get; }

    public StashByteSize(long totalBytes)
    {
        TotalBytes = totalBytes;
    }

    // --- Unit properties (convert to single unit as float) ---

    public double Kb => TotalBytes / 1_024.0;
    public double Mb => TotalBytes / 1_048_576.0;
    public double Gb => TotalBytes / 1_073_741_824.0;
    public double Tb => TotalBytes / 1_099_511_627_776.0;

    // --- Arithmetic ---

    public StashByteSize Add(StashByteSize other) => new(TotalBytes + other.TotalBytes);
    public StashByteSize Subtract(StashByteSize other) => new(TotalBytes - other.TotalBytes);
    public StashByteSize Multiply(double factor) => new((long)(TotalBytes * factor));
    public StashByteSize Divide(double divisor)
    {
        if (divisor == 0) throw new InvalidOperationException("Cannot divide byte size by zero.");
        return new((long)(TotalBytes / divisor));
    }
    public double DivideBy(StashByteSize other)
    {
        if (other.TotalBytes == 0) throw new InvalidOperationException("Cannot divide byte size by zero.");
        return (double)TotalBytes / other.TotalBytes;
    }

    // --- Parsing ---

    /// <summary>
    /// Tries to parse a byte-size string like "100B", "1.5MB", "2GB".
    /// </summary>
    public static bool TryParse(string text, out StashByteSize? result)
    {
        result = null;
        if (string.IsNullOrEmpty(text)) return false;

        // Find where the number ends and the unit begins
        int i = 0;
        bool hasDecimal = false;
        while (i < text.Length && (char.IsDigit(text[i]) || (text[i] == '.' && !hasDecimal)))
        {
            if (text[i] == '.') hasDecimal = true;
            i++;
        }

        if (i == 0 || i >= text.Length) return false;

        string numStr = text[..i];
        string unit = text[i..];

        if (!double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double numValue))
            return false;

        double multiplier = unit switch
        {
            "B" => 1,
            "KB" => 1_024,
            "MB" => 1_048_576,
            "GB" => 1_073_741_824,
            "TB" => 1_099_511_627_776,
            _ => -1
        };

        if (multiplier < 0) return false;

        result = new StashByteSize((long)(numValue * multiplier));
        return true;
    }

    /// <summary>
    /// Returns a validation error message, or null if the text is valid.
    /// </summary>
    public static string? ValidateFormat(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "Empty byte size literal.";

        int i = 0;
        bool hasDecimal = false;
        while (i < text.Length && (char.IsDigit(text[i]) || (text[i] == '.' && !hasDecimal)))
        {
            if (text[i] == '.') hasDecimal = true;
            i++;
        }

        if (i == 0) return "Expected number in byte size literal.";
        if (i >= text.Length) return "Byte size number must be followed by a unit (B, KB, MB, GB, TB).";

        string unit = text[i..];
        if (unit is not ("B" or "KB" or "MB" or "GB" or "TB"))
            return $"Unknown byte size unit '{unit}'. Valid units: B, KB, MB, GB, TB.";

        return null;
    }

    // --- Comparison ---

    public int CompareTo(StashByteSize? other)
    {
        if (other is null) return 1;
        return TotalBytes.CompareTo(other.TotalBytes);
    }

    // --- Equality ---

    public bool Equals(StashByteSize? other)
    {
        if (other is null) return false;
        return TotalBytes == other.TotalBytes;
    }

    public override bool Equals(object? obj) => obj is StashByteSize other && Equals(other);
    public override int GetHashCode() => TotalBytes.GetHashCode();

    public static bool operator ==(StashByteSize? left, StashByteSize? right) =>
        left is null ? right is null : left.Equals(right);
    public static bool operator !=(StashByteSize? left, StashByteSize? right) => !(left == right);

    // --- ToString ---

    /// <summary>
    /// Returns a human-readable byte size string.
    /// Uses the largest unit where the value is >= 1.
    /// Examples: "0B", "512B", "1.5KB", "256MB", "2.5GB", "1TB".
    /// </summary>
    public override string ToString()
    {
        long b = TotalBytes;
        if (b < 0) return $"-{new StashByteSize(-b)}";

        if (b >= 1_099_511_627_776L)
        {
            double tb = b / 1_099_511_627_776.0;
            return FormatUnit(tb, "TB");
        }
        if (b >= 1_073_741_824L)
        {
            double gb = b / 1_073_741_824.0;
            return FormatUnit(gb, "GB");
        }
        if (b >= 1_048_576L)
        {
            double mb = b / 1_048_576.0;
            return FormatUnit(mb, "MB");
        }
        if (b >= 1_024L)
        {
            double kb = b / 1_024.0;
            return FormatUnit(kb, "KB");
        }

        return $"{b}B";
    }

    private static string FormatUnit(double value, string unit)
    {
        // Show up to 2 decimal places, trim trailing zeros
        string formatted = value.ToString("0.##", CultureInfo.InvariantCulture);
        return $"{formatted}{unit}";
    }

    // --- VM Protocol Implementations ---

    public string VMTypeName => "bytes";

    public bool VMTryGetField(string name, out StashValue value, SourceSpan? span)
    {
        switch (name)
        {
            case "bytes": value = StashValue.FromInt(TotalBytes); return true;
            case "kb":    value = StashValue.FromFloat(Kb);       return true;
            case "mb":    value = StashValue.FromFloat(Mb);       return true;
            case "gb":    value = StashValue.FromFloat(Gb);       return true;
            case "tb":    value = StashValue.FromFloat(Tb);       return true;
            default:      value = StashValue.Null;                return false;
        }
    }

    public bool VMTryArithmetic(ArithmeticOp op, StashValue other, bool isLeftOperand,
                                out StashValue result, SourceSpan? span)
    {
        result = StashValue.Null;
        switch (op)
        {
            case ArithmeticOp.Add when isLeftOperand:
                if (other.IsObj && other.AsObj is StashByteSize addBs)
                {
                    result = StashValue.FromObj(Add(addBs));
                    return true;
                }
                return false;

            case ArithmeticOp.Subtract when isLeftOperand:
                if (other.IsObj && other.AsObj is StashByteSize subBs)
                {
                    result = StashValue.FromObj(Subtract(subBs));
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
                if (other.IsObj && other.AsObj is StashByteSize divBs)
                {
                    result = StashValue.FromFloat(DivideBy(divBs));
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
                result = StashValue.FromObj(new StashByteSize(-TotalBytes));
                return true;

            default:
                return false;
        }
    }

    public bool VMTryCompare(StashValue other, out int result, SourceSpan? span)
    {
        if (other.IsObj && other.AsObj is StashByteSize otherBs)
        {
            result = CompareTo(otherBs);
            return true;
        }
        result = 0;
        return false;
    }

    public string VMToString() => ToString();
}
