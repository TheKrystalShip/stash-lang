namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Abstractions;
using Stash.Runtime.Errors;

[StashNamespace]
public static partial class JsonBuiltIns
{
    /// <summary>Parses a JSON string into a Stash value (dict, array, string, number, bool, or null).</summary>
    /// <param name="str">The JSON string to parse</param>
    /// <exception cref="ParseError">if the input is not valid JSON</exception>
    /// <exception cref="TypeError">if the argument is not a string</exception>
    /// <returns>The parsed value</returns>
    [StashFn(ReturnType = "any")]
    private static StashValue Parse(string str)
    {
        try
        {
            using var doc = JsonDocument.Parse(str);
            return StashValue.FromObject(ConvertElement(doc.RootElement));
        }
        catch (JsonException e)
        {
            throw new ParseError("json.parse: invalid JSON — " + e.Message);
        }
    }

    /// <summary>Converts a Stash value to a JSON string. When indent is 0 or omitted, outputs compact JSON; when positive, outputs pretty-printed JSON with that many spaces of indentation.</summary>
    /// <param name="value">The value to serialize</param>
    /// <param name="indent">Number of spaces for indentation (optional, default 0 for compact)</param>
    /// <exception cref="TypeError">if the value contains a type that cannot be serialized to JSON, or if indent is not a number</exception>
    /// <returns>The JSON string representation</returns>
    [StashFn(ReturnType = "string")]
    private static string Stringify(StashValue value, [StashParam(Type = "number")] double indent = 0)
    {
        if (indent > 0)
            return PrettyValue(value.ToObject(), 0, (int)indent);
        return StringifyValue(value.ToObject());
    }

    /// <summary>Converts a Stash value to a formatted JSON string with indentation. Defaults to 2 spaces.</summary>
    /// <param name="value">The value to serialize</param>
    /// <param name="indent">Number of spaces for indentation (optional, default 2)</param>
    /// <exception cref="TypeError">if the value contains a type that cannot be serialized to JSON, or if indent is not a number</exception>
    /// <returns>The pretty-printed JSON string</returns>
    [StashFn(ReturnType = "string")]
    private static string Pretty(StashValue value, [StashParam(Type = "number")] double indent = 2)
    {
        return PrettyValue(value.ToObject(), 0, (int)indent);
    }

    /// <summary>Checks whether a string is valid JSON without parsing it into a value.</summary>
    /// <param name="text">The string to validate</param>
    /// <exception cref="TypeError">if the argument is not a string</exception>
    /// <returns>true if the string is valid JSON</returns>
    [StashFn]
    public static bool Valid(string text)
    {
        try { using var doc = JsonDocument.Parse(text); return true; }
        catch (JsonException) { return false; }
    }

    /// <summary>Recursively converts a <see cref="System.Text.Json.JsonElement"/> to a Stash runtime value.</summary>
    /// <param name="element">The JSON element to convert.</param>
    /// <returns>A Stash runtime value: <c>string</c>, <c>long</c>, <c>double</c>, <c>bool</c>, <c>null</c>, <c>List&lt;object?&gt;</c>, or <see cref="StashDictionary"/>.</returns>
    private static object? ConvertElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString()!,
            JsonValueKind.Number => element.TryGetInt64(out long l) ? (object?)l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => ConvertArray(element),
            JsonValueKind.Object => ConvertObject(element),
            _ => null
        };
    }

    /// <summary>Converts a JSON array element to a <c>List&lt;StashValue&gt;</c>.</summary>
    /// <param name="element">The JSON array element to convert.</param>
    /// <returns>A list of converted Stash values.</returns>
    private static List<StashValue> ConvertArray(JsonElement element)
    {
        var list = new List<StashValue>();
        foreach (var item in element.EnumerateArray())
        {
            list.Add(StashValue.FromObject(ConvertElement(item)));
        }

        return list;
    }

    /// <summary>Converts a JSON object element to a <see cref="StashDictionary"/>.</summary>
    /// <param name="element">The JSON object element to convert.</param>
    /// <returns>A <see cref="StashDictionary"/> with string keys and converted values.</returns>
    private static StashDictionary ConvertObject(JsonElement element)
    {
        var dict = new StashDictionary();
        foreach (var prop in element.EnumerateObject())
        {
            dict.Set(prop.Name, StashValue.FromObject(ConvertElement(prop.Value)));
        }

        return dict;
    }

    /// <summary>Converts a Stash runtime value to its compact JSON string representation.</summary>
    /// <param name="value">The Stash value to serialize.</param>
    /// <returns>A compact JSON string.</returns>
    private static string StringifyValue(object? value)
    {
        return value switch
        {
            null => "null",
            bool b => b ? "true" : "false",
            long l => l.ToString(CultureInfo.InvariantCulture),
            double d => d.ToString("G", CultureInfo.InvariantCulture),
            byte b => b.ToString(CultureInfo.InvariantCulture),
            string s => JsonSerializer.Serialize(s, StashJsonContext.Default.String),
            List<StashValue> arr => StringifyArray(arr),
            StashDictionary dict => StringifyDict(dict),
            StashInstance inst => StringifyInstance(inst),
            StashByteArray ba => JsonSerializer.Serialize(Convert.ToBase64String(ba.AsSpan()), StashJsonContext.Default.String),
            StashTypedArray ta => StringifyTypedArray(ta),
            _ => throw new TypeError($"json.stringify: cannot serialize value of type {value.GetType().Name}.")
        };
    }

    private static string StringifyTypedArray(StashTypedArray ta)
    {
        var list = new List<StashValue>(ta.Count);
        for (int i = 0; i < ta.Count; i++)
            list.Add(ta.Get(i));
        return StringifyArray(list);
    }

    /// <summary>Serializes a Stash array to a compact JSON array string.</summary>
    /// <param name="arr">The array to serialize.</param>
    /// <returns>A compact JSON array string.</returns>
    private static string StringifyArray(List<StashValue> arr)
    {
        var sb = new StringBuilder("[");
        for (int i = 0; i < arr.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            sb.Append(StringifyValue(arr[i].ToObject()));
        }
        sb.Append(']');
        return sb.ToString();
    }

    /// <summary>Serializes a <see cref="StashDictionary"/> to a compact JSON object string.</summary>
    /// <param name="dict">The dictionary to serialize.</param>
    /// <returns>A compact JSON object string.</returns>
    private static string StringifyDict(StashDictionary dict)
    {
        var sb = new StringBuilder("{");
        var keys = dict.RawKeys();
        for (int i = 0; i < keys.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            string keyStr = StringifyKey(keys[i]);
            object? val = dict.Get(keys[i]).ToObject();
            sb.Append(keyStr);
            sb.Append(':');
            sb.Append(StringifyValue(val));
        }
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>Serializes a <see cref="StashInstance"/> to a compact JSON object string using its fields.</summary>
    /// <param name="inst">The struct instance to serialize.</param>
    /// <returns>A compact JSON object string.</returns>
    private static string StringifyInstance(StashInstance inst)
    {
        var sb = new StringBuilder("{");
        bool first = true;
        foreach (var kvp in inst.GetFields())
        {
            if (!first)
            {
                sb.Append(',');
            }

            first = false;
            sb.Append(JsonSerializer.Serialize(kvp.Key, StashJsonContext.Default.String));
            sb.Append(':');
            sb.Append(StringifyValue(kvp.Value.ToObject()));
        }
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>Converts a dictionary key to a JSON-quoted string.</summary>
    /// <param name="key">The dictionary key to convert.</param>
    /// <returns>A JSON-quoted string representation of the key.</returns>
    private static string StringifyKey(object key)
    {
        return key switch
        {
            string s => JsonSerializer.Serialize(s, StashJsonContext.Default.String),
            long l => JsonSerializer.Serialize(l.ToString(CultureInfo.InvariantCulture), StashJsonContext.Default.String),
            double d => JsonSerializer.Serialize(d.ToString("G", CultureInfo.InvariantCulture), StashJsonContext.Default.String),
            bool b => JsonSerializer.Serialize(b ? "true" : "false", StashJsonContext.Default.String),
            _ => throw new TypeError("json.stringify: dict key must be a string, number, or bool.")
        };
    }

    /// <summary>Converts a Stash runtime value to a pretty-printed JSON string with indentation.</summary>
    /// <param name="value">The Stash value to serialize.</param>
    /// <param name="indent">The current indentation level.</param>
    /// <returns>A pretty-printed JSON string.</returns>
    private static string PrettyValue(object? value, int indent, int indentWidth)
    {
        return value switch
        {
            null => "null",
            bool b => b ? "true" : "false",
            long l => l.ToString(CultureInfo.InvariantCulture),
            double d => d.ToString("G", CultureInfo.InvariantCulture),
            string s => JsonSerializer.Serialize(s, StashJsonContext.Default.String),
            List<StashValue> arr => PrettyArray(arr, indent, indentWidth),
            StashDictionary dict => PrettyDict(dict, indent, indentWidth),
            StashInstance inst => PrettyInstance(inst, indent, indentWidth),
            StashByteArray ba => JsonSerializer.Serialize(Convert.ToBase64String(ba.AsSpan()), StashJsonContext.Default.String),
            StashTypedArray ta => PrettyTypedArray(ta, indent, indentWidth),
            _ => throw new TypeError($"json.pretty: cannot serialize value of type {value.GetType().Name}.")
        };
    }

    private static string PrettyTypedArray(StashTypedArray ta, int indent, int indentWidth)
    {
        var list = new List<StashValue>(ta.Count);
        for (int i = 0; i < ta.Count; i++)
            list.Add(ta.Get(i));
        return PrettyArray(list, indent, indentWidth);
    }

    /// <summary>Pretty-prints a Stash array as a JSON array with indentation.</summary>
    /// <param name="arr">The array to pretty-print.</param>
    /// <param name="indent">The current indentation level.</param>
    /// <returns>A pretty-printed JSON array string.</returns>
    private static string PrettyArray(List<StashValue> arr, int indent, int indentWidth)
    {
        if (arr.Count == 0)
        {
            return "[]";
        }

        var sb = new StringBuilder("[\n");
        string innerIndent = new string(' ', (indent + 1) * indentWidth);
        string closingIndent = new string(' ', indent * indentWidth);
        for (int i = 0; i < arr.Count; i++)
        {
            sb.Append(innerIndent);
            sb.Append(PrettyValue(arr[i].ToObject(), indent + 1, indentWidth));
            if (i < arr.Count - 1)
            {
                sb.Append(',');
            }

            sb.Append('\n');
        }
        sb.Append(closingIndent);
        sb.Append(']');
        return sb.ToString();
    }

    /// <summary>Pretty-prints a <see cref="StashDictionary"/> as a JSON object with indentation.</summary>
    /// <param name="dict">The dictionary to pretty-print.</param>
    /// <param name="indent">The current indentation level.</param>
    /// <returns>A pretty-printed JSON object string.</returns>
    private static string PrettyDict(StashDictionary dict, int indent, int indentWidth)
    {
        var keys = dict.RawKeys();
        if (keys.Count == 0)
        {
            return "{}";
        }

        var sb = new StringBuilder("{\n");
        string innerIndent = new string(' ', (indent + 1) * indentWidth);
        string closingIndent = new string(' ', indent * indentWidth);
        bool first = true;
        foreach (var key in keys)
        {
            if (!first)
            {
                sb.Append(",\n");
            }

            first = false;
            string keyStr = StringifyKey(key);
            object? val = dict.Get(key).ToObject();
            sb.Append(innerIndent);
            sb.Append(keyStr);
            sb.Append(": ");
            sb.Append(PrettyValue(val, indent + 1, indentWidth));
        }
        sb.Append('\n');
        sb.Append(closingIndent);
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>Pretty-prints a <see cref="StashInstance"/> as a JSON object with indentation.</summary>
    /// <param name="inst">The struct instance to pretty-print.</param>
    /// <param name="indent">The current indentation level.</param>
    /// <returns>A pretty-printed JSON object string.</returns>
    private static string PrettyInstance(StashInstance inst, int indent, int indentWidth)
    {
        var fields = inst.GetFields();
        if (fields.Count == 0)
        {
            return "{}";
        }

        var sb = new StringBuilder("{\n");
        string innerIndent = new string(' ', (indent + 1) * indentWidth);
        string closingIndent = new string(' ', indent * indentWidth);
        bool first = true;
        foreach (var kvp in fields)
        {
            if (!first)
            {
                sb.Append(",\n");
            }

            first = false;
            sb.Append(innerIndent);
            sb.Append(JsonSerializer.Serialize(kvp.Key, StashJsonContext.Default.String));
            sb.Append(": ");
            sb.Append(PrettyValue(kvp.Value.ToObject(), indent + 1, indentWidth));
        }
        sb.Append('\n');
        sb.Append(closingIndent);
        sb.Append('}');
        return sb.ToString();
    }

    // ── Config namespace integration ──────────────────────────────────────────

    /// <summary>Parses a JSON string to a Stash runtime value. Called by config.parse/config.read.</summary>
    internal static object? ParseJson(string text, string callerName)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return ConvertElement(doc.RootElement);
        }
        catch (JsonException e)
        {
            throw new ParseError($"{callerName}: invalid JSON — " + e.Message);
        }
    }

    /// <summary>Serializes a Stash value to a pretty-printed JSON string with 2-space indentation. Called by config.stringify/config.write.</summary>
    internal static string StringifyJson(object? data)
    {
        return PrettyValue(data, 0, 2);
    }
}
