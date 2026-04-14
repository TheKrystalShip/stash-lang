namespace Stash.Stdlib.BuiltIns;

using System;
using Stash.Runtime;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

/// <summary>
/// Registers the 'conv' namespace built-in functions.
/// </summary>
public static class ConvBuiltIns
{
    public static NamespaceDefinition Define()
    {
        // ── conv namespace ───────────────────────────────────────────────
        var ns = new NamespaceBuilder("conv");

        ns.Function("toStr", [Param("value")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            return StashValue.FromObj(RuntimeValues.Stringify(args[0].ToObject()));
        },
            returnType: "string",
            documentation: "Converts any value to its string representation.\n@param value The value to convert\n@return The string representation of the value");

        ns.Function("toInt", [Param("value"), Param("base", "int")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 1 || args.Length > 2)
                throw new RuntimeError("'conv.toInt' requires 1 or 2 arguments.");
            if (args.Length == 2)
            {
                int radix = (int)SvArgs.Long(args, 1, "conv.toInt");
                if (radix != 2 && radix != 8 && radix != 10 && radix != 16)
                    throw new RuntimeError($"conv.toInt: unsupported base {radix}. Must be 2, 8, 10, or 16.");
                string s = SvArgs.String(args, 0, "conv.toInt");
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
                    throw new RuntimeError($"Cannot parse '{s}' as a base-{radix} integer.");
                }
            }
            StashValue val = args[0];
            if (val.IsByte) return StashValue.FromInt(val.AsByte);
            if (val.IsInt) return val;
            if (val.IsFloat) return StashValue.FromInt((long)val.AsFloat);
            if (val.IsObj && val.AsObj is string str)
            {
                if (long.TryParse(str, out long result))
                    return StashValue.FromInt(result);
                throw new RuntimeError($"Cannot parse '{str}' as integer.");
            }
            throw new RuntimeError("Argument to 'conv.toInt' must be a number or string.");
        },
            returnType: "int",
            isVariadic: true,
            documentation: "Parses a string or converts a number to an integer. Supports optional base (2, 8, 10, 16). Handles \"0x\", \"0b\", \"0o\" prefixes automatically. Floats are truncated.\n@param value A string or number to convert\n@param base The numeric base (optional, default 10; must be 2, 8, 10, or 16)\n@return The integer value");

        ns.Function("toFloat", [Param("value")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            StashValue val = args[0];
            if (val.IsFloat) return val;
            if (val.IsByte) return StashValue.FromFloat((double)val.AsByte);
            if (val.IsInt) return StashValue.FromFloat((double)val.AsInt);
            if (val.IsObj && val.AsObj is string s)
            {
                if (double.TryParse(s, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double result))
                    return StashValue.FromFloat(result);
                throw new RuntimeError($"Cannot parse '{s}' as float.");
            }
            throw new RuntimeError("Argument to 'conv.toFloat' must be a number or string.");
        },
            returnType: "float",
            documentation: "Parses a string or converts a number to a float. Returns null on failure.\n@param value A string or number to convert\n@return The float value, or null if parsing fails");

        ns.Function("toBool", [Param("value")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            return StashValue.FromBool(RuntimeValues.IsTruthy(args[0].ToObject()));
        },
            returnType: "bool",
            documentation: "Converts a value to boolean using truthiness rules. false, null, 0, 0.0, and \"\" are falsy; everything else is truthy.\n@param value The value to convert\n@return The boolean result");

        ns.Function("toByte", [Param("value"), Param("base", "int")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 1 || args.Length > 2)
                throw new RuntimeError("'conv.toByte' requires 1 or 2 arguments.");
            if (args.Length == 2)
            {
                int radix = (int)SvArgs.Long(args, 1, "conv.toByte");
                if (radix != 2 && radix != 8 && radix != 10 && radix != 16)
                    throw new RuntimeError($"conv.toByte: unsupported base {radix}. Must be 2, 8, 10, or 16.");
                string s = SvArgs.String(args, 0, "conv.toByte");
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
                        throw new RuntimeError($"Value {parsed} is out of byte range [0, 255].");
                    return StashValue.FromByte((byte)parsed);
                }
                catch (Exception e) when (e is FormatException || e is OverflowException)
                {
                    throw new RuntimeError($"Cannot parse '{s}' as a base-{radix} byte value.");
                }
            }
            StashValue val = args[0];
            if (val.IsByte) return val;
            if (val.IsInt)
            {
                long n = val.AsInt;
                if (n < 0 || n > 255)
                    throw new RuntimeError($"Value {n} is out of byte range [0, 255].");
                return StashValue.FromByte((byte)n);
            }
            if (val.IsFloat)
            {
                long n = (long)val.AsFloat;
                if (n < 0 || n > 255)
                    throw new RuntimeError($"Value {n} is out of byte range [0, 255].");
                return StashValue.FromByte((byte)n);
            }
            if (val.IsObj && val.AsObj is string str)
            {
                if (byte.TryParse(str, out byte result))
                    return StashValue.FromByte(result);
                throw new RuntimeError($"Cannot parse '{str}' as byte.");
            }
            throw new RuntimeError("Argument to 'conv.toByte' must be a number or string.");
        },
            returnType: "byte",
            isVariadic: true,
            documentation: "Converts a value to a byte (0-255). Supports optional base (2, 8, 10, 16). Throws if out of range.\n@param value A string or number to convert\n@param base The numeric base (optional, default 10)\n@return The byte value");

        ns.Function("toHex", [Param("n", "int"), Param("padding", "int")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 1 || args.Length > 2)
                throw new RuntimeError("'conv.toHex' requires 1 or 2 arguments.");
            long n = SvArgs.Long(args, 0, "conv.toHex");
            if (args.Length == 2)
            {
                int padding = (int)SvArgs.Long(args, 1, "conv.toHex");
                return StashValue.FromObj(n.ToString($"x{padding}"));
            }
            return StashValue.FromObj(n.ToString("x"));
        },
            returnType: "string",
            isVariadic: true,
            documentation: "Converts an integer to its hexadecimal string representation with optional zero-padding.\n@param n The integer to convert\n@param padding Minimum number of characters (optional, zero-pads if needed)\n@return The hexadecimal string (e.g., \"ff\")");

        ns.Function("toOct", [Param("n", "int")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            long n = SvArgs.Long(args, 0, "conv.toOct");
            return StashValue.FromObj(System.Convert.ToString(n, 8));
        },
            returnType: "string",
            documentation: "Converts an integer to its octal string representation.\n@param n The integer to convert\n@return The octal string");

        ns.Function("toBin", [Param("n", "int")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            long n = SvArgs.Long(args, 0, "conv.toBin");
            return StashValue.FromObj(System.Convert.ToString(n, 2));
        },
            returnType: "string",
            documentation: "Converts an integer to its binary string representation.\n@param n The integer to convert\n@return The binary string");

        ns.Function("fromHex", [Param("s", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            string s = SvArgs.String(args, 0, "conv.fromHex");
            string hex = s.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase) ? s[2..] : s;
            try
            {
                return StashValue.FromInt(System.Convert.ToInt64(hex, 16));
            }
            catch (System.Exception e) when (e is System.FormatException || e is System.OverflowException)
            {
                throw new RuntimeError($"Cannot parse '{s}' as a hexadecimal integer.");
            }
        },
            returnType: "int",
            documentation: "Parses a hexadecimal string to an integer. Supports optional \"0x\" prefix.\n@param s The hexadecimal string to parse\n@return The parsed integer value");

        ns.Function("fromOct", [Param("s", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            string s = SvArgs.String(args, 0, "conv.fromOct");
            string oct = s.StartsWith("0o", System.StringComparison.OrdinalIgnoreCase) ? s[2..] : s;
            try
            {
                return StashValue.FromInt(System.Convert.ToInt64(oct, 8));
            }
            catch (System.Exception e) when (e is System.FormatException || e is System.OverflowException)
            {
                throw new RuntimeError($"Cannot parse '{s}' as an octal integer.");
            }
        },
            returnType: "int",
            documentation: "Parses an octal string to an integer. Supports optional \"0o\" prefix.\n@param s The octal string to parse\n@return The parsed integer value");

        ns.Function("fromBin", [Param("s", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            string s = SvArgs.String(args, 0, "conv.fromBin");
            string bin = s.StartsWith("0b", System.StringComparison.OrdinalIgnoreCase) ? s[2..] : s;
            try
            {
                return StashValue.FromInt(System.Convert.ToInt64(bin, 2));
            }
            catch (System.Exception e) when (e is System.FormatException || e is System.OverflowException)
            {
                throw new RuntimeError($"Cannot parse '{s}' as a binary integer.");
            }
        },
            returnType: "int",
            documentation: "Parses a binary string to an integer. Supports optional \"0b\" prefix.\n@param s The binary string to parse\n@return The parsed integer value");

        ns.Function("charCode", [Param("s", "string")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            string s = SvArgs.String(args, 0, "conv.charCode");
            if (s.Length == 0)
                throw new RuntimeError("Argument to 'conv.charCode' must be a non-empty string.");
            return StashValue.FromInt((long)s[0]);
        },
            returnType: "int",
            documentation: "Returns the Unicode code point of the first character in the string.\n@param s A non-empty string\n@return The Unicode code point as an integer");

        ns.Function("fromCharCode", [Param("n", "int")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            long n = SvArgs.Long(args, 0, "conv.fromCharCode");
            if (n < 0 || n > 0x10FFFF)
                throw new RuntimeError($"Code point {n} is out of the valid Unicode range (0–0x10FFFF).");
            return StashValue.FromObj(((char)(int)n).ToString());
        },
            returnType: "string",
            documentation: "Returns a single-character string from a Unicode code point.\n@param n The Unicode code point\n@return A string containing the character");

        return ns.Build();
    }
}
