namespace Stash.Stdlib.BuiltIns;

using System;
using Stash.Runtime;
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
        ns.Function("abs", [Param("n", "number")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            StashValue n = args[0];
            if (n.IsInt) return StashValue.FromInt(Math.Abs(n.AsInt));
            if (n.IsFloat) return StashValue.FromFloat(Math.Abs(n.AsFloat));
            throw new RuntimeError("First argument to 'math.abs' must be a number.");
        },
            returnType: "number",
            documentation: "Returns the absolute value of a number.\n@param n The number\n@return The absolute value");

        // math.ceil(n) — Returns the smallest integer greater than or equal to n.
        ns.Function("ceil", [Param("n", "number")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args[0].IsInt) return args[0];
            double d = SvArgs.Numeric(args, 0, "math.ceil");
            return StashValue.FromFloat(Math.Ceiling(d));
        },
            returnType: "number",
            documentation: "Returns the smallest integer greater than or equal to a number (rounds up).\n@param n The number to round up\n@return The ceiling value");

        // math.floor(n) — Returns the largest integer less than or equal to n.
        ns.Function("floor", [Param("n", "number")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args[0].IsInt) return args[0];
            double d = SvArgs.Numeric(args, 0, "math.floor");
            return StashValue.FromFloat(Math.Floor(d));
        },
            returnType: "number",
            documentation: "Returns the largest integer less than or equal to a number (rounds down).\n@param n The number to round down\n@return The floor value");

        // math.round(n, precision?) — Returns n rounded to the nearest integer, or to precision decimal places.
        ns.Function("round", [Param("n", "number"), Param("precision", "int")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 1 || args.Length > 2)
                throw new RuntimeError("'math.round' requires 1 or 2 arguments.");
            if (args[0].IsInt && args.Length == 1) return args[0];
            double d = SvArgs.Numeric(args, 0, "math.round");
            if (args.Length == 1)
                return StashValue.FromFloat(Math.Round(d, MidpointRounding.AwayFromZero));
            long precision = SvArgs.Long(args, 1, "math.round");
            if (precision >= 0)
                return StashValue.FromFloat(Math.Round(d, (int)precision, MidpointRounding.AwayFromZero));
            double factor = Math.Pow(10, -precision);
            return StashValue.FromFloat(Math.Round(d / factor, MidpointRounding.AwayFromZero) * factor);
        },
            returnType: "number",
            isVariadic: true,
            documentation: "Rounds a number to the nearest integer, or to a specified number of decimal places. Ties round away from zero.\n@param n The number to round\n@param precision (optional) Number of decimal places (positive) or significant digits to the left of the decimal (negative). Defaults to 0\n@return The rounded value");

        // math.min(a, b, ...args) — Returns the smallest of two or more numbers.
        ns.Function("min", [Param("a", "number"), Param("b", "number")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 2)
                throw new RuntimeError("'math.min' requires at least 2 arguments.");
            bool allInt = true;
            double result = SvArgs.Numeric(args, 0, "math.min");
            if (!args[0].IsInt) allInt = false;
            for (int i = 1; i < args.Length; i++)
            {
                if (!args[i].IsInt) allInt = false;
                double val = SvArgs.Numeric(args, i, "math.min");
                if (val < result) result = val;
            }
            return allInt ? StashValue.FromInt((long)result) : StashValue.FromFloat(result);
        },
            returnType: "number",
            isVariadic: true,
            documentation: "Returns the smallest of two or more numbers.\n@param a The first number\n@param b The second number\n@param args Additional numbers to compare\n@return The smallest value");

        // math.max(a, b, ...args) — Returns the largest of two or more numbers.
        ns.Function("max", [Param("a", "number"), Param("b", "number")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 2)
                throw new RuntimeError("'math.max' requires at least 2 arguments.");
            bool allInt = true;
            double result = SvArgs.Numeric(args, 0, "math.max");
            if (!args[0].IsInt) allInt = false;
            for (int i = 1; i < args.Length; i++)
            {
                if (!args[i].IsInt) allInt = false;
                double val = SvArgs.Numeric(args, i, "math.max");
                if (val > result) result = val;
            }
            return allInt ? StashValue.FromInt((long)result) : StashValue.FromFloat(result);
        },
            returnType: "number",
            isVariadic: true,
            documentation: "Returns the largest of two or more numbers.\n@param a The first number\n@param b The second number\n@param args Additional numbers to compare\n@return The largest value");

        // math.pow(base, exponent) — Returns base raised to the power of exponent as a double.
        ns.Function("pow", [Param("base", "number"), Param("exp", "number")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            double b = SvArgs.Numeric(args, 0, "math.pow");
            double e = SvArgs.Numeric(args, 1, "math.pow");
            return StashValue.FromFloat(Math.Pow(b, e));
        },
            returnType: "float",
            documentation: "Raises a number to a power.\n@param base The base number\n@param exp The exponent\n@return The result as a float");

        // math.sqrt(n) — Returns the square root of n as a double.
        ns.Function("sqrt", [Param("n", "number")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            double d = SvArgs.Numeric(args, 0, "math.sqrt");
            return StashValue.FromFloat(Math.Sqrt(d));
        },
            returnType: "float",
            documentation: "Returns the square root of a number.\n@param n The number (must be non-negative)\n@return The square root as a float");

        // math.log(n, base?) — Returns the natural logarithm, or logarithm of the specified base, of n.
        ns.Function("log", [Param("n", "number"), Param("base", "number")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 1 || args.Length > 2)
                throw new RuntimeError("'math.log' requires 1 or 2 arguments.");
            double d = SvArgs.Numeric(args, 0, "math.log");
            if (args.Length == 1)
                return StashValue.FromFloat(Math.Log(d));
            double @base = SvArgs.Numeric(args, 1, "math.log");
            return StashValue.FromFloat(Math.Log(d, @base));
        },
            returnType: "float",
            isVariadic: true,
            documentation: "Returns the logarithm of a number. When called with one argument, returns the natural logarithm (base e). When called with two arguments, returns the logarithm in the specified base.\n@param n The number (must be positive)\n@param base (optional) The logarithm base. Defaults to e (natural log)\n@return The logarithm as a float");

        // math.random() — Returns a random double in the range [0.0, 1.0).
        ns.Function("random", [], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            return StashValue.FromFloat(Random.Shared.NextDouble());
        },
            returnType: "float",
            documentation: "Returns a random float between 0.0 (inclusive) and 1.0 (exclusive).\n@return A random float in [0.0, 1.0)");

        // math.randomInt(min?, max?) — Returns a random integer. With 0 args: [0, int.MaxValue]; 1 arg: [0, max]; 2 args: [min, max].
        ns.Function("randomInt", [Param("min", "int"), Param("max", "int")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length > 2)
                throw new RuntimeError("'math.randomInt' requires 0, 1, or 2 arguments.");
            if (args.Length == 0)
                return StashValue.FromInt(Random.Shared.NextInt64(0, (long)int.MaxValue + 1));
            if (args.Length == 1)
            {
                long max = SvArgs.Long(args, 0, "math.randomInt");
                return StashValue.FromInt(Random.Shared.NextInt64(0, max + 1));
            }
            long min = SvArgs.Long(args, 0, "math.randomInt");
            long maxVal = SvArgs.Long(args, 1, "math.randomInt");
            return StashValue.FromInt(Random.Shared.NextInt64(min, maxVal + 1));
        },
            returnType: "int",
            isVariadic: true,
            documentation: "Returns a random integer. With no arguments, returns a random integer in [0, int.MaxValue]. With one argument, returns an integer in [0, max]. With two arguments, returns an integer in [min, max].\n@param min (optional) The minimum value (inclusive). Defaults to 0\n@param max (optional) The maximum value (inclusive). Defaults to int.MaxValue when no args given, otherwise required upper bound\n@return A random integer");

        // math.clamp(n, min, max) — Returns n clamped to the range [min, max].
        ns.Function("clamp", [Param("n", "number"), Param("min", "number"), Param("max", "number")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            double n = SvArgs.Numeric(args, 0, "math.clamp");
            double min = SvArgs.Numeric(args, 1, "math.clamp");
            double max = SvArgs.Numeric(args, 2, "math.clamp");
            double result = Math.Clamp(n, min, max);
            if (args[0].IsInt && args[1].IsInt && args[2].IsInt)
                return StashValue.FromInt((long)result);
            return StashValue.FromFloat(result);
        },
            returnType: "number",
            documentation: "Constrains a number to be within a specified range.\n@param n The number to clamp\n@param min The minimum value\n@param max The maximum value\n@return The clamped value");

        // math.sin(n) — Returns the sine of angle n (in radians) as a double.
        ns.Function("sin", [Param("n", "number")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            double d = SvArgs.Numeric(args, 0, "math.sin");
            return StashValue.FromFloat(Math.Sin(d));
        },
            returnType: "float",
            documentation: "Returns the sine of an angle in radians.\n@param n The angle in radians\n@return The sine value");

        // math.cos(n) — Returns the cosine of angle n (in radians) as a double.
        ns.Function("cos", [Param("n", "number")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            double d = SvArgs.Numeric(args, 0, "math.cos");
            return StashValue.FromFloat(Math.Cos(d));
        },
            returnType: "float",
            documentation: "Returns the cosine of an angle in radians.\n@param n The angle in radians\n@return The cosine value");

        // math.tan(n) — Returns the tangent of angle n (in radians) as a double.
        ns.Function("tan", [Param("n", "number")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            double d = SvArgs.Numeric(args, 0, "math.tan");
            return StashValue.FromFloat(Math.Tan(d));
        },
            returnType: "float",
            documentation: "Returns the tangent of an angle in radians.\n@param n The angle in radians\n@return The tangent value");

        // math.asin(n) — Returns the arcsine of n in radians as a double. Input must be in [-1, 1].
        ns.Function("asin", [Param("n", "number")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            double d = SvArgs.Numeric(args, 0, "math.asin");
            return StashValue.FromFloat(Math.Asin(d));
        },
            returnType: "float",
            documentation: "Returns the arc sine (inverse sine) of a number in radians.\n@param n The value (must be between -1 and 1)\n@return The angle in radians");

        // math.acos(n) — Returns the arccosine of n in radians as a double. Input must be in [-1, 1].
        ns.Function("acos", [Param("n", "number")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            double d = SvArgs.Numeric(args, 0, "math.acos");
            return StashValue.FromFloat(Math.Acos(d));
        },
            returnType: "float",
            documentation: "Returns the arc cosine (inverse cosine) of a number in radians.\n@param n The value (must be between -1 and 1)\n@return The angle in radians");

        // math.atan(n) — Returns the arctangent of n in radians as a double.
        ns.Function("atan", [Param("n", "number")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            double d = SvArgs.Numeric(args, 0, "math.atan");
            return StashValue.FromFloat(Math.Atan(d));
        },
            returnType: "float",
            documentation: "Returns the arc tangent (inverse tangent) of a number in radians.\n@param n The value\n@return The angle in radians");

        // math.atan2(y, x) — Returns the angle in radians between the positive x-axis and the point (x, y).
        ns.Function("atan2", [Param("y", "number"), Param("x", "number")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            double y = SvArgs.Numeric(args, 0, "math.atan2");
            double x = SvArgs.Numeric(args, 1, "math.atan2");
            return StashValue.FromFloat(Math.Atan2(y, x));
        },
            returnType: "float",
            documentation: "Returns the angle in radians between the positive x-axis and the point (x, y).\n@param y The y coordinate\n@param x The x coordinate\n@return The angle in radians");

        // math.sign(n) — Returns -1, 0, or 1 as a long indicating the sign of n.
        ns.Function("sign", [Param("n", "number")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            StashValue n = args[0];
            if (n.IsInt) return StashValue.FromInt((long)Math.Sign(n.AsInt));
            if (n.IsFloat) return StashValue.FromInt((long)Math.Sign(n.AsFloat));
            throw new RuntimeError("First argument to 'math.sign' must be a number.");
        },
            returnType: "int",
            documentation: "Returns the sign of a number: -1 for negative, 0 for zero, 1 for positive.\n@param n The number\n@return -1, 0, or 1");

        // math.exp(n) — Returns e raised to the power of n as a double (i.e. eⁿ).
        ns.Function("exp", [Param("n", "number")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            double d = SvArgs.Numeric(args, 0, "math.exp");
            return StashValue.FromFloat(Math.Exp(d));
        },
            returnType: "float",
            documentation: "Returns e raised to the specified power.\n@param n The exponent\n@return The value of e^n");

        // math.log10(n) — Returns the base-10 logarithm of n as a double.
        ns.Function("log10", [Param("n", "number")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            double d = SvArgs.Numeric(args, 0, "math.log10");
            return StashValue.FromFloat(Math.Log10(d));
        },
            returnType: "float",
            documentation: "Returns the base-10 logarithm of a number.\n@param n The number (must be positive)\n@return The base-10 logarithm");

        // math.log2(n) — Returns the base-2 logarithm of n as a double.
        ns.Function("log2", [Param("n", "number")], static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
        {
            double d = SvArgs.Numeric(args, 0, "math.log2");
            return StashValue.FromFloat(Math.Log2(d));
        },
            returnType: "float",
            documentation: "Returns the base-2 logarithm of a number.\n@param n The number (must be positive)\n@return The base-2 logarithm");

        return ns.Build();
    }

}
