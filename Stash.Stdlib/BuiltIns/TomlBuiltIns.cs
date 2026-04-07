namespace Stash.Stdlib.BuiltIns;

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Tomlyn;
using Tomlyn.Model;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;
using System;

/// <summary>
/// Registers the <c>toml</c> namespace built-in functions for TOML serialization and deserialization.
/// </summary>
/// <remarks>
/// <para>
/// Provides functions for working with TOML data: <c>toml.parse</c>, <c>toml.stringify</c>,
/// and <c>toml.valid</c>.
/// </para>
/// <para>
/// TOML tables are represented as <see cref="StashDictionary"/> instances and TOML arrays
/// as <c>List&lt;object?&gt;</c> values in the Stash runtime.
/// </para>
/// </remarks>
[UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
    Justification = "Only deserializes to TomlTable — Tomlyn's own concrete model type; no reflection needed")]
[UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
    Justification = "Only deserializes to TomlTable — Tomlyn's own concrete model type; no reflection needed")]
public static class TomlBuiltIns
{
    /// <summary>
    /// Registers all <c>toml</c> namespace functions into the global environment.
    /// </summary>
    /// <param name="globals">The global <see cref="Stash.Interpreting.Environment"/> to register functions in.</param>
    public static NamespaceDefinition Define()
    {
        var ns = new NamespaceBuilder("toml");

        // toml.parse(string) — Parses a TOML string into a Stash dict.
        ns.Function("parse", [Param("text", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var s = SvArgs.String(args, 0, "toml.parse");

            try
            {
                var table = TomlSerializer.Deserialize<TomlTable>(s);
                if (table is null)
                {
                    return StashValue.FromObj(new StashDictionary());
                }

                return StashValue.FromObj(ConvertTable(table));
            }
            catch (TomlException e)
            {
                throw new RuntimeError("toml.parse: invalid TOML — " + e.Message);
            }
        },
            returnType: "dict",
            documentation: "Parses a TOML string into a Stash dictionary.\n@param text The TOML string\n@return Parsed dictionary");

        // toml.stringify(dict) — Serializes a Stash dict to a TOML string.
        ns.Function("stringify", [Param("data", "dict")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var dict = SvArgs.Dict(args, 0, "toml.stringify");

            try
            {
                var table = ConvertDictToTomlTable(dict);
                return StashValue.FromObj(TomlSerializer.Serialize<TomlTable>(table));
            }
            catch (TomlException e)
            {
                throw new RuntimeError("toml.stringify: " + e.Message);
            }
        },
            returnType: "string",
            documentation: "Serializes a Stash dictionary to a TOML string.\n@param data The dictionary to serialize\n@return TOML string");

        // toml.valid(string) — Returns true if the string is valid TOML, false otherwise.
        ns.Function("valid", [Param("text", "string")], static (IInterpreterContext _, ReadOnlySpan<StashValue> args) =>
        {
            var s = SvArgs.String(args, 0, "toml.valid");

            try
            {
                TomlSerializer.Deserialize<TomlTable>(s);
                return StashValue.True;
            }
            catch (TomlException)
            {
                return StashValue.False;
            }
        },
            returnType: "bool",
            documentation: "Returns true if the string is valid TOML, false otherwise.\n@param text The TOML string to validate\n@return true if valid TOML");

        return ns.Build();
    }

    /// <summary>Parses a TOML string into a <see cref="StashDictionary"/>.</summary>
    /// <param name="text">The TOML text to parse.</param>
    /// <returns>A parsed <see cref="StashDictionary"/>.</returns>
    internal static StashDictionary ParseToml(string text)
    {
        var table = TomlSerializer.Deserialize<TomlTable>(text);
        if (table is null)
        {
            return new StashDictionary();
        }

        return ConvertTable(table);
    }

    /// <summary>Serializes a <see cref="StashDictionary"/> to a TOML string.</summary>
    /// <param name="dict">The dictionary to serialize.</param>
    /// <returns>A TOML string.</returns>
    internal static string StringifyToml(StashDictionary dict)
    {
        var table = ConvertDictToTomlTable(dict);
        return TomlSerializer.Serialize<TomlTable>(table);
    }

    /// <summary>Recursively converts a <see cref="TomlTable"/> to a <see cref="StashDictionary"/>.</summary>
    /// <param name="table">The TOML table to convert.</param>
    /// <returns>A <see cref="StashDictionary"/> with all nested values converted to Stash types.</returns>
    private static StashDictionary ConvertTable(TomlTable table)
    {
        var dict = new StashDictionary();
        foreach (var kvp in table)
        {
            dict.Set(kvp.Key, StashValue.FromObject(ConvertValue(kvp.Value)));
        }

        return dict;
    }

    /// <summary>Recursively converts a Tomlyn value to a Stash runtime value.</summary>
    /// <param name="value">The Tomlyn value to convert.</param>
    /// <returns>A Stash runtime value: <c>string</c>, <c>long</c>, <c>double</c>, <c>bool</c>, <c>null</c>, <c>List&lt;object?&gt;</c>, or <see cref="StashDictionary"/>.</returns>
    private static object? ConvertValue(object? value)
    {
        return value switch
        {
            null => null,
            TomlTable t => ConvertTable(t),
            TomlArray a => ConvertArray(a),
            TomlTableArray ta => ConvertTableArray(ta),
            string s => s,
            long l => l,
            int i => (long)i,
            double d => d,
            float f => (double)f,
            bool b => b,
            TomlDateTime dt => dt.ToString(),
            _ => value.ToString()
        };
    }

    /// <summary>Converts a <see cref="TomlArray"/> to a <c>List&lt;StashValue&gt;</c>.</summary>
    /// <param name="array">The TOML array to convert.</param>
    /// <returns>A list of converted Stash values.</returns>
    private static List<StashValue> ConvertArray(TomlArray array)
    {
        var list = new List<StashValue>();
        foreach (var item in array)
        {
            list.Add(StashValue.FromObject(ConvertValue(item)));
        }

        return list;
    }

    /// <summary>Converts a <see cref="TomlTableArray"/> to a <c>List&lt;StashValue&gt;</c> of <see cref="StashDictionary"/> instances.</summary>
    /// <param name="tableArray">The TOML table array to convert.</param>
    /// <returns>A list of <see cref="StashDictionary"/> values, one per TOML table.</returns>
    private static List<StashValue> ConvertTableArray(TomlTableArray tableArray)
    {
        var list = new List<StashValue>();
        foreach (var table in tableArray)
        {
            list.Add(StashValue.FromObject(ConvertTable(table)));
        }

        return list;
    }

    /// <summary>Recursively converts a <see cref="StashDictionary"/> to a <see cref="TomlTable"/>.</summary>
    /// <param name="dict">The Stash dictionary to convert.</param>
    /// <returns>A <see cref="TomlTable"/> with all nested values converted to Tomlyn types.</returns>
    private static TomlTable ConvertDictToTomlTable(StashDictionary dict)
    {
        var table = new TomlTable();
        foreach (var kvp in dict.RawEntries())
        {
            if (kvp.Value.IsNull)
            {
                continue;
            }

            string key = kvp.Key.ToString()!;
            object? converted = ConvertStashValue(kvp.Value.ToObject());
            if (converted is not null)
            {
                table[key] = converted;
            }
        }

        return table;
    }

    /// <summary>Recursively converts a <see cref="StashInstance"/> to a <see cref="TomlTable"/>.</summary>
    /// <param name="inst">The Stash struct instance to convert.</param>
    /// <returns>A <see cref="TomlTable"/> with all fields converted to Tomlyn types.</returns>
    private static TomlTable ConvertInstanceToTomlTable(StashInstance inst)
    {
        var table = new TomlTable();
        foreach (var kvp in inst.GetFields())
        {
            object? converted = ConvertStashValue(kvp.Value.ToObject());
            if (converted is not null)
            {
                table[kvp.Key] = converted;
            }
        }

        return table;
    }

    /// <summary>Recursively converts a Stash runtime value to a Tomlyn-compatible value.</summary>
    /// <param name="value">The Stash value to convert.</param>
    /// <returns>A Tomlyn-compatible value, or <c>null</c> if the value cannot be represented in TOML.</returns>
    private static object? ConvertStashValue(object? value)
    {
        return value switch
        {
            null => null,
            StashDictionary dict => ConvertDictToTomlTable(dict),
            StashInstance inst => ConvertInstanceToTomlTable(inst),
            List<StashValue> list => ConvertListToToml(list),
            long l => l,
            double d => d,
            string s => s,
            bool b => b,
            _ => value.ToString()
        };
    }

    /// <summary>
    /// Converts a <c>List&lt;StashValue&gt;</c> to either a <see cref="TomlTableArray"/> if every
    /// non-null element is a dict or struct instance, or a <see cref="TomlArray"/> otherwise.
    /// Null elements are silently omitted.
    /// </summary>
    /// <param name="list">The Stash list to convert.</param>
    /// <returns>A <see cref="TomlTableArray"/> or <see cref="TomlArray"/>.</returns>
    private static object ConvertListToToml(List<StashValue> list)
    {
        if (list.Count == 0)
        {
            return new TomlArray();
        }

        bool allTables = true;
        foreach (StashValue item in list)
        {
            var obj = item.ToObject();
            if (obj is null)
            {
                continue;
            }

            if (obj is not StashDictionary && obj is not StashInstance)
            {
                allTables = false;
                break;
            }
        }

        if (allTables)
        {
            var tableArray = new TomlTableArray();
            foreach (StashValue item in list)
            {
                var obj = item.ToObject();
                if (obj is null)
                {
                    continue;
                }

                if (obj is StashDictionary dict)
                {
                    tableArray.Add(ConvertDictToTomlTable(dict));
                }
                else if (obj is StashInstance inst)
                {
                    tableArray.Add(ConvertInstanceToTomlTable(inst));
                }
            }

            return tableArray;
        }
        else
        {
            var tomlArray = new TomlArray();
            foreach (StashValue item in list)
            {
                var obj = item.ToObject();
                if (obj is null)
                {
                    continue;
                }

                var converted = ConvertStashValue(obj);
                if (converted is not null)
                {
                    tomlArray.Add(converted);
                }
            }

            return tomlArray;
        }
    }
}
