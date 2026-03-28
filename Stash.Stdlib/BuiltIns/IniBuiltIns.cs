namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

/// <summary>
/// Registers the <c>ini</c> namespace built-in functions for INI format parsing and serialization.
/// </summary>
public static class IniBuiltIns
{
    public static NamespaceDefinition Define()
    {
        var ns = new NamespaceBuilder("ini");

        // ini.parse(string) — Parses an INI-format string into a dict. Section headers become nested dicts.
        ns.Function("parse", [Param("text", "string")], (_, args) =>
        {
            var s = Args.String(args, 0, "ini.parse");

            try
            {
                return ParseIni(s);
            }
            catch (Exception e) when (e is not RuntimeError)
            {
                throw new RuntimeError("ini.parse: failed to parse INI — " + e.Message);
            }
        },
            returnType: "dict",
            documentation: "Parses INI-formatted text into a dictionary. Sections become nested dictionaries.\n@param text The INI text to parse\n@return A dictionary representing the INI structure");

        // ini.stringify(dict) — Serializes a dict to an INI-format string. Nested dicts become section headers.
        ns.Function("stringify", [Param("data", "dict")], (_, args) =>
        {
            var dict = Args.Dict(args, 0, "ini.stringify");

            return StringifyIni(dict);
        },
            returnType: "string",
            documentation: "Converts a dictionary to INI-formatted text.\n@param data The dictionary to serialize\n@return The INI text representation");

        return ns.Build();
    }

    /// <summary>Parses an INI-format string into a <see cref="StashDictionary"/>. Section headers create nested dictionaries.</summary>
    /// <param name="text">The INI-format string to parse.</param>
    /// <returns>A <see cref="StashDictionary"/> representing the parsed INI structure.</returns>
    internal static StashDictionary ParseIni(string text)
    {
        var root = new StashDictionary();
        StashDictionary? currentSection = null;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();

            // Skip empty lines and comments
            if (line.Length == 0 || line[0] == ';' || line[0] == '#')
            {
                continue;
            }

            // Section header
            if (line[0] == '[')
            {
                var close = line.IndexOf(']');
                if (close < 0)
                {
                    continue; // malformed section header — skip
                }

                var sectionName = line.Substring(1, close - 1).Trim();
                if (sectionName.Length == 0)
                {
                    continue;
                }

                var sectionDict = new StashDictionary();
                root.Set(sectionName, sectionDict);
                currentSection = sectionDict;
                continue;
            }

            // Key-value pair
            var eqIndex = line.IndexOf('=');
            if (eqIndex < 0)
            {
                continue; // not a valid key=value line — skip
            }

            var key = line.Substring(0, eqIndex).Trim();
            var rawValue = line.Substring(eqIndex + 1).Trim();

            if (key.Length == 0)
            {
                continue;
            }

            var value = CoerceValue(rawValue);
            var target = currentSection ?? root;
            target.Set(key, value);
        }

        return root;
    }

    /// <summary>Coerces a raw INI value string to the appropriate Stash type (long, double, bool, or string).</summary>
    /// <param name="raw">The raw string value from the INI file.</param>
    /// <returns>A <c>long</c>, <c>double</c>, <c>bool</c>, or <c>string</c> value.</returns>
    private static object? CoerceValue(string raw)
    {
        // Strip surrounding double quotes
        if (raw.Length >= 2 && raw[0] == '"' && raw[raw.Length - 1] == '"')
        {
            return raw.Substring(1, raw.Length - 2);
        }

        // Try long
        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
        {
            return l;
        }

        // Try double
        if (double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out double d))
        {
            return d;
        }

        // Try bool
        if (raw.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (raw.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return raw;
    }

    /// <summary>Serializes a <see cref="StashDictionary"/> to an INI-format string. Nested dictionaries become sections.</summary>
    /// <param name="dict">The dictionary to serialize.</param>
    /// <returns>An INI-format string.</returns>
    internal static string StringifyIni(StashDictionary dict)
    {
        var sb = new StringBuilder();

        // Collect global (non-section) keys and section keys separately
        var globals = new List<KeyValuePair<object, object?>>();
        var sections = new List<KeyValuePair<object, object?>>();

        foreach (var kvp in dict.RawEntries())
        {
            if (kvp.Value is StashDictionary)
            {
                sections.Add(kvp);
            }
            else
            {
                globals.Add(kvp);
            }
        }

        // Write global keys first
        foreach (var kvp in globals)
        {
            var key = kvp.Key.ToString()!;
            var val = FormatValue(kvp.Value);
            sb.AppendLine($"{key} = {val}");
        }

        // Write section blocks
        foreach (var kvp in sections)
        {
            var sectionName = kvp.Key.ToString()!;
            var sectionDict = (StashDictionary)kvp.Value!;

            if (sb.Length > 0)
            {
                sb.AppendLine();
            }

            sb.AppendLine($"[{sectionName}]");

            foreach (var entry in sectionDict.RawEntries())
            {
                // Skip nested dicts — INI is flat
                if (entry.Value is StashDictionary)
                {
                    continue;
                }

                var key = entry.Key.ToString()!;
                var val = FormatValue(entry.Value);
                sb.AppendLine($"{key} = {val}");
            }
        }

        // Trim trailing newline
        return sb.ToString().TrimEnd('\r', '\n');
    }

    /// <summary>Formats a single value for INI output, quoting strings with leading/trailing spaces.</summary>
    /// <param name="value">The value to format.</param>
    /// <returns>A string representation suitable for INI output.</returns>
    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => "",
            bool b => b ? "true" : "false",
            long l => l.ToString(CultureInfo.InvariantCulture),
            double d => d.ToString("G", CultureInfo.InvariantCulture),
            string s when s.Length > 0 && (s[0] == ' ' || s[s.Length - 1] == ' ') => $"\"{s}\"",
            string s => s,
            _ => value.ToString() ?? ""
        };
    }
}
