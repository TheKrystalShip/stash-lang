namespace Stash.Common;

using System.Collections.Generic;
using System.Globalization;
using Stash.Runtime;
using Stash.Runtime.Types;

/// <summary>Builds CLI argument token lists from a dict specification and a values dict — the reverse of ArgumentParser.</summary>
public static class ArgumentBuilder
{
    private static List<StashValue>? AsObjectList(object? value)
    {
        if (value is List<StashValue> svList) return svList;
        return null;
    }

    /// <summary>
    /// Implements the args.build() built-in function.
    /// Takes a spec dict and a values dict, producing a List&lt;StashValue&gt; of CLI argument token strings.
    /// </summary>
    public static List<StashValue> Build(object? specObj, object? valuesObj)
    {
        if (specObj is not StashDictionary spec)
        {
            throw new RuntimeError("First argument to 'args.build' must be a dict.");
        }

        if (valuesObj is not StashDictionary values)
        {
            throw new RuntimeError("Second argument to 'args.build' must be a dict.");
        }

        var result = new List<StashValue>();

        var flagsSpec = spec.Has("flags") ? spec.Get("flags").ToObject() as StashDictionary : null;
        var optionsSpec = spec.Has("options") ? spec.Get("options").ToObject() as StashDictionary : null;
        var positionalsSpec = spec.Has("positionals") ? AsObjectList(spec.Get("positionals").ToObject()) : null;
        var commandsSpec = spec.Has("commands") ? spec.Get("commands").ToObject() as StashDictionary : null;

        BuildFlags(flagsSpec, values, result);
        BuildOptions(optionsSpec, values, result);

        // Handle subcommand
        if (commandsSpec is not null && values.Has("command"))
        {
            var commandValue = values.Get("command").ToObject();
            if (commandValue is string commandName && commandName.Length > 0)
            {
                result.Add(StashValue.FromObj(commandName));

                var cmdProps = commandsSpec.Has(commandName) ? commandsSpec.Get(commandName).ToObject() as StashDictionary : null;
                var subValues = values.Has(commandName) ? values.Get(commandName).ToObject() as StashDictionary : null;

                if (cmdProps is not null && subValues is not null)
                {
                    var subFlagsSpec = cmdProps.Has("flags") ? cmdProps.Get("flags").ToObject() as StashDictionary : null;
                    var subOptionsSpec = cmdProps.Has("options") ? cmdProps.Get("options").ToObject() as StashDictionary : null;
                    var subPositionalsSpec = cmdProps.Has("positionals") ? AsObjectList(cmdProps.Get("positionals").ToObject()) : null;

                    BuildFlags(subFlagsSpec, subValues, result);
                    BuildOptions(subOptionsSpec, subValues, result);
                    BuildPositionals(subPositionalsSpec, subValues, result);
                }
            }
        }

        BuildPositionals(positionalsSpec, values, result);

        return result;
    }

    private static void BuildFlags(StashDictionary? flagsSpec, StashDictionary values, List<StashValue> result)
    {
        if (flagsSpec is null)
        {
            return;
        }

        foreach (var entry in flagsSpec.RawEntries())
        {
            string name = (string)entry.Key;
            if (!values.Has(name))
            {
                continue;
            }

            var val = values.Get(name).ToObject();
            if (val is not true)
            {
                continue;
            }

            var props = entry.Value.ToObject() as StashDictionary;
            string? flagStr = props is not null && props.Has("flag") ? props.Get("flag").ToObject() as string : null;
            string? shortName = props is not null && props.Has("short") ? props.Get("short").ToObject() as string : null;
            result.Add(StashValue.FromObj(flagStr ?? (shortName is not null ? $"-{shortName}" : $"--{name}")));
        }
    }

    private static void BuildOptions(StashDictionary? optionsSpec, StashDictionary values, List<StashValue> result)
    {
        if (optionsSpec is null)
        {
            return;
        }

        foreach (var entry in optionsSpec.RawEntries())
        {
            string name = (string)entry.Key;
            if (!values.Has(name))
            {
                continue;
            }

            var val = values.Get(name).ToObject();
            if (val is null)
            {
                continue;
            }

            var props = entry.Value.ToObject() as StashDictionary;
            string? flagStr = props is not null && props.Has("flag") ? props.Get("flag").ToObject() as string : null;
            string? shortName = props is not null && props.Has("short") ? props.Get("short").ToObject() as string : null;
            string? type = props is not null && props.Has("type") ? props.Get("type").ToObject() as string : null;
            string flag = flagStr ?? (shortName is not null ? $"-{shortName}" : $"--{name}");

            switch (type)
            {
                case "list":
                    var listVal = AsObjectList(val);
                    if (listVal is null)
                    {
                        throw new RuntimeError($"Option '--{name}' has type 'list' but value is not an array.");
                    }

                    foreach (StashValue item in listVal)
                    {
                        result.Add(StashValue.FromObj(flag));
                        result.Add(StashValue.FromObj(FormatValue(item.ToObject())));
                    }
                    break;

                case "map":
                    if (val is not StashDictionary mapVal)
                    {
                        throw new RuntimeError($"Option '--{name}' has type 'map' but value is not a dictionary.");
                    }

                    foreach (var kvp in mapVal.RawEntries())
                    {
                        result.Add(StashValue.FromObj(flag));
                        result.Add(StashValue.FromObj($"{kvp.Key}={FormatValue(kvp.Value.ToObject())}"));
                    }
                    break;

                case "csv":
                    var csvVal = AsObjectList(val);
                    if (csvVal is null)
                    {
                        throw new RuntimeError($"Option '--{name}' has type 'csv' but value is not an array.");
                    }

                    result.Add(StashValue.FromObj(flag));
                    result.Add(StashValue.FromObj(string.Join(",", csvVal.ConvertAll(v => FormatValue(v.ToObject())))));
                    break;

                default:
                    if (val is List<StashValue>)
                    {
                        throw new RuntimeError($"Option '--{name}' has an array value but type is not 'list' or 'csv'.");
                    }

                    if (val is StashDictionary)
                    {
                        throw new RuntimeError($"Option '--{name}' has a dict value but type is not 'map'.");
                    }

                    result.Add(StashValue.FromObj(flag));
                    result.Add(StashValue.FromObj(FormatValue(val)));
                    break;
            }
        }
    }

    private static void BuildPositionals(List<StashValue>? positionalsSpec, StashDictionary values, List<StashValue> result)
    {
        if (positionalsSpec is null)
        {
            return;
        }

        foreach (StashValue item in positionalsSpec)
        {
            if (item.ToObject() is not StashDictionary posDict)
            {
                continue;
            }

            string? posName = posDict.Has("name") ? posDict.Get("name").ToObject() as string : null;
            if (posName is null)
            {
                continue;
            }

            if (!values.Has(posName))
            {
                continue;
            }

            var val = values.Get(posName).ToObject();
            if (val is null)
            {
                continue;
            }

            result.Add(StashValue.FromObj(FormatValue(val)));
        }
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            bool b => b ? "true" : "false",
            long l => l.ToString(),
            double d => d.ToString(CultureInfo.InvariantCulture),
            string s => s,
            _ => value?.ToString() ?? ""
        };
    }
}
