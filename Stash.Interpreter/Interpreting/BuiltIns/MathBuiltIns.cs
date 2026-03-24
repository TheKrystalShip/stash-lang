namespace Stash.Interpreting.BuiltIns;

using System;
using Stash.Interpreting.Types;

/// <summary>
/// Registers the <c>math</c> namespace built-in functions and constants for mathematical operations.
/// </summary>
/// <remarks>
/// <para>
/// Provides constants <c>math.PI</c> and <c>math.E</c>, and 22 functions including
/// <c>math.abs</c>, <c>math.ceil</c>, <c>math.floor</c>, <c>math.round</c>,
/// <c>math.min</c>, <c>math.max</c>, <c>math.pow</c>, <c>math.sqrt</c>,
/// <c>math.log</c>, <c>math.log2</c>, <c>math.log10</c>, <c>math.exp</c>,
/// <c>math.sin</c>, <c>math.cos</c>, <c>math.tan</c>, <c>math.asin</c>,
/// <c>math.acos</c>, <c>math.atan</c>, <c>math.atan2</c>, <c>math.sign</c>,
/// <c>math.clamp</c>, <c>math.random</c>, and <c>math.randomInt</c>.
/// All functions are registered as <see cref="BuiltInFunction"/> instances on a
/// <see cref="StashNamespace"/> in the global <see cref="Environment"/>.
/// </para>
/// <para>
/// Integer-preserving functions (e.g. <c>math.abs</c>, <c>math.min</c>, <c>math.clamp</c>)
/// return a <see langword="long"/> when all inputs are integers, otherwise a <see langword="double"/>.
/// </para>
/// </remarks>
public static class MathBuiltIns
{
    /// <summary>
    /// Registers all <c>math</c> namespace functions and constants into the global environment.
    /// </summary>
    /// <param name="globals">The global <see cref="Environment"/> to register functions in.</param>
    public static void Register(Stash.Interpreting.Environment globals)
    {
        var math = new StashNamespace("math");

        // math.PI — The mathematical constant π (≈ 3.14159265358979…).
        math.Define("PI", Math.PI);

        // math.E — Euler's number e (≈ 2.71828182845904…).
        math.Define("E", Math.E);

        // math.abs(n) — Returns the absolute value of integer or float n.
        // Returns long when n is an integer, double when n is a float.
        // Throws RuntimeError if n is not a number.
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

        // math.ceil(n) — Returns the smallest integer greater than or equal to n.
        // Returns the integer unchanged if n is already an integer; otherwise returns double.
        // Throws RuntimeError if n is not a number.
        math.Define("ceil", new BuiltInFunction("math.ceil", 1, (interp, args) =>
        {
            if (args[0] is long l)
            {
                return l;
            }

            double d = ToDouble(args[0], "math.ceil");
            return Math.Ceiling(d);
        }));

        // math.floor(n) — Returns the largest integer less than or equal to n.
        // Returns the integer unchanged if n is already an integer; otherwise returns double.
        // Throws RuntimeError if n is not a number.
        math.Define("floor", new BuiltInFunction("math.floor", 1, (interp, args) =>
        {
            if (args[0] is long l)
            {
                return l;
            }

            double d = ToDouble(args[0], "math.floor");
            return Math.Floor(d);
        }));

        // math.round(n) — Returns n rounded to the nearest integer using midpoint-away-from-zero
        // rounding (e.g. 0.5 → 1, -0.5 → -1). Returns the integer unchanged if n is already one.
        // Throws RuntimeError if n is not a number.
        math.Define("round", new BuiltInFunction("math.round", 1, (interp, args) =>
        {
            if (args[0] is long l)
            {
                return l;
            }

            double d = ToDouble(args[0], "math.round");
            return Math.Round(d, MidpointRounding.AwayFromZero);
        }));

        // math.min(a, b) — Returns the smaller of a and b. Returns long when both inputs are
        // integers, otherwise double. Throws RuntimeError if either argument is not a number.
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

        // math.max(a, b) — Returns the larger of a and b. Returns long when both inputs are
        // integers, otherwise double. Throws RuntimeError if either argument is not a number.
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

        // math.pow(base, exponent) — Returns base raised to the power of exponent as a double.
        // Throws RuntimeError if either argument is not a number.
        math.Define("pow", new BuiltInFunction("math.pow", 2, (interp, args) =>
        {
            double b = ToDouble(args[0], "math.pow");
            double e = ToDouble(args[1], "math.pow");
            return Math.Pow(b, e);
        }));

        // math.sqrt(n) — Returns the square root of n as a double.
        // Throws RuntimeError if n is not a number.
        math.Define("sqrt", new BuiltInFunction("math.sqrt", 1, (interp, args) =>
        {
            double d = ToDouble(args[0], "math.sqrt");
            return Math.Sqrt(d);
        }));

        // math.log(n) — Returns the natural logarithm (base e) of n as a double.
        // Throws RuntimeError if n is not a number.
        math.Define("log", new BuiltInFunction("math.log", 1, (interp, args) =>
        {
            double d = ToDouble(args[0], "math.log");
            return Math.Log(d);
        }));

        // math.random() — Returns a random double in the range [0.0, 1.0).
        math.Define("random", new BuiltInFunction("math.random", 0, (interp, args) =>
        {
            return Random.Shared.NextDouble();
        }));

        // math.randomInt(min, max) — Returns a random integer in the inclusive range [min, max].
        // Throws RuntimeError if either argument is not an integer.
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

            return (long)Random.Shared.NextInt64(min, max + 1);
        }));

        // math.clamp(n, min, max) — Returns n clamped to the range [min, max].
        // Returns long when all three arguments are integers, otherwise double.
        // Throws RuntimeError if any argument is not a number.
        math.Define("clamp", new BuiltInFunction("math.clamp", 3, (interp, args) =>
        {
            double n = ToDouble(args[0], "math.clamp");
            double min = ToDouble(args[1], "math.clamp");
            double max = ToDouble(args[2], "math.clamp");
            double result = Math.Clamp(n, min, max);
            if (args[0] is long && args[1] is long && args[2] is long)
            {
                return (long)result;
            }

            return result;
        }));

        // math.sin(n) — Returns the sine of angle n (in radians) as a double.
        // Throws RuntimeError if n is not a number.
        math.Define("sin", new BuiltInFunction("math.sin", 1, (interp, args) =>
        {
            double d = ToDouble(args[0], "math.sin");
            return Math.Sin(d);
        }));

        // math.cos(n) — Returns the cosine of angle n (in radians) as a double.
        // Throws RuntimeError if n is not a number.
        math.Define("cos", new BuiltInFunction("math.cos", 1, (interp, args) =>
        {
            double d = ToDouble(args[0], "math.cos");
            return Math.Cos(d);
        }));

        // math.tan(n) — Returns the tangent of angle n (in radians) as a double.
        // Throws RuntimeError if n is not a number.
        math.Define("tan", new BuiltInFunction("math.tan", 1, (interp, args) =>
        {
            double d = ToDouble(args[0], "math.tan");
            return Math.Tan(d);
        }));

        // math.asin(n) — Returns the arcsine of n in radians as a double. Input must be in [-1, 1].
        // Throws RuntimeError if n is not a number.
        math.Define("asin", new BuiltInFunction("math.asin", 1, (interp, args) =>
        {
            double d = ToDouble(args[0], "math.asin");
            return Math.Asin(d);
        }));

        // math.acos(n) — Returns the arccosine of n in radians as a double. Input must be in [-1, 1].
        // Throws RuntimeError if n is not a number.
        math.Define("acos", new BuiltInFunction("math.acos", 1, (interp, args) =>
        {
            double d = ToDouble(args[0], "math.acos");
            return Math.Acos(d);
        }));

        // math.atan(n) — Returns the arctangent of n in radians as a double.
        // Throws RuntimeError if n is not a number.
        math.Define("atan", new BuiltInFunction("math.atan", 1, (interp, args) =>
        {
            double d = ToDouble(args[0], "math.atan");
            return Math.Atan(d);
        }));

        // math.atan2(y, x) — Returns the angle in radians between the positive x-axis and the
        // point (x, y), in the range (-π, π]. Equivalent to Math.Atan2(y, x).
        // Throws RuntimeError if either argument is not a number.
        math.Define("atan2", new BuiltInFunction("math.atan2", 2, (interp, args) =>
        {
            double y = ToDouble(args[0], "math.atan2");
            double x = ToDouble(args[1], "math.atan2");
            return Math.Atan2(y, x);
        }));

        // math.sign(n) — Returns -1, 0, or 1 as a long indicating the sign of n.
        // Throws RuntimeError if n is not a number.
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

        // math.exp(n) — Returns e raised to the power of n as a double (i.e. eⁿ).
        // Throws RuntimeError if n is not a number.
        math.Define("exp", new BuiltInFunction("math.exp", 1, (interp, args) =>
        {
            double d = ToDouble(args[0], "math.exp");
            return Math.Exp(d);
        }));

        // math.log10(n) — Returns the base-10 logarithm of n as a double.
        // Throws RuntimeError if n is not a number.
        math.Define("log10", new BuiltInFunction("math.log10", 1, (interp, args) =>
        {
            double d = ToDouble(args[0], "math.log10");
            return Math.Log10(d);
        }));

        // math.log2(n) — Returns the base-2 logarithm of n as a double.
        // Throws RuntimeError if n is not a number.
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
