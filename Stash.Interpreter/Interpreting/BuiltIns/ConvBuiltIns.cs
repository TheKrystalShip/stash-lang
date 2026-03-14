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

        globals.Define("conv", conv);
    }
}
