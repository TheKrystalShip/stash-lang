namespace Stash.Stdlib.BuiltIns;

using System;
using Stash.Runtime;
using Stash.Stdlib.Abstractions;
using Stash.Stdlib;

/// <summary>
/// Registers the <c>math</c> namespace built-in functions and constants for mathematical operations.
/// </summary>
[StashNamespace]
public static partial class MathBuiltIns
{
    /// <summary>The ratio of a circle's circumference to its diameter (π ≈ 3.14159).</summary>
    [StashConst(Display = "3.141592653589793")]
    public const double PI = Math.PI;

    /// <summary>Euler's number, the base of natural logarithms (e ≈ 2.71828).</summary>
    [StashConst(Display = "2.718281828459045")]
    public const double E = Math.E;

    /// <summary>Returns the absolute value of a number.</summary>
    /// <param name="n">The number</param>
    /// <returns>The absolute value</returns>
    [StashFn(ReturnType = "number")]
    public static StashValue Abs([StashParam(Type = "number")] StashValue n)
    {
        if (n.IsInt) return StashValue.FromInt(Math.Abs(n.AsInt));
        if (n.IsFloat) return StashValue.FromFloat(Math.Abs(n.AsFloat));
        throw new RuntimeError("First argument to 'math.abs' must be a number.", errorType: StashErrorTypes.TypeError);
    }

    /// <summary>Returns the smallest integer greater than or equal to a number (rounds up).</summary>
    /// <param name="n">The number to round up</param>
    /// <returns>The ceiling value</returns>
    [StashFn(ReturnType = "number")]
    public static StashValue Ceil([StashParam(Type = "number")] StashValue n)
    {
        if (n.IsInt) return n;
        if (n.IsFloat) return StashValue.FromFloat(Math.Ceiling(n.AsFloat));
        throw new RuntimeError("First argument to 'math.ceil' must be a number.");
    }

    /// <summary>Returns the largest integer less than or equal to a number (rounds down).</summary>
    /// <param name="n">The number to round down</param>
    /// <returns>The floor value</returns>
    [StashFn(ReturnType = "number")]
    public static StashValue Floor([StashParam(Type = "number")] StashValue n)
    {
        if (n.IsInt) return n;
        if (n.IsFloat) return StashValue.FromFloat(Math.Floor(n.AsFloat));
        throw new RuntimeError("First argument to 'math.floor' must be a number.");
    }

    /// <summary>Rounds a number to the nearest integer, or to a specified number of decimal places. Ties round away from zero.</summary>
    /// <param name="n">The number to round</param>
    /// <param name="precision">(optional) Number of decimal places (positive) or significant digits to the left of the decimal (negative). Defaults to 0</param>
    /// <returns>The rounded value</returns>
    [StashFn(ReturnType = "number")]
    public static StashValue Round([StashParam(Type = "number")] StashValue n, params StashValue[] rest)
    {
        if (rest.Length > 1)
            throw new RuntimeError("'math.round' requires 1 or 2 arguments.");
        if (n.IsInt && rest.Length == 0) return n;
        double d = AsNumber(n, 1, "math.round");
        if (rest.Length == 0)
            return StashValue.FromFloat(Math.Round(d, MidpointRounding.AwayFromZero));
        long precision = AsLong(rest[0], 2, "math.round");
        if (precision >= 0)
            return StashValue.FromFloat(Math.Round(d, (int)precision, MidpointRounding.AwayFromZero));
        double factor = Math.Pow(10, -precision);
        return StashValue.FromFloat(Math.Round(d / factor, MidpointRounding.AwayFromZero) * factor);
    }

    /// <summary>Returns the smallest of two or more numbers.</summary>
    /// <param name="a">The first number</param>
    /// <param name="b">The second number</param>
    /// <param name="args">Additional numbers to compare</param>
    /// <returns>The smallest value</returns>
    [StashFn(ReturnType = "number")]
    public static StashValue Min(
        [StashParam(Type = "number")] StashValue a,
        [StashParam(Type = "number")] StashValue b,
        params StashValue[] args)
    {
        bool allInt = a.IsInt && b.IsInt;
        double av = AsNumber(a, 1, "math.min");
        double bv = AsNumber(b, 2, "math.min");
        double result = av < bv ? av : bv;
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].IsInt) allInt = false;
            double v = AsNumber(args[i], i + 3, "math.min");
            if (v < result) result = v;
        }
        return allInt ? StashValue.FromInt((long)result) : StashValue.FromFloat(result);
    }

    /// <summary>Returns the largest of two or more numbers.</summary>
    /// <param name="a">The first number</param>
    /// <param name="b">The second number</param>
    /// <param name="args">Additional numbers to compare</param>
    /// <returns>The largest value</returns>
    [StashFn(ReturnType = "number")]
    public static StashValue Max(
        [StashParam(Type = "number")] StashValue a,
        [StashParam(Type = "number")] StashValue b,
        params StashValue[] args)
    {
        bool allInt = a.IsInt && b.IsInt;
        double av = AsNumber(a, 1, "math.max");
        double bv = AsNumber(b, 2, "math.max");
        double result = av > bv ? av : bv;
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].IsInt) allInt = false;
            double v = AsNumber(args[i], i + 3, "math.max");
            if (v > result) result = v;
        }
        return allInt ? StashValue.FromInt((long)result) : StashValue.FromFloat(result);
    }

    /// <summary>Raises a number to a power.</summary>
    /// <param name="base">The base number</param>
    /// <param name="exp">The exponent</param>
    /// <returns>The result as a float</returns>
    [StashFn]
    public static double Pow(
        [StashParam(Type = "number")] double @base,
        [StashParam(Type = "number")] double exp)
        => Math.Pow(@base, exp);

    /// <summary>Returns the square root of a number.</summary>
    /// <param name="n">The number (must be non-negative)</param>
    /// <returns>The square root as a float</returns>
    [StashFn]
    public static double Sqrt([StashParam(Type = "number")] double n) => Math.Sqrt(n);

    /// <summary>Returns the logarithm of a number. When called with one argument, returns the natural logarithm (base e). When called with two arguments, returns the logarithm in the specified base.</summary>
    /// <param name="n">The number (must be positive)</param>
    /// <param name="base">(optional) The logarithm base. Defaults to e (natural log)</param>
    /// <returns>The logarithm as a float</returns>
    [StashFn]
    public static double Log([StashParam(Type = "number")] double n, params StashValue[] rest)
    {
        if (rest.Length > 1)
            throw new RuntimeError("'math.log' requires 1 or 2 arguments.");
        if (rest.Length == 0) return Math.Log(n);
        double b = AsNumber(rest[0], 2, "math.log");
        return Math.Log(n, b);
    }

    /// <summary>Returns a random float between 0.0 (inclusive) and 1.0 (exclusive).</summary>
    /// <returns>A random float in [0.0, 1.0)</returns>
    [StashFn]
    public static double Random() => System.Random.Shared.NextDouble();

    /// <summary>Returns a random integer. With no arguments, returns a random integer in [0, int.MaxValue]. With one argument, returns an integer in [0, max]. With two arguments, returns an integer in [min, max].</summary>
    /// <param name="min">(optional) The minimum value (inclusive). Defaults to 0</param>
    /// <param name="max">(optional) The maximum value (inclusive). Defaults to int.MaxValue when no args given, otherwise required upper bound</param>
    /// <returns>A random integer</returns>
    [StashFn]
    public static long RandomInt(params StashValue[] args)
    {
        if (args.Length > 2)
            throw new RuntimeError("'math.randomInt' requires 0, 1, or 2 arguments.");
        if (args.Length == 0)
            return System.Random.Shared.NextInt64(0, (long)int.MaxValue + 1);
        if (args.Length == 1)
        {
            long max = AsLong(args[0], 1, "math.randomInt");
            return System.Random.Shared.NextInt64(0, max + 1);
        }
        long mn = AsLong(args[0], 1, "math.randomInt");
        long mx = AsLong(args[1], 2, "math.randomInt");
        return System.Random.Shared.NextInt64(mn, mx + 1);
    }

    /// <summary>Constrains a number to be within a specified range.</summary>
    /// <param name="n">The number to clamp</param>
    /// <param name="min">The minimum value</param>
    /// <param name="max">The maximum value</param>
    /// <returns>The clamped value</returns>
    [StashFn(ReturnType = "number")]
    public static StashValue Clamp(
        [StashParam(Type = "number")] StashValue n,
        [StashParam(Type = "number")] StashValue min,
        [StashParam(Type = "number")] StashValue max)
    {
        double nv = AsNumber(n, 1, "math.clamp");
        double minV = AsNumber(min, 2, "math.clamp");
        double maxV = AsNumber(max, 3, "math.clamp");
        double result = Math.Clamp(nv, minV, maxV);
        if (n.IsInt && min.IsInt && max.IsInt)
            return StashValue.FromInt((long)result);
        return StashValue.FromFloat(result);
    }

    /// <summary>Returns the sine of an angle in radians.</summary>
    /// <param name="n">The angle in radians</param>
    /// <returns>The sine value</returns>
    [StashFn]
    public static double Sin([StashParam(Type = "number")] double n) => Math.Sin(n);

    /// <summary>Returns the cosine of an angle in radians.</summary>
    /// <param name="n">The angle in radians</param>
    /// <returns>The cosine value</returns>
    [StashFn]
    public static double Cos([StashParam(Type = "number")] double n) => Math.Cos(n);

    /// <summary>Returns the tangent of an angle in radians.</summary>
    /// <param name="n">The angle in radians</param>
    /// <returns>The tangent value</returns>
    [StashFn]
    public static double Tan([StashParam(Type = "number")] double n) => Math.Tan(n);

    /// <summary>Returns the arc sine (inverse sine) of a number in radians.</summary>
    /// <param name="n">The value (must be between -1 and 1)</param>
    /// <returns>The angle in radians</returns>
    [StashFn]
    public static double Asin([StashParam(Type = "number")] double n) => Math.Asin(n);

    /// <summary>Returns the arc cosine (inverse cosine) of a number in radians.</summary>
    /// <param name="n">The value (must be between -1 and 1)</param>
    /// <returns>The angle in radians</returns>
    [StashFn]
    public static double Acos([StashParam(Type = "number")] double n) => Math.Acos(n);

    /// <summary>Returns the arc tangent (inverse tangent) of a number in radians.</summary>
    /// <param name="n">The value</param>
    /// <returns>The angle in radians</returns>
    [StashFn]
    public static double Atan([StashParam(Type = "number")] double n) => Math.Atan(n);

    /// <summary>Returns the angle in radians between the positive x-axis and the point (x, y).</summary>
    /// <param name="y">The y coordinate</param>
    /// <param name="x">The x coordinate</param>
    /// <returns>The angle in radians</returns>
    [StashFn]
    public static double Atan2(
        [StashParam(Type = "number")] double y,
        [StashParam(Type = "number")] double x)
        => Math.Atan2(y, x);

    /// <summary>Returns the sign of a number: -1 for negative, 0 for zero, 1 for positive.</summary>
    /// <param name="n">The number</param>
    /// <returns>-1, 0, or 1</returns>
    [StashFn(ReturnType = "int")]
    public static long Sign([StashParam(Type = "number")] StashValue n)
    {
        if (n.IsInt) return Math.Sign(n.AsInt);
        if (n.IsFloat) return Math.Sign(n.AsFloat);
        throw new RuntimeError("First argument to 'math.sign' must be a number.", errorType: StashErrorTypes.TypeError);
    }

    /// <summary>Returns e raised to the specified power.</summary>
    /// <param name="n">The exponent</param>
    /// <returns>The value of e^n</returns>
    [StashFn]
    public static double Exp([StashParam(Type = "number")] double n) => Math.Exp(n);

    /// <summary>Returns the base-10 logarithm of a number.</summary>
    /// <param name="n">The number (must be positive)</param>
    /// <returns>The base-10 logarithm</returns>
    [StashFn]
    public static double Log10([StashParam(Type = "number")] double n) => Math.Log10(n);

    /// <summary>Returns the base-2 logarithm of a number.</summary>
    /// <param name="n">The number (must be positive)</param>
    /// <returns>The base-2 logarithm</returns>
    [StashFn]
    public static double Log2([StashParam(Type = "number")] double n) => Math.Log2(n);

    private static double AsNumber(StashValue v, int oneBasedIndex, string func)
    {
        if (v.IsInt) return v.AsInt;
        if (v.IsFloat) return v.AsFloat;
        throw new RuntimeError($"{SvArgs.Ordinal(oneBasedIndex - 1)} argument to '{func}' must be a number.");
    }

    private static long AsLong(StashValue v, int oneBasedIndex, string func)
    {
        if (v.IsInt) return v.AsInt;
        throw new RuntimeError($"{SvArgs.Ordinal(oneBasedIndex - 1)} argument to '{func}' must be an integer.");
    }
}
