namespace Stash.Stdlib.BuiltIns;

using System;
using Stash.Runtime;
using Stash.Stdlib;
using Stash.Stdlib.Abstractions;

/// <summary>
/// Registers the <c>conv</c> namespace built-in functions for type conversions.
/// </summary>
[StashNamespace]
public static partial class ConvBuiltIns
{
    /// <summary>Converts any value to its string representation.</summary>
    /// <param name="value">The value to convert</param>
    /// <returns>The string representation of the value</returns>
    [StashFn(ReturnType = "string")]
    public static string ToStr(StashValue value)
    {
        return RuntimeValues.Stringify(value.ToObject());
    }

    /// <summary>Parses a string or converts a number to an integer. Supports optional base (2, 8, 10, 16). Handles "0x", "0b", "0o" prefixes automatically. Floats are truncated.</summary>
    /// <param name="value">A string or number to convert</param>
    /// <param name="base">The numeric base (optional, default 10; must be 2, 8, 10, or 16)</param>
    /// <exception cref="StashErrorTypes.ParseError">if the string cannot be parsed as an integer in the given base</exception>
    /// <exception cref="StashErrorTypes.ValueError">if the base is not 2, 8, 10, or 16</exception>
    /// <exception cref="StashErrorTypes.TypeError">if the argument is not a number or string</exception>
    /// <returns>The integer value</returns>
    [StashFn(ReturnType = "int")]
    public static StashValue ToInt(StashValue value, params StashValue[] rest)
    {
        if (rest.Length > 1)
            throw new RuntimeError("'conv.toInt' requires 1 or 2 arguments.");
        if (rest.Length == 1)
        {
            int radix = (int)SvArgs.Long(rest, 0, "conv.toInt");
            if (radix != 2 && radix != 8 && radix != 10 && radix != 16)
                throw new RuntimeError($"conv.toInt: unsupported base {radix}. Must be 2, 8, 10, or 16.", errorType: StashErrorTypes.ValueError);
            if (!(value.IsObj && value.AsObj is string))
                throw new RuntimeError("First argument to 'conv.toInt' must be a string.");
            string s = (string)value.AsObj;
            string stripped = radix switch
            {
                16 when s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) => s[2..],
                2 when s.StartsWith("0b", StringComparison.OrdinalIgnoreCase) => s[2..],
                8 when s.StartsWith("0o", StringComparison.OrdinalIgnoreCase) => s[2..],
                _ => s
            };
            try
            {
                return StashValue.FromInt(System.Convert.ToInt64(stripped, radix));
            }
            catch (Exception e) when (e is FormatException || e is OverflowException)
            {
                throw new RuntimeError($"Cannot parse '{s}' as a base-{radix} integer.", errorType: StashErrorTypes.ParseError);
            }
        }
        if (value.IsByte) return StashValue.FromInt(value.AsByte);
        if (value.IsInt) return value;
        if (value.IsFloat) return StashValue.FromInt((long)value.AsFloat);
        if (value.IsObj && value.AsObj is string str)
        {
            if (long.TryParse(str, out long result))
                return StashValue.FromInt(result);
            throw new RuntimeError($"Cannot parse '{str}' as integer.", errorType: StashErrorTypes.ParseError);
        }
        throw new RuntimeError("Argument to 'conv.toInt' must be a number or string.", errorType: StashErrorTypes.TypeError);
    }

    /// <summary>Parses a string or converts a number to a float. Returns null on failure.</summary>
    /// <param name="value">A string or number to convert</param>
    /// <exception cref="StashErrorTypes.ParseError">if the string cannot be parsed as a floating-point number</exception>
    /// <exception cref="StashErrorTypes.TypeError">if the argument is not a number or string</exception>
    /// <returns>The float value, or null if parsing fails</returns>
    [StashFn(ReturnType = "float")]
    public static StashValue ToFloat(StashValue value)
    {
        if (value.IsFloat) return value;
        if (value.IsByte) return StashValue.FromFloat((double)value.AsByte);
        if (value.IsInt) return StashValue.FromFloat((double)value.AsInt);
        if (value.IsObj && value.AsObj is string s)
        {
            if (double.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double result))
                return StashValue.FromFloat(result);
            throw new RuntimeError($"Cannot parse '{s}' as float.", errorType: StashErrorTypes.ParseError);
        }
        throw new RuntimeError("Argument to 'conv.toFloat' must be a number or string.", errorType: StashErrorTypes.TypeError);
    }

    /// <summary>Converts a value to boolean using truthiness rules. false, null, 0, 0.0, and "" are falsy; everything else is truthy.</summary>
    /// <param name="value">The value to convert</param>
    /// <returns>The boolean result</returns>
    [StashFn(ReturnType = "bool")]
    public static bool ToBool(StashValue value)
    {
        return RuntimeValues.IsTruthy(value.ToObject());
    }

    /// <summary>Converts a value to a byte (0-255). Supports optional base (2, 8, 10, 16). Throws if out of range.</summary>
    /// <param name="value">A string or number to convert</param>
    /// <param name="base">The numeric base (optional, default 10)</param>
    /// <exception cref="StashErrorTypes.ParseError">if the string cannot be parsed as an integer in the given base</exception>
    /// <exception cref="StashErrorTypes.ValueError">if the base is not 2, 8, 10, or 16, or if the value is outside the range [0, 255]</exception>
    /// <exception cref="StashErrorTypes.TypeError">if the argument is not a number or string</exception>
    /// <returns>The byte value</returns>
    [StashFn(ReturnType = "byte")]
    public static StashValue ToByte(StashValue value, params StashValue[] rest)
    {
        if (rest.Length > 1)
            throw new RuntimeError("'conv.toByte' requires 1 or 2 arguments.");
        if (rest.Length == 1)
        {
            int radix = (int)SvArgs.Long(rest, 0, "conv.toByte");
            if (radix != 2 && radix != 8 && radix != 10 && radix != 16)
                throw new RuntimeError($"conv.toByte: unsupported base {radix}. Must be 2, 8, 10, or 16.", errorType: StashErrorTypes.ValueError);
            if (!(value.IsObj && value.AsObj is string))
                throw new RuntimeError("First argument to 'conv.toByte' must be a string.");
            string s = (string)value.AsObj;
            string stripped = radix switch
            {
                16 when s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) => s[2..],
                2 when s.StartsWith("0b", StringComparison.OrdinalIgnoreCase) => s[2..],
                8 when s.StartsWith("0o", StringComparison.OrdinalIgnoreCase) => s[2..],
                _ => s
            };
            try
            {
                long parsed = System.Convert.ToInt64(stripped, radix);
                if (parsed < 0 || parsed > 255)
                    throw new RuntimeError($"Value {parsed} is out of byte range [0, 255].", errorType: StashErrorTypes.ValueError);
                return StashValue.FromByte((byte)parsed);
            }
            catch (Exception e) when (e is FormatException || e is OverflowException)
            {
                throw new RuntimeError($"Cannot parse '{s}' as a base-{radix} byte value.", errorType: StashErrorTypes.ParseError);
            }
        }
        if (value.IsByte) return value;
        if (value.IsInt)
        {
            long n = value.AsInt;
            if (n < 0 || n > 255)
                throw new RuntimeError($"Value {n} is out of byte range [0, 255].", errorType: StashErrorTypes.ValueError);
            return StashValue.FromByte((byte)n);
        }
        if (value.IsFloat)
        {
            long n = (long)value.AsFloat;
            if (n < 0 || n > 255)
                throw new RuntimeError($"Value {n} is out of byte range [0, 255].", errorType: StashErrorTypes.ValueError);
            return StashValue.FromByte((byte)n);
        }
        if (value.IsObj && value.AsObj is string str)
        {
            if (byte.TryParse(str, out byte result))
                return StashValue.FromByte(result);
            throw new RuntimeError($"Cannot parse '{str}' as byte.", errorType: StashErrorTypes.ParseError);
        }
        throw new RuntimeError("Argument to 'conv.toByte' must be a number or string.", errorType: StashErrorTypes.TypeError);
    }

    /// <summary>Converts an integer to its hexadecimal string representation with optional zero-padding.</summary>
    /// <param name="n">The integer to convert</param>
    /// <param name="padding">Minimum number of characters (optional, zero-pads if needed)</param>
    /// <exception cref="StashErrorTypes.TypeError">if any argument has the wrong type</exception>
    /// <returns>The hexadecimal string (e.g., "ff")</returns>
    [StashFn(ReturnType = "string")]
    public static string ToHex(long n, params StashValue[] rest)
    {
        if (rest.Length > 1)
            throw new RuntimeError("'conv.toHex' requires 1 or 2 arguments.");
        if (rest.Length == 1)
        {
            int padding = (int)SvArgs.Long(rest, 0, "conv.toHex");
            return n.ToString($"x{padding}");
        }
        return n.ToString("x");
    }

    /// <summary>Converts an integer to its octal string representation.</summary>
    /// <param name="n">The integer to convert</param>
    /// <exception cref="StashErrorTypes.TypeError">if the argument is not an integer</exception>
    /// <returns>The octal string</returns>
    [StashFn(ReturnType = "string")]
    public static string ToOct(long n) => System.Convert.ToString(n, 8);

    /// <summary>Converts an integer to its binary string representation.</summary>
    /// <param name="n">The integer to convert</param>
    /// <exception cref="StashErrorTypes.TypeError">if the argument is not an integer</exception>
    /// <returns>The binary string</returns>
    [StashFn(ReturnType = "string")]
    public static string ToBin(long n) => System.Convert.ToString(n, 2);

    /// <summary>Parses a hexadecimal string to an integer. Supports optional "0x" prefix.</summary>
    /// <param name="s">The hexadecimal string to parse</param>
    /// <exception cref="StashErrorTypes.ParseError">if the string is not a valid hexadecimal integer</exception>
    /// <exception cref="StashErrorTypes.TypeError">if the argument is not a string</exception>
    /// <returns>The parsed integer value</returns>
    [StashFn(ReturnType = "int")]
    public static long FromHex(string s)
    {
        string hex = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s[2..] : s;
        try
        {
            return System.Convert.ToInt64(hex, 16);
        }
        catch (Exception e) when (e is FormatException || e is OverflowException)
        {
            throw new RuntimeError($"Cannot parse '{s}' as a hexadecimal integer.", errorType: StashErrorTypes.ParseError);
        }
    }

    /// <summary>Parses an octal string to an integer. Supports optional "0o" prefix.</summary>
    /// <param name="s">The octal string to parse</param>
    /// <exception cref="StashErrorTypes.ParseError">if the string is not a valid octal integer</exception>
    /// <exception cref="StashErrorTypes.TypeError">if the argument is not a string</exception>
    /// <returns>The parsed integer value</returns>
    [StashFn(ReturnType = "int")]
    public static long FromOct(string s)
    {
        string oct = s.StartsWith("0o", StringComparison.OrdinalIgnoreCase) ? s[2..] : s;
        try
        {
            return System.Convert.ToInt64(oct, 8);
        }
        catch (Exception e) when (e is FormatException || e is OverflowException)
        {
            throw new RuntimeError($"Cannot parse '{s}' as an octal integer.", errorType: StashErrorTypes.ParseError);
        }
    }

    /// <summary>Parses a binary string to an integer. Supports optional "0b" prefix.</summary>
    /// <param name="s">The binary string to parse</param>
    /// <exception cref="StashErrorTypes.ParseError">if the string is not a valid binary integer</exception>
    /// <exception cref="StashErrorTypes.TypeError">if the argument is not a string</exception>
    /// <returns>The parsed integer value</returns>
    [StashFn(ReturnType = "int")]
    public static long FromBin(string s)
    {
        string bin = s.StartsWith("0b", StringComparison.OrdinalIgnoreCase) ? s[2..] : s;
        try
        {
            return System.Convert.ToInt64(bin, 2);
        }
        catch (Exception e) when (e is FormatException || e is OverflowException)
        {
            throw new RuntimeError($"Cannot parse '{s}' as a binary integer.", errorType: StashErrorTypes.ParseError);
        }
    }

    /// <summary>Deprecated. Use `str.charCode`. Returns the Unicode code point of the first character in the string.</summary>
    /// <param name="s">A non-empty string</param>
    /// <exception cref="StashErrorTypes.ValueError">if the string is empty</exception>
    /// <exception cref="StashErrorTypes.TypeError">if the argument is not a string</exception>
    /// <returns>The Unicode code point as an integer</returns>
    [StashFn(ReturnType = "int")]
    [StashDeprecated("str.charCode")]
    public static long CharCode(string s)
    {
        if (s.Length == 0)
            throw new RuntimeError("Argument to 'conv.charCode' must be a non-empty string.", errorType: StashErrorTypes.ValueError);
        return (long)s[0];
    }

    /// <summary>Deprecated. Use `str.fromCharCode`. Returns a single-character string from a Unicode code point.</summary>
    /// <param name="n">The Unicode code point</param>
    /// <exception cref="StashErrorTypes.ValueError">if the code point is outside the valid Unicode range (0–0x10FFFF)</exception>
    /// <exception cref="StashErrorTypes.TypeError">if the argument is not an integer</exception>
    /// <returns>A string containing the character</returns>
    [StashFn(ReturnType = "string")]
    [StashDeprecated("str.fromCharCode")]
    public static string FromCharCode(long n)
    {
        if (n < 0 || n > 0x10FFFF)
            throw new RuntimeError($"Code point {n} is out of the valid Unicode range (0–0x10FFFF).", errorType: StashErrorTypes.ValueError);
        return ((char)(int)n).ToString();
    }
}
