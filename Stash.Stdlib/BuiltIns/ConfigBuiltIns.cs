namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Models;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

/// <summary>
/// Registers the <c>config</c> namespace providing unified configuration file reading and writing
/// across formats (read, write, parse, stringify). Supports JSON, INI, YAML, and TOML formats.
/// </summary>
public static class ConfigBuiltIns
{
    /// <summary>
    /// Registers the <c>config</c> namespace and all its functions into the global environment.
    /// </summary>
    /// <param name="globals">The global environment to register into.</param>
    public static NamespaceDefinition Define()
    {
        var ns = new NamespaceBuilder("config");

        // config.read(path, format?) — Reads a config file from disk and parses it. Format is auto-detected from extension if omitted. Supports "json" and "ini".
        ns.Function("read", [Param("path", "string"), Param("format", "string")], (_, args) =>
        {
            Args.Count(args, 1, 2, "config.read");
            var path = Args.String(args, 0, "config.read");

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
        },
            returnType: "dict",
            isVariadic: true,
            documentation: "Reads and parses a config file. Format is auto-detected from extension if omitted.\n@param path The file path\n@param format Optional format: 'json', 'ini', 'yaml', 'toml'\n@return Parsed dictionary");

        // config.write(path, data, format?) — Serializes data and writes it to a config file. Format is auto-detected from extension if omitted. Supports "json" and "ini".
        ns.Function("write", [Param("path", "string"), Param("data", "any"), Param("format", "string")], (_, args) =>
        {
            Args.Count(args, 2, 3, "config.write");
            var path = Args.String(args, 0, "config.write");

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
        },
            returnType: "void",
            isVariadic: true,
            documentation: "Serializes data and writes it to a config file. Format is auto-detected from extension if omitted.\n@param path The file path\n@param data The data to write\n@param format Optional format: 'json', 'ini', 'yaml', 'toml'");

        // config.parse(text, format) — Parses a config string in the given format ("json" or "ini"). Returns a dict.
        ns.Function("parse", [Param("text", "string"), Param("format", "string")], (_, args) =>
        {
            var text = Args.String(args, 0, "config.parse");
            var format = Args.String(args, 1, "config.parse");

            return ParseByFormat(text, format, "config.parse");
        },
            returnType: "dict",
            documentation: "Parses a config string in the given format.\n@param text The config text\n@param format The format: 'json', 'ini', 'yaml', 'toml'\n@return Parsed dictionary");

        // config.stringify(data, format) — Serializes a Stash value to the given config format string ("json" or "ini"). Returns a string.
        ns.Function("stringify", [Param("data", "any"), Param("format", "string")], (_, args) =>
        {
            var format = Args.String(args, 1, "config.stringify");

            return StringifyByFormat(args[0], format, "config.stringify");
        },
            returnType: "string",
            documentation: "Serializes a value to the given config format.\n@param data The value to serialize\n@param format The format: 'json', 'ini', 'yaml', 'toml'\n@return Config string");

        return ns.Build();
    }

    /// <summary>Detects the configuration format from a file extension (.json, .ini, .cfg, .conf, .properties, .yaml, .yml, .toml).</summary>
    /// <param name="path">The file path whose extension is inspected.</param>
    /// <returns><c>"json"</c>, <c>"ini"</c>, <c>"yaml"</c>, or <c>"toml"</c> based on the extension.</returns>
    private static string DetectFormat(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".json" => "json",
            ".ini" or ".cfg" or ".conf" or ".properties" => "ini",
            ".yaml" or ".yml" => "yaml",
            ".toml" => "toml",
            _ => throw new RuntimeError($"config: unsupported file extension '{ext}'. Use the format parameter to specify the format explicitly.")
        };
    }

    /// <summary>Parses configuration text using the specified format.</summary>
    /// <param name="text">The configuration text to parse.</param>
    /// <param name="format">The format name (<c>"json"</c>, <c>"ini"</c>, <c>"yaml"</c>, or <c>"toml"</c>).</param>
    /// <param name="callerName">The calling function name, used in error messages.</param>
    /// <returns>A parsed Stash value.</returns>
    private static object? ParseByFormat(string text, string format, string callerName)
    {
        return format.ToLowerInvariant() switch
        {
            "json" => ParseJson(text, callerName),
            "ini" => IniBuiltIns.ParseIni(text),
            "yaml" => YamlBuiltIns.ParseYaml(text),
            "toml" => TomlBuiltIns.ParseToml(text),
            _ => throw new RuntimeError($"{callerName}: unknown format '{format}'. Supported formats: 'json', 'ini', 'yaml', 'toml'.")
        };
    }

    /// <summary>Serializes a Stash value to the specified configuration format.</summary>
    /// <param name="data">The Stash value to serialize.</param>
    /// <param name="format">The format name (<c>"json"</c>, <c>"ini"</c>, <c>"yaml"</c>, or <c>"toml"</c>).</param>
    /// <param name="callerName">The calling function name, used in error messages.</param>
    /// <returns>A serialized configuration string.</returns>
    private static string StringifyByFormat(object? data, string format, string callerName)
    {
        return format.ToLowerInvariant() switch
        {
            "json" => PrettyValue(data, 0),
            "ini" => data is StashDictionary iniDict
                ? IniBuiltIns.StringifyIni(iniDict)
                : throw new RuntimeError($"{callerName}: INI format requires a dict value."),
            "yaml" => YamlBuiltIns.StringifyYaml(data),
            "toml" => data is StashDictionary tomlDict
                ? TomlBuiltIns.StringifyToml(tomlDict)
                : throw new RuntimeError($"{callerName}: TOML format requires a dict value."),
            _ => throw new RuntimeError($"{callerName}: unknown format '{format}'. Supported formats: 'json', 'ini', 'yaml', 'toml'.")
        };
    }

    /// <summary>Parses a JSON string to Stash runtime values.</summary>
    /// <param name="text">The JSON string to parse.</param>
    /// <param name="callerName">The calling function name, used in error messages.</param>
    /// <returns>A parsed Stash value.</returns>
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

    /// <summary>Recursively converts a <see cref="System.Text.Json.JsonElement"/> to a Stash runtime value.</summary>
    /// <param name="element">The JSON element to convert.</param>
    /// <returns>A Stash runtime value.</returns>
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

    /// <summary>Converts a JSON array element to a <c>List&lt;object?&gt;</c>.</summary>
    /// <param name="element">The JSON array element to convert.</param>
    /// <returns>A list of converted Stash values.</returns>
    private static List<object?> ConvertJsonArray(JsonElement element)
    {
        var list = new List<object?>();
        foreach (var item in element.EnumerateArray())
        {
            list.Add(ConvertJsonElement(item));
        }

        return list;
    }

    /// <summary>Converts a JSON object element to a <see cref="StashDictionary"/>.</summary>
    /// <param name="element">The JSON object element to convert.</param>
    /// <returns>A <see cref="StashDictionary"/> with string keys and converted values.</returns>
    private static StashDictionary ConvertJsonObject(JsonElement element)
    {
        var dict = new StashDictionary();
        foreach (var prop in element.EnumerateObject())
        {
            dict.Set(prop.Name, ConvertJsonElement(prop.Value));
        }

        return dict;
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
            _ => throw new RuntimeError($"config.stringify: cannot serialize value of type {value.GetType().Name}.")
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

    /// <summary>Converts a dictionary key to a JSON-quoted string.</summary>
    /// <param name="key">The dictionary key to convert.</param>
    /// <returns>A JSON-quoted string representation of the key.</returns>
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
