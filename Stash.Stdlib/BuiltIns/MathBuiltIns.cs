namespace Stash.Stdlib.BuiltIns;

using System;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Models;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

/// <summary>
/// Registers the <c>math</c> namespace built-in functions and constants for mathematical operations.
/// </summary>
public static class MathBuiltIns
{
    public static NamespaceDefinition Define()
    {
        var ns = new NamespaceBuilder("math");

        // math.PI — The mathematical constant π (≈ 3.14159265358979…).
        ns.Constant("PI", Math.PI, "float", "3.141592653589793",
            "The ratio of a circle's circumference to its diameter (π ≈ 3.14159).");

        // math.E — Euler's number e (≈ 2.71828182845904…).
        ns.Constant("E", Math.E, "float", "2.718281828459045",
            "Euler's number, the base of natural logarithms (e ≈ 2.71828).");

        // math.abs(n) — Returns the absolute value of integer or float n.
        ns.Function("abs", [Param("n", "number")], (interp, args) =>
        {
            if (args[0] is long l)
            {
                return Math.Abs(l);
            }

            if (args[0] is double d)
            {
                return Math.Abs(d);
            }

            throw new RuntimeError("First argument to 'math.abs' must be a number.");
        },
            returnType: "number",
            documentation: "Returns the absolute value of a number.\n@param n The number\n@return The absolute value");

        // math.ceil(n) — Returns the smallest integer greater than or equal to n.
        ns.Function("ceil", [Param("n", "number")], (interp, args) =>
        {
            if (args[0] is long l)
            {
                return l;
            }

            double d = Args.Numeric(args, 0, "math.ceil");
            return Math.Ceiling(d);
        },
            returnType: "number",
            documentation: "Returns the smallest integer greater than or equal to a number (rounds up).\n@param n The number to round up\n@return The ceiling value");

        // math.floor(n) — Returns the largest integer less than or equal to n.
        ns.Function("floor", [Param("n", "number")], (interp, args) =>
        {
            if (args[0] is long l)
            {
                return l;
            }

            double d = Args.Numeric(args, 0, "math.floor");
            return Math.Floor(d);
        },
            returnType: "number",
            documentation: "Returns the largest integer less than or equal to a number (rounds down).\n@param n The number to round down\n@return The floor value");

        // math.round(n) — Returns n rounded to the nearest integer.
        ns.Function("round", [Param("n", "number")], (interp, args) =>
        {
            if (args[0] is long l)
            {
                return l;
            }

            double d = Args.Numeric(args, 0, "math.round");
            return Math.Round(d, MidpointRounding.AwayFromZero);
        },
            returnType: "number",
            documentation: "Rounds a number to the nearest integer. Ties round away from zero.\n@param n The number to round\n@return The rounded value");

        // math.min(a, b) — Returns the smaller of a and b.
        ns.Function("min", [Param("a", "number"), Param("b", "number")], (interp, args) =>
        {
            double a = Args.Numeric(args, 0, "math.min");
            double b = Args.Numeric(args, 1, "math.min");
            double result = Math.Min(a, b);
            if (args[0] is long && args[1] is long)
            {
                return (long)result;
            }

            return result;
        },
            returnType: "number",
            documentation: "Returns the smaller of two numbers.\n@param a The first number\n@param b The second number\n@return The smaller value");

        // math.max(a, b) — Returns the larger of a and b.
        ns.Function("max", [Param("a", "number"), Param("b", "number")], (interp, args) =>
        {
            double a = Args.Numeric(args, 0, "math.max");
            double b = Args.Numeric(args, 1, "math.max");
            double result = Math.Max(a, b);
            if (args[0] is long && args[1] is long)
            {
                return (long)result;
            }

            return result;
        },
            returnType: "number",
            documentation: "Returns the larger of two numbers.\n@param a The first number\n@param b The second number\n@return The larger value");

        // math.pow(base, exponent) — Returns base raised to the power of exponent as a double.
        ns.Function("pow", [Param("base", "number"), Param("exp", "number")], (interp, args) =>
        {
            double b = Args.Numeric(args, 0, "math.pow");
            double e = Args.Numeric(args, 1, "math.pow");
            return Math.Pow(b, e);
        },
            returnType: "float",
            documentation: "Raises a number to a power.\n@param base The base number\n@param exp The exponent\n@return The result as a float");

        // math.sqrt(n) — Returns the square root of n as a double.
        ns.Function("sqrt", [Param("n", "number")], (interp, args) =>
        {
            double d = Args.Numeric(args, 0, "math.sqrt");
            return Math.Sqrt(d);
        },
            returnType: "float",
            documentation: "Returns the square root of a number.\n@param n The number (must be non-negative)\n@return The square root as a float");

        // math.log(n) — Returns the natural logarithm (base e) of n as a double.
        ns.Function("log", [Param("n", "number")], (interp, args) =>
        {
            double d = Args.Numeric(args, 0, "math.log");
            return Math.Log(d);
        },
            returnType: "float",
            documentation: "Returns the natural logarithm (base e) of a number.\n@param n The number (must be positive)\n@return The natural logarithm as a float");

        // math.random() — Returns a random double in the range [0.0, 1.0).
        ns.Function("random", [], (interp, args) =>
        {
            return Random.Shared.NextDouble();
        },
            returnType: "float",
            documentation: "Returns a random float between 0.0 (inclusive) and 1.0 (exclusive).\n@return A random float in [0.0, 1.0)");

        // math.randomInt(min, max) — Returns a random integer in the inclusive range [min, max].
        ns.Function("randomInt", [Param("min", "int"), Param("max", "int")], (interp, args) =>
        {
            var min = Args.Long(args, 0, "math.randomInt");
            var max = Args.Long(args, 1, "math.randomInt");

            return (long)Random.Shared.NextInt64(min, max + 1);
        },
            returnType: "int",
            documentation: "Returns a random integer between min (inclusive) and max (inclusive).\n@param min The minimum value (inclusive)\n@param max The maximum value (inclusive)\n@return A random integer in [min, max]");

        // math.clamp(n, min, max) — Returns n clamped to the range [min, max].
        ns.Function("clamp", [Param("n", "number"), Param("min", "number"), Param("max", "number")], (interp, args) =>
        {
            double n = Args.Numeric(args, 0, "math.clamp");
            double min = Args.Numeric(args, 1, "math.clamp");
            double max = Args.Numeric(args, 2, "math.clamp");
            double result = Math.Clamp(n, min, max);
            if (args[0] is long && args[1] is long && args[2] is long)
            {
                return (long)result;
            }

            return result;
        },
            returnType: "number",
            documentation: "Constrains a number to be within a specified range.\n@param n The number to clamp\n@param min The minimum value\n@param max The maximum value\n@return The clamped value");

        // math.sin(n) — Returns the sine of angle n (in radians) as a double.
        ns.Function("sin", [Param("n", "number")], (interp, args) =>
        {
            double d = Args.Numeric(args, 0, "math.sin");
            return Math.Sin(d);
        },
            returnType: "float",
            documentation: "Returns the sine of an angle in radians.\n@param n The angle in radians\n@return The sine value");

        // math.cos(n) — Returns the cosine of angle n (in radians) as a double.
        ns.Function("cos", [Param("n", "number")], (interp, args) =>
        {
            double d = Args.Numeric(args, 0, "math.cos");
            return Math.Cos(d);
        },
            returnType: "float",
            documentation: "Returns the cosine of an angle in radians.\n@param n The angle in radians\n@return The cosine value");

        // math.tan(n) — Returns the tangent of angle n (in radians) as a double.
        ns.Function("tan", [Param("n", "number")], (interp, args) =>
        {
            double d = Args.Numeric(args, 0, "math.tan");
            return Math.Tan(d);
        },
            returnType: "float",
            documentation: "Returns the tangent of an angle in radians.\n@param n The angle in radians\n@return The tangent value");

        // math.asin(n) — Returns the arcsine of n in radians as a double. Input must be in [-1, 1].
        ns.Function("asin", [Param("n", "number")], (interp, args) =>
        {
            double d = Args.Numeric(args, 0, "math.asin");
            return Math.Asin(d);
        },
            returnType: "float",
            documentation: "Returns the arc sine (inverse sine) of a number in radians.\n@param n The value (must be between -1 and 1)\n@return The angle in radians");

        // math.acos(n) — Returns the arccosine of n in radians as a double. Input must be in [-1, 1].
        ns.Function("acos", [Param("n", "number")], (interp, args) =>
        {
            double d = Args.Numeric(args, 0, "math.acos");
            return Math.Acos(d);
        },
            returnType: "float",
            documentation: "Returns the arc cosine (inverse cosine) of a number in radians.\n@param n The value (must be between -1 and 1)\n@return The angle in radians");

        // math.atan(n) — Returns the arctangent of n in radians as a double.
        ns.Function("atan", [Param("n", "number")], (interp, args) =>
        {
            double d = Args.Numeric(args, 0, "math.atan");
            return Math.Atan(d);
        },
            returnType: "float",
            documentation: "Returns the arc tangent (inverse tangent) of a number in radians.\n@param n The value\n@return The angle in radians");

        // math.atan2(y, x) — Returns the angle in radians between the positive x-axis and the point (x, y).
        ns.Function("atan2", [Param("y", "number"), Param("x", "number")], (interp, args) =>
        {
            double y = Args.Numeric(args, 0, "math.atan2");
            double x = Args.Numeric(args, 1, "math.atan2");
            return Math.Atan2(y, x);
        },
            returnType: "float",
            documentation: "Returns the angle in radians between the positive x-axis and the point (x, y).\n@param y The y coordinate\n@param x The x coordinate\n@return The angle in radians");

        // math.sign(n) — Returns -1, 0, or 1 as a long indicating the sign of n.
        ns.Function("sign", [Param("n", "number")], (interp, args) =>
        {
            if (args[0] is long l)
            {
                return (long)Math.Sign(l);
            }

            if (args[0] is double d)
            {
                return (long)Math.Sign(d);
            }

            throw new RuntimeError("First argument to 'math.sign' must be a number.");
        },
            returnType: "int",
            documentation: "Returns the sign of a number: -1 for negative, 0 for zero, 1 for positive.\n@param n The number\n@return -1, 0, or 1");

        // math.exp(n) — Returns e raised to the power of n as a double (i.e. eⁿ).
        ns.Function("exp", [Param("n", "number")], (interp, args) =>
        {
            double d = Args.Numeric(args, 0, "math.exp");
            return Math.Exp(d);
        },
            returnType: "float",
            documentation: "Returns e raised to the specified power.\n@param n The exponent\n@return The value of e^n");

        // math.log10(n) — Returns the base-10 logarithm of n as a double.
        ns.Function("log10", [Param("n", "number")], (interp, args) =>
        {
            double d = Args.Numeric(args, 0, "math.log10");
            return Math.Log10(d);
        },
            returnType: "float",
            documentation: "Returns the base-10 logarithm of a number.\n@param n The number (must be positive)\n@return The base-10 logarithm");

        // math.log2(n) — Returns the base-2 logarithm of n as a double.
        ns.Function("log2", [Param("n", "number")], (interp, args) =>
        {
            double d = Args.Numeric(args, 0, "math.log2");
            return Math.Log2(d);
        },
            returnType: "float",
            documentation: "Returns the base-2 logarithm of a number.\n@param n The number (must be positive)\n@return The base-2 logarithm");

        return ns.Build();
    }

}
