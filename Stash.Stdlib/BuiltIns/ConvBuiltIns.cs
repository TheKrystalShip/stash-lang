namespace Stash.Stdlib.BuiltIns;

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

        ns.Function("toStr", [Param("value")], (_, args) =>
        {
            return RuntimeValues.Stringify(args[0]);
        },
            returnType: "string",
            documentation: "Converts any value to its string representation.\n@param value The value to convert\n@return The string representation of the value");

        ns.Function("toInt", [Param("value")], (_, args) =>
        {
            object? val = args[0];
            if (val is long l)
            {
                return l;
            }

            if (val is double d)
            {
                return (long)d;
            }

            if (val is string s)
            {
                if (long.TryParse(s, out long result))
                {
                    return result;
                }

                throw new RuntimeError($"Cannot parse '{s}' as integer.");
            }
            throw new RuntimeError("Argument to 'conv.toInt' must be a number or string.");
        },
            returnType: "int",
            documentation: "Parses a string or converts a number to an integer. Floats are truncated. Returns null on failure.\n@param value A string or number to convert\n@return The integer value, or null if parsing fails");

        ns.Function("toFloat", [Param("value")], (_, args) =>
        {
            object? val = args[0];
            if (val is double d)
            {
                return d;
            }

            if (val is long l)
            {
                return (double)l;
            }

            if (val is string s)
            {
                if (double.TryParse(s, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double result))
                {
                    return result;
                }

                throw new RuntimeError($"Cannot parse '{s}' as float.");
            }
            throw new RuntimeError("Argument to 'conv.toFloat' must be a number or string.");
        },
            returnType: "float",
            documentation: "Parses a string or converts a number to a float. Returns null on failure.\n@param value A string or number to convert\n@return The float value, or null if parsing fails");

        ns.Function("toBool", [Param("value")], (_, args) =>
        {
            return RuntimeValues.IsTruthy(args[0]);
        },
            returnType: "bool",
            documentation: "Converts a value to boolean using truthiness rules. false, null, 0, 0.0, and \"\" are falsy; everything else is truthy.\n@param value The value to convert\n@return The boolean result");

        ns.Function("toHex", [Param("n", "int")], (_, args) =>
        {
            var n = Args.Long(args, 0, "conv.toHex");
            return n.ToString("x");
        },
            returnType: "string",
            documentation: "Converts an integer to its hexadecimal string representation.\n@param n The integer to convert\n@return The hexadecimal string (e.g., \"ff\")");

        ns.Function("toOct", [Param("n", "int")], (_, args) =>
        {
            var n = Args.Long(args, 0, "conv.toOct");
            return System.Convert.ToString(n, 8);
        },
            returnType: "string",
            documentation: "Converts an integer to its octal string representation.\n@param n The integer to convert\n@return The octal string");

        ns.Function("toBin", [Param("n", "int")], (_, args) =>
        {
            var n = Args.Long(args, 0, "conv.toBin");
            return System.Convert.ToString(n, 2);
        },
            returnType: "string",
            documentation: "Converts an integer to its binary string representation.\n@param n The integer to convert\n@return The binary string");

        ns.Function("fromHex", [Param("s", "string")], (_, args) =>
        {
            var s = Args.String(args, 0, "conv.fromHex");
            string hex = s.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase) ? s[2..] : s;
            try
            {
                return System.Convert.ToInt64(hex, 16);
            }
            catch (System.Exception e) when (e is System.FormatException || e is System.OverflowException)
            {
                throw new RuntimeError($"Cannot parse '{s}' as a hexadecimal integer.");
            }
        },
            returnType: "int",
            documentation: "Parses a hexadecimal string to an integer. Supports optional \"0x\" prefix.\n@param s The hexadecimal string to parse\n@return The parsed integer value");

        ns.Function("fromOct", [Param("s", "string")], (_, args) =>
        {
            var s = Args.String(args, 0, "conv.fromOct");
            string oct = s.StartsWith("0o", System.StringComparison.OrdinalIgnoreCase) ? s[2..] : s;
            try
            {
                return System.Convert.ToInt64(oct, 8);
            }
            catch (System.Exception e) when (e is System.FormatException || e is System.OverflowException)
            {
                throw new RuntimeError($"Cannot parse '{s}' as an octal integer.");
            }
        },
            returnType: "int",
            documentation: "Parses an octal string to an integer. Supports optional \"0o\" prefix.\n@param s The octal string to parse\n@return The parsed integer value");

        ns.Function("fromBin", [Param("s", "string")], (_, args) =>
        {
            var s = Args.String(args, 0, "conv.fromBin");
            string bin = s.StartsWith("0b", System.StringComparison.OrdinalIgnoreCase) ? s[2..] : s;
            try
            {
                return System.Convert.ToInt64(bin, 2);
            }
            catch (System.Exception e) when (e is System.FormatException || e is System.OverflowException)
            {
                throw new RuntimeError($"Cannot parse '{s}' as a binary integer.");
            }
        },
            returnType: "int",
            documentation: "Parses a binary string to an integer. Supports optional \"0b\" prefix.\n@param s The binary string to parse\n@return The parsed integer value");

        ns.Function("charCode", [Param("s", "string")], (_, args) =>
        {
            var s = Args.String(args, 0, "conv.charCode");
            if (s.Length == 0)
            {
                throw new RuntimeError("Argument to 'conv.charCode' must be a non-empty string.");
            }

            return (long)s[0];
        },
            returnType: "int",
            documentation: "Returns the Unicode code point of the first character in the string.\n@param s A non-empty string\n@return The Unicode code point as an integer");

        ns.Function("fromCharCode", [Param("n", "int")], (_, args) =>
        {
            var n = Args.Long(args, 0, "conv.fromCharCode");

            if (n < 0 || n > 0x10FFFF)
            {
                throw new RuntimeError($"Code point {n} is out of the valid Unicode range (0–0x10FFFF).");
            }

            return ((char)(int)n).ToString();
        },
            returnType: "string",
            documentation: "Returns a single-character string from a Unicode code point.\n@param n The Unicode code point\n@return A string containing the character");

        return ns.Build();
    }
}
