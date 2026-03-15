namespace Stash.Interpreting.BuiltIns;

using System;
using Stash.Interpreting.Types;

public static class MathBuiltIns
{
    private static readonly Random _random = new();

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

        globals.Define("math", math);
    }

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
