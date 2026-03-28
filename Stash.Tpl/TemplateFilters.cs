namespace Stash.Tpl;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Stash.Runtime;
using Stash.Runtime.Types;

/// <summary>
/// Static registry of all built-in template filters applied via the pipe operator
/// in output expressions (e.g. <c>{{ name | upper }}</c> or <c>{{ list | join(", ") }}</c>).
/// </summary>
/// <remarks>
/// <para>
/// Filters are invoked by <see cref="TemplateRenderer"/> after an expression has been
/// evaluated by the Stash interpreter.  Each filter receives the current value and an
/// optional array of string arguments parsed from the filter's parenthesised argument list.
/// </para>
/// <para>
/// The 19 built-in filters are:
/// <c>upper</c>, <c>lower</c>, <c>trim</c>, <c>capitalize</c>, <c>title</c>,
/// <c>length</c>, <c>reverse</c>, <c>first</c>, <c>last</c>, <c>sort</c>,
/// <c>join</c>, <c>split</c>, <c>replace</c>, <c>default</c>,
/// <c>round</c>, <c>abs</c>, <c>keys</c>, <c>values</c>, <c>json</c>.
/// </para>
/// </remarks>
public static class TemplateFilters
{
    /// <summary>
    /// Looks up and invokes the named filter, passing <paramref name="value"/> and
    /// <paramref name="args"/> to the corresponding implementation.
    /// </summary>
    /// <param name="name">The filter name as it appears in template source (e.g. <c>"upper"</c>).</param>
    /// <param name="value">The value produced by the expression or the previous filter in the pipeline.</param>
    /// <param name="args">
    /// Zero or more string arguments parsed from the filter's parenthesised argument list.
    /// Surrounding quotes have already been stripped by <see cref="TemplateParser"/>.
    /// </param>
    /// <param name="evaluator">
    /// The active template evaluator, required by filters that evaluate expressions
    /// (currently only <c>default</c>).
    /// </param>
    /// <returns>The transformed value.</returns>
    /// <exception cref="TemplateException">Thrown for unrecognised filter names.</exception>
    public static object? Apply(string name, object? value, string[] args, ITemplateEvaluator evaluator)
    {
        return name switch
        {
            "upper" => Upper(value),
            "lower" => Lower(value),
            "trim" => Trim(value),
            "length" => Length(value),
            "reverse" => Reverse(value),
            "join" => Join(value, args),
            "split" => Split(value, args),
            "replace" => Replace(value, args),
            "default" => Default(value, args, evaluator),
            "round" => Round(value),
            "abs" => Abs(value),
            "keys" => Keys(value),
            "values" => Values(value),
            "json" => JsonEncode(value),
            "first" => First(value),
            "last" => Last(value),
            "sort" => Sort(value),
            "capitalize" => Capitalize(value),
            "title" => TitleCase(value),
            _ => throw new TemplateException($"Unknown filter '{name}'.")
        };
    }

    /// <summary>
    /// Converts a string to upper-case using the invariant culture
    /// (e.g. <c>{{ "hello" | upper }}</c> → <c>"HELLO"</c>).
    /// </summary>
    /// <exception cref="TemplateException">Thrown when <paramref name="value"/> is not a string.</exception>
    private static object? Upper(object? value)
    {
        if (value is string s)
        {
            return s.ToUpperInvariant();
        }

        throw new TemplateException("Filter 'upper' requires a string value.");
    }

    /// <summary>
    /// Converts a string to lower-case using the invariant culture
    /// (e.g. <c>{{ "HELLO" | lower }}</c> → <c>"hello"</c>).
    /// </summary>
    /// <exception cref="TemplateException">Thrown when <paramref name="value"/> is not a string.</exception>
    private static object? Lower(object? value)
    {
        if (value is string s)
        {
            return s.ToLowerInvariant();
        }

        throw new TemplateException("Filter 'lower' requires a string value.");
    }

    /// <summary>
    /// Removes leading and trailing whitespace from a string
    /// (e.g. <c>{{ "  hi  " | trim }}</c> → <c>"hi"</c>).
    /// </summary>
    /// <exception cref="TemplateException">Thrown when <paramref name="value"/> is not a string.</exception>
    private static object? Trim(object? value)
    {
        if (value is string s)
        {
            return s.Trim();
        }

        throw new TemplateException("Filter 'trim' requires a string value.");
    }

    /// <summary>
    /// Returns the number of characters in a string, elements in an array, or
    /// entries in a dictionary (e.g. <c>{{ items | length }}</c>).
    /// </summary>
    /// <exception cref="TemplateException">
    /// Thrown when <paramref name="value"/> is not a string, array, or dictionary.
    /// </exception>
    private static object? Length(object? value)
    {
        if (value is string s)
        {
            return (long)s.Length;
        }

        if (value is List<object?> list)
        {
            return (long)list.Count;
        }

        if (value is StashDictionary dict)
        {
            return (long)dict.Count;
        }

        throw new TemplateException("Filter 'length' requires a string, array, or dictionary.");
    }

    /// <summary>
    /// Reverses a string character-by-character or reverses the order of elements in an array.
    /// Returns a new value without mutating the original.
    /// </summary>
    /// <exception cref="TemplateException">
    /// Thrown when <paramref name="value"/> is neither a string nor an array.
    /// </exception>
    private static object? Reverse(object? value)
    {
        if (value is string s)
        {
            var chars = s.ToCharArray();
            Array.Reverse(chars);
            return new string(chars);
        }
        if (value is List<object?> list)
        {
            var copy = new List<object?>(list);
            copy.Reverse();
            return copy;
        }
        throw new TemplateException("Filter 'reverse' requires a string or array.");
    }

    /// <summary>
    /// Joins all elements of an array into a single string using a separator
    /// (e.g. <c>{{ tags | join(", ") }}</c>).
    /// </summary>
    /// <param name="value">The array to join.</param>
    /// <param name="args"><c>args[0]</c> is the separator string.</param>
    /// <exception cref="TemplateException">
    /// Thrown when no separator argument is provided or <paramref name="value"/> is not an array.
    /// </exception>
    private static object? Join(object? value, string[] args)
    {
        if (args.Length < 1)
        {
            throw new TemplateException("Filter 'join' requires a separator argument.");
        }

        if (value is not List<object?> list)
        {
            throw new TemplateException("Filter 'join' requires an array value.");
        }

        var separator = args[0];
        return string.Join(separator, list.Select(RuntimeValues.Stringify));
    }

    /// <summary>
    /// Splits a string on a delimiter and returns the resulting array
    /// (e.g. <c>{{ csv | split(",") }}</c>).
    /// </summary>
    /// <param name="value">The string to split.</param>
    /// <param name="args"><c>args[0]</c> is the delimiter string.</param>
    /// <exception cref="TemplateException">
    /// Thrown when no delimiter argument is provided or <paramref name="value"/> is not a string.
    /// </exception>
    private static object? Split(object? value, string[] args)
    {
        if (args.Length < 1)
        {
            throw new TemplateException("Filter 'split' requires a delimiter argument.");
        }

        if (value is not string s)
        {
            throw new TemplateException("Filter 'split' requires a string value.");
        }

        var parts = s.Split(args[0]);
        return new List<object?>(parts.Select(p => (object?)p));
    }

    /// <summary>
    /// Replaces all occurrences of a substring with another string
    /// (e.g. <c>{{ text | replace("foo", "bar") }}</c>).
    /// </summary>
    /// <param name="value">The source string.</param>
    /// <param name="args"><c>args[0]</c> is the old value; <c>args[1]</c> is the new value.</param>
    /// <exception cref="TemplateException">
    /// Thrown when fewer than two arguments are provided or <paramref name="value"/> is not a string.
    /// </exception>
    private static object? Replace(object? value, string[] args)
    {
        if (args.Length < 2)
        {
            throw new TemplateException("Filter 'replace' requires two arguments: old and new.");
        }

        if (value is not string s)
        {
            throw new TemplateException("Filter 'replace' requires a string value.");
        }

        return s.Replace(args[0], args[1]);
    }

    /// <summary>
    /// Returns <paramref name="value"/> unchanged if it is non-<see langword="null"/>;
    /// otherwise evaluates the fallback argument as a Stash expression and returns that
    /// (e.g. <c>{{ user.bio | default("No bio provided.") }}</c>).
    /// </summary>
    /// <remarks>
    /// The fallback argument is first attempted as a Stash expression evaluation.  If that
    /// fails the raw argument string is returned verbatim.
    /// </remarks>
    /// <param name="value">The value to test for nullness.</param>
    /// <param name="args"><c>args[0]</c> is the fallback expression or literal string.</param>
    /// <param name="evaluator">Used to evaluate the fallback argument as a Stash expression.</param>
    /// <exception cref="TemplateException">Thrown when no fallback argument is provided.</exception>
    private static object? Default(object? value, string[] args, ITemplateEvaluator evaluator)
    {
        if (args.Length < 1)
        {
            throw new TemplateException("Filter 'default' requires a fallback value argument.");
        }

        if (value is null)
        {
            try
            {
                // Try to evaluate the argument as a Stash expression to handle strings, numbers, etc.
                var (result, error) = evaluator.EvaluateExpression(args[0], evaluator.GlobalEnvironment);
                if (error is null)
                {
                    return result;
                }
            }
            catch
            {
                // Fall through to return raw string
            }
            return args[0];
        }
        return value;
    }

    /// <summary>
    /// Rounds a floating-point number to the nearest integer using midpoint-away-from-zero
    /// rounding (e.g. <c>{{ 2.5 | round }}</c> → <c>3</c>).
    /// Integer values are returned unchanged.
    /// </summary>
    /// <exception cref="TemplateException">Thrown when <paramref name="value"/> is not numeric.</exception>
    private static object? Round(object? value)
    {
        if (value is double d)
        {
            return Math.Round(d, MidpointRounding.AwayFromZero);
        }

        if (value is long)
        {
            return value;
        }

        throw new TemplateException("Filter 'round' requires a numeric value.");
    }

    /// <summary>
    /// Returns the absolute value of a number (e.g. <c>{{ -5 | abs }}</c> → <c>5</c>).
    /// Works for both <see cref="double"/> and <see cref="long"/> values.
    /// </summary>
    /// <exception cref="TemplateException">Thrown when <paramref name="value"/> is not numeric.</exception>
    private static object? Abs(object? value)
    {
        if (value is double d)
        {
            return Math.Abs(d);
        }

        if (value is long l)
        {
            return Math.Abs(l);
        }

        throw new TemplateException("Filter 'abs' requires a numeric value.");
    }

    /// <summary>
    /// Returns all keys of a <see cref="StashDictionary"/> as an array
    /// (e.g. <c>{{ config | keys }}</c>).
    /// </summary>
    /// <exception cref="TemplateException">Thrown when <paramref name="value"/> is not a dictionary.</exception>
    private static object? Keys(object? value)
    {
        if (value is StashDictionary dict)
        {
            return dict.Keys();
        }

        throw new TemplateException("Filter 'keys' requires a dictionary value.");
    }

    /// <summary>
    /// Returns all values of a <see cref="StashDictionary"/> as an array
    /// (e.g. <c>{{ config | values }}</c>).
    /// </summary>
    /// <exception cref="TemplateException">Thrown when <paramref name="value"/> is not a dictionary.</exception>
    private static object? Values(object? value)
    {
        if (value is StashDictionary dict)
        {
            return dict.Values();
        }

        throw new TemplateException("Filter 'values' requires a dictionary value.");
    }

    /// <summary>
    /// Serialises any value to its JSON-compatible string representation using
    /// <c>RuntimeValues.Stringify</c> (e.g. <c>{{ data | json }}</c>).
    /// </summary>
    private static object? JsonEncode(object? value)
    {
        return RuntimeValues.Stringify(value);
    }

    /// <summary>
    /// Returns the first character of a string or the first element of an array,
    /// or <see langword="null"/> when the value is empty
    /// (e.g. <c>{{ items | first }}</c>).
    /// </summary>
    /// <exception cref="TemplateException">
    /// Thrown when <paramref name="value"/> is neither a string nor an array.
    /// </exception>
    private static object? First(object? value)
    {
        if (value is string s)
        {
            return s.Length > 0 ? s[0].ToString() : null;
        }

        if (value is List<object?> list)
        {
            return list.Count > 0 ? list[0] : null;
        }

        throw new TemplateException("Filter 'first' requires a string or array.");
    }

    /// <summary>
    /// Returns the last character of a string or the last element of an array,
    /// or <see langword="null"/> when the value is empty
    /// (e.g. <c>{{ items | last }}</c>).
    /// </summary>
    /// <exception cref="TemplateException">
    /// Thrown when <paramref name="value"/> is neither a string nor an array.
    /// </exception>
    private static object? Last(object? value)
    {
        if (value is string s)
        {
            return s.Length > 0 ? s[^1].ToString() : null;
        }

        if (value is List<object?> list)
        {
            return list.Count > 0 ? list[^1] : null;
        }

        throw new TemplateException("Filter 'last' requires a string or array.");
    }

    /// <summary>
    /// Returns a sorted copy of an array.  Numbers are sorted numerically; strings are
    /// sorted with ordinal comparison; mixed types fall back to stringified comparison
    /// (e.g. <c>{{ scores | sort }}</c>).
    /// </summary>
    /// <exception cref="TemplateException">Thrown when <paramref name="value"/> is not an array.</exception>
    private static object? Sort(object? value)
    {
        if (value is not List<object?> list)
        {
            throw new TemplateException("Filter 'sort' requires an array.");
        }

        var copy = new List<object?>(list);
        copy.Sort((a, b) =>
        {
            if (a is long la && b is long lb)
            {
                return la.CompareTo(lb);
            }

            if (a is double da && b is double db)
            {
                return da.CompareTo(db);
            }

            if (a is string sa && b is string sb)
            {
                return string.Compare(sa, sb, StringComparison.Ordinal);
            }

            return string.Compare(
                RuntimeValues.Stringify(a),
                RuntimeValues.Stringify(b),
                StringComparison.Ordinal);
        });
        return copy;
    }

    /// <summary>
    /// Capitalizes the first character of a string and lower-cases the rest
    /// (e.g. <c>{{ "hELLO" | capitalize }}</c> → <c>"Hello"</c>).
    /// </summary>
    /// <exception cref="TemplateException">Thrown when <paramref name="value"/> is not a string.</exception>
    private static object? Capitalize(object? value)
    {
        if (value is not string s)
        {
            throw new TemplateException("Filter 'capitalize' requires a string value.");
        }

        if (s.Length == 0)
        {
            return s;
        }

        return char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();
    }

    /// <summary>
    /// Converts a string to title case using the invariant culture, where the first
    /// letter of each word is capitalised (e.g. <c>{{ "hello world" | title }}</c> →
    /// <c>"Hello World"</c>).
    /// </summary>
    /// <exception cref="TemplateException">Thrown when <paramref name="value"/> is not a string.</exception>
    private static object? TitleCase(object? value)
    {
        if (value is not string s)
        {
            throw new TemplateException("Filter 'title' requires a string value.");
        }

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());
    }
}
