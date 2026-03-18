namespace Stash.Interpreting.BuiltIns;

using System;
using Stash.Interpreting.Types;

/// <summary>Registers the <c>math</c> namespace providing mathematical functions and constants (abs, ceil, floor, round, min, max, pow, sqrt, log, random, randomInt, clamp, PI, E).</summary>
public static class MathBuiltIns
{
    /// <summary>Shared random number generator for <c>math.random()</c> and <c>math.randomInt()</c>.</summary>
    private static readonly Random _random = new();

    /// <summary>Registers the <c>math</c> namespace and all its functions into the global environment.</summary>
    /// <param name="globals">The global environment to register into.</param>
    public static void Register(Stash.Interpreting.Environment globals)
    {
        var math = new StashNamespace("math");

        math.Define("PI", Math.PI);
        math.Define("E", Math.E);

        math.Define("abs", new BuiltInFunction("math.abs", 1, (interp, args) =>
        {
            if (args[0] is long l)
            {
                return Math.Abs(l);
            }

            if (args[0] is double d)
            {
                return Math.Abs(d);
            }

            throw new RuntimeError("Argument to 'math.abs' must be a number.");
        }));

        math.Define("ceil", new BuiltInFunction("math.ceil", 1, (interp, args) =>
        {
            double d = ToDouble(args[0], "math.ceil");
            return (long)Math.Ceiling(d);
        }));

        math.Define("floor", new BuiltInFunction("math.floor", 1, (interp, args) =>
        {
            double d = ToDouble(args[0], "math.floor");
            return (long)Math.Floor(d);
        }));

        math.Define("round", new BuiltInFunction("math.round", 1, (interp, args) =>
        {
            double d = ToDouble(args[0], "math.round");
            return (long)Math.Round(d, MidpointRounding.AwayFromZero);
        }));

        math.Define("min", new BuiltInFunction("math.min", 2, (interp, args) =>
        {
            double a = ToDouble(args[0], "math.min");
            double b = ToDouble(args[1], "math.min");
            double result = Math.Min(a, b);
            if (args[0] is long && args[1] is long)
            {
                return (long)result;
            }

            return result;
        }));

        math.Define("max", new BuiltInFunction("math.max", 2, (interp, args) =>
        {
            double a = ToDouble(args[0], "math.max");
            double b = ToDouble(args[1], "math.max");
            double result = Math.Max(a, b);
            if (args[0] is long && args[1] is long)
            {
                return (long)result;
            }

            return result;
        }));

        math.Define("pow", new BuiltInFunction("math.pow", 2, (interp, args) =>
        {
            double b = ToDouble(args[0], "math.pow");
            double e = ToDouble(args[1], "math.pow");
            return Math.Pow(b, e);
        }));

        math.Define("sqrt", new BuiltInFunction("math.sqrt", 1, (interp, args) =>
        {
            double d = ToDouble(args[0], "math.sqrt");
            return Math.Sqrt(d);
        }));

        math.Define("log", new BuiltInFunction("math.log", 1, (interp, args) =>
        {
            double d = ToDouble(args[0], "math.log");
            return Math.Log(d);
        }));

        math.Define("random", new BuiltInFunction("math.random", 0, (interp, args) =>
        {
            return _random.NextDouble();
        }));

        math.Define("randomInt", new BuiltInFunction("math.randomInt", 2, (interp, args) =>
        {
            if (args[0] is not long min)
            {
                throw new RuntimeError("First argument to 'math.randomInt' must be an integer.");
            }

            if (args[1] is not long max)
            {
                throw new RuntimeError("Second argument to 'math.randomInt' must be an integer.");
            }

            return (long)_random.NextInt64(min, max + 1);
        }));

        math.Define("clamp", new BuiltInFunction("math.clamp", 3, (interp, args) =>
        {
            double n   = ToDouble(args[0], "math.clamp");
            double min = ToDouble(args[1], "math.clamp");
            double max = ToDouble(args[2], "math.clamp");
            double result = Math.Clamp(n, min, max);
            if (args[0] is long && args[1] is long && args[2] is long)
            {
                return (long)result;
            }

            return result;
        }));

        math.Define("sin", new BuiltInFunction("math.sin", 1, (interp, args) =>
        {
            double d = ToDouble(args[0], "math.sin");
            return Math.Sin(d);
        }));

        math.Define("cos", new BuiltInFunction("math.cos", 1, (interp, args) =>
        {
            double d = ToDouble(args[0], "math.cos");
            return Math.Cos(d);
        }));

        math.Define("tan", new BuiltInFunction("math.tan", 1, (interp, args) =>
        {
            double d = ToDouble(args[0], "math.tan");
            return Math.Tan(d);
        }));

        math.Define("asin", new BuiltInFunction("math.asin", 1, (interp, args) =>
        {
            double d = ToDouble(args[0], "math.asin");
            return Math.Asin(d);
        }));

        math.Define("acos", new BuiltInFunction("math.acos", 1, (interp, args) =>
        {
            double d = ToDouble(args[0], "math.acos");
            return Math.Acos(d);
        }));

        math.Define("atan", new BuiltInFunction("math.atan", 1, (interp, args) =>
        {
            double d = ToDouble(args[0], "math.atan");
            return Math.Atan(d);
        }));

        math.Define("atan2", new BuiltInFunction("math.atan2", 2, (interp, args) =>
        {
            double y = ToDouble(args[0], "math.atan2");
            double x = ToDouble(args[1], "math.atan2");
            return Math.Atan2(y, x);
        }));

        math.Define("sign", new BuiltInFunction("math.sign", 1, (interp, args) =>
        {
            if (args[0] is long l)
            {
                return (long)Math.Sign(l);
            }

            if (args[0] is double d)
            {
                return (long)Math.Sign(d);
            }

            throw new RuntimeError("Argument to 'math.sign' must be a number.");
        }));

        math.Define("exp", new BuiltInFunction("math.exp", 1, (interp, args) =>
        {
            double d = ToDouble(args[0], "math.exp");
            return Math.Exp(d);
        }));

        math.Define("log10", new BuiltInFunction("math.log10", 1, (interp, args) =>
        {
            double d = ToDouble(args[0], "math.log10");
            return Math.Log10(d);
        }));

        math.Define("log2", new BuiltInFunction("math.log2", 1, (interp, args) =>
        {
            double d = ToDouble(args[0], "math.log2");
            return Math.Log2(d);
        }));

        globals.Define("math", math);
    }

    /// <summary>Converts a numeric value to <see cref="double"/>, throwing if the value is not a number.</summary>
    /// <param name="val">The value to convert.</param>
    /// <param name="funcName">The calling function name, used in error messages.</param>
    /// <returns>The numeric value as a <see cref="double"/>.</returns>
    private static double ToDouble(object? val, string funcName)
    {
        if (val is long l)
        {
            return (double)l;
        }

        if (val is double d)
        {
            return d;
        }

        throw new RuntimeError($"Argument to '{funcName}' must be a number.");
    }
}
