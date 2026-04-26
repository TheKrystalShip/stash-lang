namespace Stash.Stdlib.BuiltIns;

using System;
using System.IO;
using Stash.Runtime;
using Stash.Runtime.Types;
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
        ns.Function("read", [Param("path", "string"), Param("format", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 1 || args.Length > 2) throw new RuntimeError("'config.read' expects 1 or 2 arguments.");
            var path = SvArgs.String(args, 0, "config.read");

            var format = args.Length == 2
                ? SvArgs.String(args, 1, "config.read")
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

            return StashValue.FromObject(ParseByFormat(text, format, "config.read"));
        },
            returnType: "dict",
            isVariadic: true,
            documentation: "Reads and parses a config file. Format is auto-detected from extension if omitted.\n@param path The file path\n@param format Optional format: 'json', 'ini', 'yaml', 'toml', 'csv', 'xml'\n@return Parsed dictionary");

        // config.write(path, data, format?) — Serializes data and writes it to a config file. Format is auto-detected from extension if omitted. Supports "json" and "ini".
        ns.Function("write", [Param("path", "string"), Param("data", "any"), Param("format", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            if (args.Length < 2 || args.Length > 3) throw new RuntimeError("'config.write' expects 2 or 3 arguments.");
            var path = SvArgs.String(args, 0, "config.write");

            var format = args.Length == 3
                ? SvArgs.String(args, 2, "config.write")
                : DetectFormat(path);

            var text = StringifyByFormat(args[1].ToObject(), format, "config.write");

            try
            {
                File.WriteAllText(path, text);
            }
            catch (IOException e)
            {
                throw new RuntimeError("config.write: " + e.Message);
            }

            return StashValue.Null;
        },
            returnType: "void",
            isVariadic: true,
            documentation: "Serializes data and writes it to a config file. Format is auto-detected from extension if omitted.\n@param path The file path\n@param data The data to write\n@param format Optional format: 'json', 'ini', 'yaml', 'toml', 'csv', 'xml'");

        // config.parse(text, format) — Parses a config string in the given format ("json" or "ini"). Returns a dict.
        ns.Function("parse", [Param("text", "string"), Param("format", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var text = SvArgs.String(args, 0, "config.parse");
            var format = SvArgs.String(args, 1, "config.parse");

            return StashValue.FromObject(ParseByFormat(text, format, "config.parse"));
        },
            returnType: "dict",
            documentation: "Parses a config string in the given format.\n@param text The config text\n@param format The format: 'json', 'ini', 'yaml', 'toml', 'csv', 'xml'\n@return Parsed dictionary");

        // config.stringify(data, format) — Serializes a Stash value to the given config format string ("json" or "ini"). Returns a string.
        ns.Function("stringify", [Param("data", "any"), Param("format", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var format = SvArgs.String(args, 1, "config.stringify");

            return StashValue.FromObj(StringifyByFormat(args[0].ToObject(), format, "config.stringify"));
        },
            returnType: "string",
            documentation: "Serializes a value to the given config format.\n@param data The value to serialize\n@param format The format: 'json', 'ini', 'yaml', 'toml', 'csv', 'xml'\n@return Config string");

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
            ".csv" => "csv",
            ".xml" => "xml",
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
            "json" => JsonBuiltIns.ParseJson(text, callerName),
            "ini" => IniBuiltIns.ParseIni(text),
            "yaml" => YamlBuiltIns.ParseYaml(text),
            "toml" => TomlBuiltIns.ParseToml(text),
            "csv" => CsvBuiltIns.ParseCsvDefault(text),
            "xml" => XmlBuiltIns.ParseXml(text),
            _ => throw new RuntimeError($"{callerName}: unknown format '{format}'. Supported formats: 'json', 'ini', 'yaml', 'toml', 'csv', 'xml'.")
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
            "json" => JsonBuiltIns.StringifyJson(data),
            "ini" => data is StashDictionary iniDict
                ? IniBuiltIns.StringifyIni(iniDict)
                : throw new RuntimeError($"{callerName}: INI format requires a dict value."),
            "yaml" => YamlBuiltIns.StringifyYaml(data),
            "toml" => data is StashDictionary tomlDict
                ? TomlBuiltIns.StringifyToml(tomlDict)
                : throw new RuntimeError($"{callerName}: TOML format requires a dict value."),
            "csv" => CsvBuiltIns.StringifyCsvDefault(data, callerName),
            "xml" => data is StashInstance xmlNode
                ? XmlBuiltIns.StringifyXml(xmlNode, callerName)
                : throw new RuntimeError($"{callerName}: XML format requires an XmlNode value."),
            _ => throw new RuntimeError($"{callerName}: unknown format '{format}'. Supported formats: 'json', 'ini', 'yaml', 'toml', 'csv', 'xml'.")
        };
    }

}
