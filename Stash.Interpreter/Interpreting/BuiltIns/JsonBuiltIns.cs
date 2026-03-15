namespace Stash.Interpreting.BuiltIns;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Stash.Interpreting.Types;

public static class JsonBuiltIns
{
    public static void Register(Stash.Interpreting.Environment globals)
    {
        var json = new StashNamespace("json");

        json.Define("parse", new BuiltInFunction("json.parse", 1, (_, args) =>
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
        }));

        json.Define("stringify", new BuiltInFunction("json.stringify", 1, (_, args) =>
        {
            return StringifyValue(args[0]);
        }));

        json.Define("pretty", new BuiltInFunction("json.pretty", 1, (_, args) =>
        {
            return PrettyValue(args[0], 0);
        }));

        globals.Define("json", json);
    }

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

    private static List<object?> ConvertArray(JsonElement element)
    {
        var list = new List<object?>();
        foreach (var item in element.EnumerateArray())
        {
            list.Add(ConvertElement(item));
        }

        return list;
    }

    private static StashDictionary ConvertObject(JsonElement element)
    {
        var dict = new StashDictionary();
        foreach (var prop in element.EnumerateObject())
        {
            dict.Set(prop.Name, ConvertElement(prop.Value));
        }

        return dict;
    }

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
