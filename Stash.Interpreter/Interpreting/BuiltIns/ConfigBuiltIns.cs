namespace Stash.Interpreting.BuiltIns;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Stash.Interpreting.Types;

public static class ConfigBuiltIns
{
    public static void Register(Stash.Interpreting.Environment globals)
    {
        var config = new StashNamespace("config");

        // config.read(path) or config.read(path, format)
        config.Define("read", new BuiltInFunction("config.read", -1, (_, args) =>
        {
            if (args.Count < 1 || args.Count > 2)
            {
                throw new RuntimeError("'config.read' expects 1 or 2 arguments.");
            }

            if (args[0] is not string path)
            {
                throw new RuntimeError("First argument to 'config.read' must be a string (path).");
            }

            var format = args.Count == 2
                ? args[1] as string ?? throw new RuntimeError("Second argument to 'config.read' must be a string (format).")
                : DetectFormat(path);

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (IOException e)
            {
                throw new RuntimeError("config.read: " + e.Message);
            }

            return ParseByFormat(text, format, "config.read");
        }));

        // config.write(path, data) or config.write(path, data, format)
        config.Define("write", new BuiltInFunction("config.write", -1, (_, args) =>
        {
            if (args.Count < 2 || args.Count > 3)
            {
                throw new RuntimeError("'config.write' expects 2 or 3 arguments.");
            }

            if (args[0] is not string path)
            {
                throw new RuntimeError("First argument to 'config.write' must be a string (path).");
            }

            var format = args.Count == 3
                ? args[2] as string ?? throw new RuntimeError("Third argument to 'config.write' must be a string (format).")
                : DetectFormat(path);

            var text = StringifyByFormat(args[1], format, "config.write");

            try
            {
                File.WriteAllText(path, text);
            }
            catch (IOException e)
            {
                throw new RuntimeError("config.write: " + e.Message);
            }

            return null;
        }));

        // config.parse(text, format)
        config.Define("parse", new BuiltInFunction("config.parse", 2, (_, args) =>
        {
            if (args[0] is not string text)
            {
                throw new RuntimeError("First argument to 'config.parse' must be a string.");
            }

            if (args[1] is not string format)
            {
                throw new RuntimeError("Second argument to 'config.parse' must be a string (format).");
            }

            return ParseByFormat(text, format, "config.parse");
        }));

        // config.stringify(data, format)
        config.Define("stringify", new BuiltInFunction("config.stringify", 2, (_, args) =>
        {
            if (args[1] is not string format)
            {
                throw new RuntimeError("Second argument to 'config.stringify' must be a string (format).");
            }

            return StringifyByFormat(args[0], format, "config.stringify");
        }));

        globals.Define("config", config);
    }

    private static string DetectFormat(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".json" => "json",
            ".ini" or ".cfg" or ".conf" or ".properties" => "ini",
            _ => throw new RuntimeError($"config: unsupported file extension '{ext}'. Use the format parameter to specify the format explicitly.")
        };
    }

    private static object? ParseByFormat(string text, string format, string callerName)
    {
        return format.ToLowerInvariant() switch
        {
            "json" => ParseJson(text, callerName),
            "ini" => IniBuiltIns.ParseIni(text),
            _ => throw new RuntimeError($"{callerName}: unknown format '{format}'. Supported formats: 'json', 'ini'.")
        };
    }

    private static string StringifyByFormat(object? data, string format, string callerName)
    {
        return format.ToLowerInvariant() switch
        {
            "json" => PrettyValue(data, 0),
            "ini" => data is StashDictionary dict
                ? IniBuiltIns.StringifyIni(dict)
                : throw new RuntimeError($"{callerName}: INI format requires a dict value."),
            _ => throw new RuntimeError($"{callerName}: unknown format '{format}'. Supported formats: 'json', 'ini'.")
        };
    }

    private static object? ParseJson(string text, string callerName)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return ConvertJsonElement(doc.RootElement);
        }
        catch (JsonException e)
        {
            throw new RuntimeError($"{callerName}: invalid JSON — " + e.Message);
        }
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString()!,
            JsonValueKind.Number => element.TryGetInt64(out long l) ? (object?)l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => ConvertJsonArray(element),
            JsonValueKind.Object => ConvertJsonObject(element),
            _ => null
        };
    }

    private static List<object?> ConvertJsonArray(JsonElement element)
    {
        var list = new List<object?>();
        foreach (var item in element.EnumerateArray())
        {
            list.Add(ConvertJsonElement(item));
        }

        return list;
    }

    private static StashDictionary ConvertJsonObject(JsonElement element)
    {
        var dict = new StashDictionary();
        foreach (var prop in element.EnumerateObject())
        {
            dict.Set(prop.Name, ConvertJsonElement(prop.Value));
        }

        return dict;
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
            _ => throw new RuntimeError($"config.stringify: cannot serialize value of type {value.GetType().Name}.")
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
            string keyStr = PrettyKey(key!);
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

    private static string PrettyKey(object key)
    {
        return key switch
        {
            string s => JsonSerializer.Serialize(s, StashJsonContext.Default.String),
            long l => JsonSerializer.Serialize(l.ToString(CultureInfo.InvariantCulture), StashJsonContext.Default.String),
            double d => JsonSerializer.Serialize(d.ToString("G", CultureInfo.InvariantCulture), StashJsonContext.Default.String),
            bool b => JsonSerializer.Serialize(b ? "true" : "false", StashJsonContext.Default.String),
            _ => throw new RuntimeError("config: dict key must be a string, number, or bool.")
        };
    }
}
