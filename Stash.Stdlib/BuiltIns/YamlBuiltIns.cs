namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using SharpYaml;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

/// <summary>
/// Registers the <c>yaml</c> namespace built-in functions for YAML serialization and deserialization.
/// </summary>
/// <remarks>
/// <para>
/// Provides functions for working with YAML data: <c>yaml.parse</c>, <c>yaml.stringify</c>,
/// and <c>yaml.valid</c>.
/// </para>
/// <para>
/// YAML mappings are represented as <see cref="StashDictionary"/> instances and YAML sequences
/// as <c>List&lt;object?&gt;</c> values in the Stash runtime.
/// </para>
/// </remarks>
public static class YamlBuiltIns
{
    /// <summary>
    /// Registers all <c>yaml</c> namespace functions into the global environment.
    /// </summary>
    /// <param name="globals">The global <see cref="Stash.Interpreting.Environment"/> to register functions in.</param>
    public static NamespaceDefinition Define()
    {
        var ns = new NamespaceBuilder("yaml");

        // yaml.parse(string) — Parses a YAML string into a Stash value (dict, array, string, number, bool, or null).
        ns.Function("parse", [Param("text", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var s = SvArgs.String(args, 0, "yaml.parse");

            try
            {
                var opts = new YamlSerializerOptions();
                object? raw = YamlSerializer.Deserialize<object>(s, opts);
                return StashValue.FromObject(ConvertFromYaml(raw));
            }
            catch (Exception e) when (e is not RuntimeError)
            {
                throw new RuntimeError("yaml.parse: invalid YAML — " + e.Message, errorType: "ParseError");
            }
        },
            returnType: "any",
            documentation: "Parses a YAML string into a Stash value (dict, array, or scalar).\n@param text The YAML string\n@return Parsed value");

        // yaml.stringify(value) — Serializes a Stash value to a YAML string.
        ns.Function("stringify", [Param("value", "any")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            try
            {
                object? native = ConvertToYaml(args[0].ToObject());
                var opts = new YamlSerializerOptions();
                return StashValue.FromObj(YamlSerializer.Serialize(native, native?.GetType() ?? typeof(object), opts));
            }
            catch (SharpYaml.YamlException e)
            {
                throw new RuntimeError("yaml.stringify: " + e.Message, errorType: "TypeError");
            }
        },
            returnType: "string",
            documentation: "Serializes a Stash value to a YAML string.\n@param value The value to serialize\n@return YAML string");

        // yaml.valid(string) — Returns true if the given string is valid YAML, false otherwise.
        ns.Function("valid", [Param("text", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var s = SvArgs.String(args, 0, "yaml.valid");

            try
            {
                var opts = new YamlSerializerOptions();
                YamlSerializer.Deserialize<object>(s, opts);
                return StashValue.True;
            }
            catch (SharpYaml.YamlException)
            {
                return StashValue.False;
            }
        },
            returnType: "bool",
            documentation: "Returns true if the string is valid YAML, false otherwise.\n@param text The YAML string to validate\n@return true if valid YAML");

        return ns.Build();
    }

    /// <summary>Parses a YAML string into a Stash runtime value.</summary>
    /// <param name="text">The YAML text to parse.</param>
    /// <returns>A parsed Stash value.</returns>
    internal static object? ParseYaml(string text)
    {
        var opts = new YamlSerializerOptions();
        object? raw = YamlSerializer.Deserialize<object>(text, opts);
        return ConvertFromYaml(raw);
    }

    /// <summary>Serializes a Stash value to a YAML string.</summary>
    /// <param name="value">The Stash value to serialize.</param>
    /// <returns>A YAML string.</returns>
    internal static string StringifyYaml(object? value)
    {
        object? native = ConvertToYaml(value);
        var opts = new YamlSerializerOptions();
        return YamlSerializer.Serialize(native, native?.GetType() ?? typeof(object), opts);
    }

    /// <summary>Recursively converts a raw SharpYaml deserialized value to a Stash runtime value.</summary>
    /// <param name="raw">The deserialized value returned by <see cref="YamlSerializer"/>.</param>
    /// <returns>
    /// A Stash runtime value: <c>string</c>, <c>long</c>, <c>double</c>, <c>bool</c>, <c>null</c>,
    /// <c>List&lt;object?&gt;</c>, or <see cref="StashDictionary"/>.
    /// </returns>
    private static object? ConvertFromYaml(object? raw)
    {
        return raw switch
        {
            null => null,
            bool b => b,
            int i => (long)i,
            long l => l,
            float f => (double)f,
            double d => d,
            string s => s,
            Dictionary<string, object> map => ConvertMapping(map),
            List<object> list => ConvertSequence(list),
            _ => raw.ToString()
        };
    }

    /// <summary>Converts a SharpYaml mapping (<c>Dictionary&lt;string, object&gt;</c>) to a <see cref="StashDictionary"/>.</summary>
    /// <param name="map">The raw mapping dictionary from SharpYaml deserialization.</param>
    /// <returns>A <see cref="StashDictionary"/> with string keys and recursively converted values.</returns>
    private static StashDictionary ConvertMapping(Dictionary<string, object> map)
    {
        var dict = new StashDictionary();
        foreach (var kvp in map)
        {
            dict.Set(kvp.Key, StashValue.FromObject(ConvertFromYaml(kvp.Value)));
        }

        return dict;
    }

    /// <summary>Converts a SharpYaml sequence (<c>List&lt;object&gt;</c>) to a <c>List&lt;StashValue&gt;</c>.</summary>
    /// <param name="list">The raw sequence list from SharpYaml deserialization.</param>
    /// <returns>A list of recursively converted Stash values.</returns>
    private static List<StashValue> ConvertSequence(List<object> list)
    {
        var result = new List<StashValue>(list.Count);
        foreach (object item in list)
        {
            result.Add(StashValue.FromObject(ConvertFromYaml(item)));
        }

        return result;
    }

    /// <summary>Recursively converts a Stash runtime value to a plain .NET value suitable for YAML serialization.</summary>
    /// <param name="value">The Stash value to convert.</param>
    /// <returns>
    /// A plain .NET value: <c>Dictionary&lt;string, object?&gt;</c>, <c>List&lt;object?&gt;</c>,
    /// <c>long</c>, <c>double</c>, <c>string</c>, <c>bool</c>, or <c>null</c>.
    /// </returns>
    private static object? ConvertToYaml(object? value)
    {
        return value switch
        {
            null => null,
            bool b => b,
            long l => l,
            double d => d,
            string s => s,
            List<StashValue> list => ConvertListToYaml(list),
            StashDictionary dict => ConvertDictToYaml(dict),
            StashInstance inst => ConvertInstanceToYaml(inst),
            _ => throw new RuntimeError($"yaml.stringify: cannot serialize value of type {value.GetType().Name}.", errorType: "TypeError")
        };
    }

    /// <summary>Converts a Stash array to a <c>List&lt;object?&gt;</c> with recursively converted elements.</summary>
    /// <param name="list">The Stash array to convert.</param>
    /// <returns>A list of plain .NET values.</returns>
    private static List<object?> ConvertListToYaml(List<StashValue> list)
    {
        var result = new List<object?>(list.Count);
        foreach (StashValue item in list)
        {
            result.Add(ConvertToYaml(item.ToObject()));
        }

        return result;
    }

    /// <summary>Converts a <see cref="StashDictionary"/> to a <c>Dictionary&lt;string, object?&gt;</c> with recursively converted values.</summary>
    /// <param name="dict">The Stash dictionary to convert.</param>
    /// <returns>A plain .NET dictionary with string keys and recursively converted values.</returns>
    private static Dictionary<string, object?> ConvertDictToYaml(StashDictionary dict)
    {
        var result = new Dictionary<string, object?>();
        foreach (var entry in dict.RawEntries())
        {
            string keyStr = entry.Key?.ToString() ?? "null";
            result[keyStr] = ConvertToYaml(entry.Value.ToObject());
        }

        return result;
    }

    /// <summary>Converts a <see cref="StashInstance"/> to a <c>Dictionary&lt;string, object?&gt;</c> using its fields.</summary>
    /// <param name="inst">The struct instance to convert.</param>
    /// <returns>A plain .NET dictionary of field names to recursively converted values.</returns>
    private static Dictionary<string, object?> ConvertInstanceToYaml(StashInstance inst)
    {
        var result = new Dictionary<string, object?>();
        foreach (var kvp in inst.GetFields())
        {
            result[kvp.Key] = ConvertToYaml(kvp.Value.ToObject());
        }

        return result;
    }
}
