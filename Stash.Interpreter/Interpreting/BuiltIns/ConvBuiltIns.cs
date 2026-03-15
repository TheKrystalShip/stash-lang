namespace Stash.Interpreting.BuiltIns;

using System.Collections.Generic;
using Stash.Interpreting.Types;

/// <summary>
/// Registers the 'conv' namespace built-in functions.
/// </summary>
public static class ConvBuiltIns
{
    public static void Register(Environment globals)
    {
        // ── conv namespace ───────────────────────────────────────────────
        var conv = new StashNamespace("conv");

        conv.Define("toStr", new BuiltInFunction("conv.toStr", 1, (_, args) =>
        {
            return RuntimeValues.Stringify(args[0]);
        }));

        conv.Define("toInt", new BuiltInFunction("conv.toInt", 1, (_, args) =>
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
        }));

        conv.Define("toFloat", new BuiltInFunction("conv.toFloat", 1, (_, args) =>
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
        }));

        conv.Define("toBool", new BuiltInFunction("conv.toBool", 1, (_, args) =>
        {
            return RuntimeValues.IsTruthy(args[0]);
        }));

        conv.Define("toHex", new BuiltInFunction("conv.toHex", 1, (_, args) =>
        {
            if (args[0] is not long n)
            {
                throw new RuntimeError("Argument to 'conv.toHex' must be an integer.");
            }

            return n.ToString("x");
        }));

        conv.Define("toOct", new BuiltInFunction("conv.toOct", 1, (_, args) =>
        {
            if (args[0] is not long n)
            {
                throw new RuntimeError("Argument to 'conv.toOct' must be an integer.");
            }

            return System.Convert.ToString(n, 8);
        }));

        conv.Define("toBin", new BuiltInFunction("conv.toBin", 1, (_, args) =>
        {
            if (args[0] is not long n)
            {
                throw new RuntimeError("Argument to 'conv.toBin' must be an integer.");
            }

            return System.Convert.ToString(n, 2);
        }));

        conv.Define("fromHex", new BuiltInFunction("conv.fromHex", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("Argument to 'conv.fromHex' must be a string.");
            }

            string hex = s.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase) ? s[2..] : s;
            try
            {
                return System.Convert.ToInt64(hex, 16);
            }
            catch (System.Exception e) when (e is System.FormatException || e is System.OverflowException)
            {
                throw new RuntimeError($"Cannot parse '{s}' as a hexadecimal integer.");
            }
        }));

        conv.Define("fromOct", new BuiltInFunction("conv.fromOct", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("Argument to 'conv.fromOct' must be a string.");
            }

            string oct = s.StartsWith("0o", System.StringComparison.OrdinalIgnoreCase) ? s[2..] : s;
            try
            {
                return System.Convert.ToInt64(oct, 8);
            }
            catch (System.Exception e) when (e is System.FormatException || e is System.OverflowException)
            {
                throw new RuntimeError($"Cannot parse '{s}' as an octal integer.");
            }
        }));

        conv.Define("fromBin", new BuiltInFunction("conv.fromBin", 1, (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("Argument to 'conv.fromBin' must be a string.");
            }

            string bin = s.StartsWith("0b", System.StringComparison.OrdinalIgnoreCase) ? s[2..] : s;
            try
            {
                return System.Convert.ToInt64(bin, 2);
            }
            catch (System.Exception e) when (e is System.FormatException || e is System.OverflowException)
            {
                throw new RuntimeError($"Cannot parse '{s}' as a binary integer.");
            }
        }));

        conv.Define("charCode", new BuiltInFunction("conv.charCode", 1, (_, args) =>
        {
            if (args[0] is not string s || s.Length == 0)
            {
                throw new RuntimeError("Argument to 'conv.charCode' must be a non-empty string.");
            }

            return (long)s[0];
        }));

        conv.Define("fromCharCode", new BuiltInFunction("conv.fromCharCode", 1, (_, args) =>
        {
            if (args[0] is not long n)
            {
                throw new RuntimeError("Argument to 'conv.fromCharCode' must be an integer.");
            }

            if (n < 0 || n > 0x10FFFF)
            {
                throw new RuntimeError($"Code point {n} is out of the valid Unicode range (0–0x10FFFF).");
            }

            return ((char)(int)n).ToString();
        }));

        globals.Define("conv", conv);
    }
}
