namespace Stash.Stdlib.BuiltIns;

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

/// <summary>
/// Registers the <c>json</c> namespace built-in functions for JSON serialization and deserialization.
/// </summary>
public static class JsonBuiltIns
{
    public static NamespaceDefinition Define()
    {
        var ns = new NamespaceBuilder("json");

        // json.parse(string) — Parses a JSON string into a Stash value (dict, array, string, number, bool, or null).
        ns.Function("parse", [Param("str", "string")], (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'json.parse' must be a string.");
            }

            try
            {
                using var doc = JsonDocument.Parse(s);
                return ConvertElement(doc.RootElement);
            }
            catch (JsonException e)
            {
                throw new RuntimeError("json.parse: invalid JSON — " + e.Message);
            }
        },
            documentation: "Parses a JSON string into a Stash value (dict, array, string, number, bool, or null).\n@param str The JSON string to parse\n@return The parsed value");

        // json.stringify(value) — Serializes a Stash value to a compact JSON string.
        ns.Function("stringify", [Param("value")], (_, args) =>
        {
            return StringifyValue(args[0]);
        },
            returnType: "string",
            documentation: "Converts a Stash value to a compact JSON string.\n@param value The value to serialize\n@return The JSON string representation");

        // json.pretty(value) — Serializes a Stash value to an indented, human-readable JSON string.
        ns.Function("pretty", [Param("value")], (_, args) =>
        {
            return PrettyValue(args[0], 0);
        },
            returnType: "string",
            documentation: "Converts a Stash value to a formatted JSON string with indentation.\n@param value The value to serialize\n@return The pretty-printed JSON string");

        // json.valid(string) — Returns true if the given string is valid JSON, false otherwise.
        ns.Function("valid", [Param("text", "string")], (_, args) =>
        {
            if (args[0] is not string s)
            {
                throw new RuntimeError("First argument to 'json.valid' must be a string.");
            }

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(s);
                return true;
            }
            catch (System.Text.Json.JsonException)
            {
                return false;
            }
        },
            returnType: "bool",
            documentation: "Checks whether a string is valid JSON without parsing it into a value.\n@param text The string to validate\n@return true if the string is valid JSON");

        return ns.Build();
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

    /// <summary>Converts a JSON array element to a <c>List&lt;object?&gt;</c>.</summary>
    /// <param name="element">The JSON array element to convert.</param>
    /// <returns>A list of converted Stash values.</returns>
    private static List<object?> ConvertArray(JsonElement element)
    {
        var list = new List<object?>();
        foreach (var item in element.EnumerateArray())
        {
            list.Add(ConvertElement(item));
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
            dict.Set(prop.Name, ConvertElement(prop.Value));
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
            string s => JsonSerializer.Serialize(s, StashJsonContext.Default.String),
            List<object?> arr => StringifyArray(arr),
            StashDictionary dict => StringifyDict(dict),
            StashInstance inst => StringifyInstance(inst),
            _ => throw new RuntimeError($"json.stringify: cannot serialize value of type {value.GetType().Name}.")
        };
    }

    /// <summary>Serializes a Stash array to a compact JSON array string.</summary>
    /// <param name="arr">The array to serialize.</param>
    /// <returns>A compact JSON array string.</returns>
    private static string StringifyArray(List<object?> arr)
    {
        var sb = new StringBuilder("[");
        for (int i = 0; i < arr.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            sb.Append(StringifyValue(arr[i]));
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
        var keys = dict.Keys();
        for (int i = 0; i < keys.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            string keyStr = StringifyKey(keys[i]!);
            object? val = dict.Get(keys[i]!);
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
            sb.Append(StringifyValue(kvp.Value));
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
            _ => throw new RuntimeError("json.stringify: dict key must be a string, number, or bool.")
        };
    }

    /// <summary>Converts a Stash runtime value to a pretty-printed JSON string with indentation.</summary>
    /// <param name="value">The Stash value to serialize.</param>
    /// <param name="indent">The current indentation level.</param>
    /// <returns>A pretty-printed JSON string.</returns>
    private static string PrettyValue(object? value, int indent)
    {
        return value switch
        {
            null => "null",
            bool b => b ? "true" : "false",
            long l => l.ToString(CultureInfo.InvariantCulture),
            double d => d.ToString("G", CultureInfo.InvariantCulture),
            string s => JsonSerializer.Serialize(s, StashJsonContext.Default.String),
            List<object?> arr => PrettyArray(arr, indent),
            StashDictionary dict => PrettyDict(dict, indent),
            StashInstance inst => PrettyInstance(inst, indent),
            _ => throw new RuntimeError($"json.pretty: cannot serialize value of type {value.GetType().Name}.")
        };
    }

    /// <summary>Pretty-prints a Stash array as a JSON array with indentation.</summary>
    /// <param name="arr">The array to pretty-print.</param>
    /// <param name="indent">The current indentation level.</param>
    /// <returns>A pretty-printed JSON array string.</returns>
    private static string PrettyArray(List<object?> arr, int indent)
    {
        if (arr.Count == 0)
        {
            return "[]";
        }

        var sb = new StringBuilder("[\n");
        string innerIndent = new string(' ', (indent + 1) * 2);
        string closingIndent = new string(' ', indent * 2);
        for (int i = 0; i < arr.Count; i++)
        {
            sb.Append(innerIndent);
            sb.Append(PrettyValue(arr[i], indent + 1));
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
    private static string PrettyDict(StashDictionary dict, int indent)
    {
        var keys = dict.Keys();
        if (keys.Count == 0)
        {
            return "{}";
        }

        var sb = new StringBuilder("{\n");
        string innerIndent = new string(' ', (indent + 1) * 2);
        string closingIndent = new string(' ', indent * 2);
        bool first = true;
        foreach (var key in keys)
        {
            if (!first)
            {
                sb.Append(",\n");
            }

            first = false;
            string keyStr = StringifyKey(key!);
            object? val = dict.Get(key!);
            sb.Append(innerIndent);
            sb.Append(keyStr);
            sb.Append(": ");
            sb.Append(PrettyValue(val, indent + 1));
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
    private static string PrettyInstance(StashInstance inst, int indent)
    {
        var fields = inst.GetFields();
        if (fields.Count == 0)
        {
            return "{}";
        }

        var sb = new StringBuilder("{\n");
        string innerIndent = new string(' ', (indent + 1) * 2);
        string closingIndent = new string(' ', indent * 2);
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
            sb.Append(PrettyValue(kvp.Value, indent + 1));
        }
        sb.Append('\n');
        sb.Append(closingIndent);
        sb.Append('}');
        return sb.ToString();
    }
}
