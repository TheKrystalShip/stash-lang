namespace Stash.Interpreting.Templating;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Stash.Interpreting.Types;

/// <summary>
/// Registry of template filters. Each filter transforms a value, optionally taking arguments.
/// Filters are applied via pipe syntax in output expressions: {{ value | filterName(args) }}
/// </summary>
public static class TemplateFilters
{
    /// <summary>
    /// Applies a named filter to a value with optional arguments.
    /// </summary>
    public static object? Apply(string name, object? value, string[] args, Interpreter interpreter)
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
            "default" => Default(value, args, interpreter),
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

    private static object? Upper(object? value)
    {
        if (value is string s) return s.ToUpperInvariant();
        throw new TemplateException("Filter 'upper' requires a string value.");
    }

    private static object? Lower(object? value)
    {
        if (value is string s) return s.ToLowerInvariant();
        throw new TemplateException("Filter 'lower' requires a string value.");
    }

    private static object? Trim(object? value)
    {
        if (value is string s) return s.Trim();
        throw new TemplateException("Filter 'trim' requires a string value.");
    }

    private static object? Length(object? value)
    {
        if (value is string s) return (long)s.Length;
        if (value is List<object?> list) return (long)list.Count;
        if (value is StashDictionary dict) return (long)dict.Count;
        throw new TemplateException("Filter 'length' requires a string, array, or dictionary.");
    }

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

    private static object? Join(object? value, string[] args)
    {
        if (args.Length < 1)
            throw new TemplateException("Filter 'join' requires a separator argument.");
        if (value is not List<object?> list)
            throw new TemplateException("Filter 'join' requires an array value.");

        var separator = args[0];
        return string.Join(separator, list.Select(RuntimeValues.Stringify));
    }

    private static object? Split(object? value, string[] args)
    {
        if (args.Length < 1)
            throw new TemplateException("Filter 'split' requires a delimiter argument.");
        if (value is not string s)
            throw new TemplateException("Filter 'split' requires a string value.");

        var parts = s.Split(args[0]);
        return new List<object?>(parts.Select(p => (object?)p));
    }

    private static object? Replace(object? value, string[] args)
    {
        if (args.Length < 2)
            throw new TemplateException("Filter 'replace' requires two arguments: old and new.");
        if (value is not string s)
            throw new TemplateException("Filter 'replace' requires a string value.");

        return s.Replace(args[0], args[1]);
    }

    private static object? Default(object? value, string[] args, Interpreter interpreter)
    {
        if (args.Length < 1)
            throw new TemplateException("Filter 'default' requires a fallback value argument.");

        if (value is null)
        {
            try
            {
                // Try to evaluate the argument as a Stash expression to handle strings, numbers, etc.
                var (result, error) = interpreter.EvaluateString(args[0], interpreter.Globals);
                if (error is null) return result;
            }
            catch
            {
                // Fall through to return raw string
            }
            return args[0];
        }
        return value;
    }

    private static object? Round(object? value)
    {
        if (value is double d) return (long)Math.Round(d);
        if (value is long) return value;
        throw new TemplateException("Filter 'round' requires a numeric value.");
    }

    private static object? Abs(object? value)
    {
        if (value is double d) return Math.Abs(d);
        if (value is long l) return Math.Abs(l);
        throw new TemplateException("Filter 'abs' requires a numeric value.");
    }

    private static object? Keys(object? value)
    {
        if (value is StashDictionary dict) return dict.Keys();
        throw new TemplateException("Filter 'keys' requires a dictionary value.");
    }

    private static object? Values(object? value)
    {
        if (value is StashDictionary dict) return dict.Values();
        throw new TemplateException("Filter 'values' requires a dictionary value.");
    }

    private static object? JsonEncode(object? value)
    {
        return RuntimeValues.Stringify(value);
    }

    private static object? First(object? value)
    {
        if (value is string s) return s.Length > 0 ? s[0].ToString() : null;
        if (value is List<object?> list) return list.Count > 0 ? list[0] : null;
        throw new TemplateException("Filter 'first' requires a string or array.");
    }

    private static object? Last(object? value)
    {
        if (value is string s) return s.Length > 0 ? s[^1].ToString() : null;
        if (value is List<object?> list) return list.Count > 0 ? list[^1] : null;
        throw new TemplateException("Filter 'last' requires a string or array.");
    }

    private static object? Sort(object? value)
    {
        if (value is not List<object?> list)
            throw new TemplateException("Filter 'sort' requires an array.");

        var copy = new List<object?>(list);
        copy.Sort((a, b) =>
        {
            if (a is long la && b is long lb) return la.CompareTo(lb);
            if (a is double da && b is double db) return da.CompareTo(db);
            if (a is string sa && b is string sb) return string.Compare(sa, sb, StringComparison.Ordinal);
            return string.Compare(
                RuntimeValues.Stringify(a),
                RuntimeValues.Stringify(b),
                StringComparison.Ordinal);
        });
        return copy;
    }

    private static object? Capitalize(object? value)
    {
        if (value is not string s)
            throw new TemplateException("Filter 'capitalize' requires a string value.");
        if (s.Length == 0) return s;
        return char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();
    }

    private static object? TitleCase(object? value)
    {
        if (value is not string s)
            throw new TemplateException("Filter 'title' requires a string value.");
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());
    }
}
