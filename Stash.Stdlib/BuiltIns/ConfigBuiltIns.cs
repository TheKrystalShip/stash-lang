namespace Stash.Stdlib.BuiltIns;

using System;
using System.IO;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Abstractions;
using Stash.Runtime.Errors;

/// <summary>
/// Registers the <c>config</c> namespace providing unified configuration file reading and writing
/// across formats (read, write, parse, stringify). Supports JSON, INI, YAML, and TOML formats.
/// </summary>
[StashNamespace]
public static partial class ConfigBuiltIns
{
    /// <summary>Reads and parses a config file. Format is auto-detected from extension if omitted.</summary>
    /// <param name="path">The file path</param>
    /// <param name="format">Optional format: 'json', 'ini', 'yaml', 'toml', 'csv', 'xml'</param>
    /// <exception cref="IOError">if the file cannot be read</exception>
    /// <exception cref="ParseError">if the file content is not valid for the detected format</exception>
    /// <exception cref="ValueError">if the file extension is not recognised and no format is given, or the format name is unknown</exception>
    /// <returns>Parsed dictionary</returns>
    [StashFn(ReturnType = "dict")]
    private static StashValue Read(string path, string? format = null)
    {
        var resolvedFormat = format ?? DetectFormat(path);

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (IOException e)
        {
            throw new IOError("config.read: " + e.Message);
        }

        return StashValue.FromObject(ParseByFormat(text, resolvedFormat, "config.read"));
    }

    /// <summary>Serializes data and writes it to a config file. Format is auto-detected from extension if omitted.</summary>
    /// <param name="path">The file path</param>
    /// <param name="data">The data to write</param>
    /// <param name="format">Optional format: 'json', 'ini', 'yaml', 'toml', 'csv', 'xml'</param>
    /// <exception cref="IOError">if the file cannot be written</exception>
    /// <exception cref="TypeError">if the data type is incompatible with the format (INI and TOML require a dict; XML requires an XmlNode)</exception>
    /// <exception cref="ValueError">if the file extension is not recognised and no format is given, or the format name is unknown</exception>
    [StashFn]
    private static void Write(string path, StashValue data, string? format = null)
    {
        var resolvedFormat = format ?? DetectFormat(path);
        var text = StringifyByFormat(data.ToObject(), resolvedFormat, "config.write");

        try
        {
            File.WriteAllText(path, text);
        }
        catch (IOException e)
        {
            throw new IOError("config.write: " + e.Message);
        }
    }

    /// <summary>Parses a config string in the given format.</summary>
    /// <param name="text">The config text</param>
    /// <param name="format">The format: 'json', 'ini', 'yaml', 'toml', 'csv', 'xml'</param>
    /// <exception cref="ParseError">if the text is not valid for the specified format</exception>
    /// <exception cref="ValueError">if the format name is unknown</exception>
    /// <returns>Parsed dictionary</returns>
    [StashFn(ReturnType = "dict")]
    private static StashValue Parse(string text, string format)
    {
        return StashValue.FromObject(ParseByFormat(text, format, "config.parse"));
    }

    /// <summary>Serializes a value to the given config format.</summary>
    /// <param name="data">The value to serialize</param>
    /// <param name="format">The format: 'json', 'ini', 'yaml', 'toml', 'csv', 'xml'</param>
    /// <exception cref="TypeError">if the data type is incompatible with the format (INI and TOML require a dict; XML requires an XmlNode)</exception>
    /// <exception cref="ValueError">if the format name is unknown</exception>
    /// <returns>Config string</returns>
    [StashFn]
    private static string Stringify(StashValue data, string format)
    {
        return StringifyByFormat(data.ToObject(), format, "config.stringify");
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
            _ => throw new ValueError($"config: unsupported file extension '{ext}'. Use the format parameter to specify the format explicitly.")
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
            _ => throw new ValueError($"{callerName}: unknown format '{format}'. Supported formats: 'json', 'ini', 'yaml', 'toml', 'csv', 'xml'.")
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
                : throw new TypeError($"{callerName}: INI format requires a dict value."),
            "yaml" => YamlBuiltIns.StringifyYaml(data),
            "toml" => data is StashDictionary tomlDict
                ? TomlBuiltIns.StringifyToml(tomlDict)
                : throw new TypeError($"{callerName}: TOML format requires a dict value."),
            "csv" => CsvBuiltIns.StringifyCsvDefault(data, callerName),
            "xml" => data is StashInstance xmlNode
                ? XmlBuiltIns.StringifyXml(xmlNode, callerName)
                : throw new TypeError($"{callerName}: XML format requires an XmlNode value."),
            _ => throw new ValueError($"{callerName}: unknown format '{format}'. Supported formats: 'json', 'ini', 'yaml', 'toml', 'csv', 'xml'.")
        };
    }

}
