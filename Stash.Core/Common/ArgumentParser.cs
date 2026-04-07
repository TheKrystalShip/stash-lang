namespace Stash.Common;

using System;
using System.Collections.Generic;
using System.Text;
using Stash.Runtime;
using Stash.Runtime.Types;

/// <summary>Parses command-line arguments against a dict specification, producing a dict with all parsed values.</summary>
public sealed class ArgumentParser
{
    private static List<StashValue>? AsObjectList(object? value)
    {
        if (value is List<StashValue> svList) return svList;
        return null;
    }

    /// <summary>The raw command-line arguments to parse.</summary>
    private readonly string[] _scriptArgs;

    /// <summary>Creates a new argument parser for the given script arguments.</summary>
    /// <param name="scriptArgs">The raw command-line arguments.</param>
    public ArgumentParser(string[] scriptArgs)
    {
        _scriptArgs = scriptArgs;
    }

    /// <summary>
    /// Implements the args.parse() built-in function.
    /// Takes a dict spec and parses _scriptArgs against it.
    /// Returns a StashDictionary with all parsed argument values.
    /// </summary>
    public object? Parse(object? specObj)
    {
        if (specObj is not StashDictionary spec)
        {
            throw new RuntimeError("Argument to 'args.parse' must be a dict.");
        }

        string? scriptName = spec.Has("name") ? spec.Get("name").ToObject() as string : null;
        string? version = spec.Has("version") ? spec.Get("version").ToObject() as string : null;
        var flagsSpec = spec.Has("flags") ? spec.Get("flags").ToObject() as StashDictionary : null;
        var optionsSpec = spec.Has("options") ? spec.Get("options").ToObject() as StashDictionary : null;
        var commandsSpec = spec.Has("commands") ? spec.Get("commands").ToObject() as StashDictionary : null;
        var positionalsSpec = spec.Has("positionals") ? AsObjectList(spec.Get("positionals").ToObject()) : null;

        var result = new StashDictionary();

        // Initialize all flags to false
        if (flagsSpec is not null)
        {
            foreach (var entry in flagsSpec.RawEntries())
            {
                result.Set((string)entry.Key, StashValue.False);
            }
        }

        // Initialize all options with defaults
        if (optionsSpec is not null)
        {
            foreach (var entry in optionsSpec.RawEntries())
            {
                var props = entry.Value.ToObject() as StashDictionary;
                object? defaultVal = props is not null && props.Has("default") ? props.Get("default").ToObject() : null;
                result.Set((string)entry.Key, StashValue.FromObject(defaultVal));
            }
        }

        // Initialize all positionals with defaults
        if (positionalsSpec is not null)
        {
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

                object? defaultVal = posDict.Has("default") ? posDict.Get("default").ToObject() : null;
                result.Set(posName, StashValue.FromObject(defaultVal));
            }
        }

        // Initialize command to null
        if (commandsSpec is not null && commandsSpec.Count > 0)
        {
            result.Set("command", StashValue.Null);
        }

        // Initialize subcommand containers
        if (commandsSpec is not null)
        {
            foreach (var entry in commandsSpec.RawEntries())
            {
                string cmdName = (string)entry.Key;
                var cmdResult = new StashDictionary();
                var cmdProps = entry.Value.ToObject() as StashDictionary;

                if (cmdProps is not null)
                {
                    // Initialize sub-flags
                    var subFlags = cmdProps.Has("flags") ? cmdProps.Get("flags").ToObject() as StashDictionary : null;
                    if (subFlags is not null)
                    {
                        foreach (var sf in subFlags.RawEntries())
                        {
                            cmdResult.Set((string)sf.Key, StashValue.False);
                        }
                    }

                    // Initialize sub-options with defaults
                    var subOpts = cmdProps.Has("options") ? cmdProps.Get("options").ToObject() as StashDictionary : null;
                    if (subOpts is not null)
                    {
                        foreach (var so in subOpts.RawEntries())
                        {
                            var soProps = so.Value.ToObject() as StashDictionary;
                            object? def = soProps is not null && soProps.Has("default") ? soProps.Get("default").ToObject() : null;
                            cmdResult.Set((string)so.Key, StashValue.FromObject(def));
                        }
                    }

                    // Initialize sub-positionals with defaults
                    var subPos = cmdProps.Has("positionals") ? AsObjectList(cmdProps.Get("positionals").ToObject()) : null;
                    if (subPos is not null)
                    {
                        foreach (StashValue sp in subPos)
                        {
                            if (sp.ToObject() is not StashDictionary spDict)
                            {
                                continue;
                            }

                            string? spName = spDict.Has("name") ? spDict.Get("name").ToObject() as string : null;
                            if (spName is null)
                            {
                                continue;
                            }

                            object? def = spDict.Has("default") ? spDict.Get("default").ToObject() : null;
                            cmdResult.Set(spName, StashValue.FromObject(def));
                        }
                    }
                }

                result.Set(cmdName, StashValue.FromObj(cmdResult));
            }
        }

        // Build lookup maps for efficient parsing
        var flagsByLong = new Dictionary<string, string>();        // "--name" → field name
        var flagsByShort = new Dictionary<string, string>();       // "-n"    → field name
        var optionsByLong = new Dictionary<string, StashDictionary?>();   // "--name" → props dict
        var optionsByShort = new Dictionary<string, StashDictionary?>();  // "-n"    → props dict
        var optionNameByLong = new Dictionary<string, string>();   // "--name" → field name
        var optionNameByShort = new Dictionary<string, string>();  // "-n"    → field name
        var commandNames = new HashSet<string>();

        if (flagsSpec is not null)
        {
            foreach (var entry in flagsSpec.RawEntries())
            {
                string name = (string)entry.Key;
                flagsByLong[$"--{name}"] = name;
                var props = entry.Value.ToObject() as StashDictionary;
                string? shortName = props is not null && props.Has("short") ? props.Get("short").ToObject() as string : null;
                if (shortName is not null)
                {
                    flagsByShort[$"-{shortName}"] = name;
                }
            }
        }

        if (optionsSpec is not null)
        {
            foreach (var entry in optionsSpec.RawEntries())
            {
                string name = (string)entry.Key;
                var props = entry.Value.ToObject() as StashDictionary;
                optionsByLong[$"--{name}"] = props;
                optionNameByLong[$"--{name}"] = name;
                string? shortName = props is not null && props.Has("short") ? props.Get("short").ToObject() as string : null;
                if (shortName is not null)
                {
                    optionsByShort[$"-{shortName}"] = props;
                    optionNameByShort[$"-{shortName}"] = name;
                }
            }
        }

        if (commandsSpec is not null)
        {
            foreach (var entry in commandsSpec.RawEntries())
            {
                commandNames.Add((string)entry.Key);
            }
        }

        // Build per-command lookup maps
        var cmdFlagsByLong = new Dictionary<string, Dictionary<string, string>>();
        var cmdFlagsByShort = new Dictionary<string, Dictionary<string, string>>();
        var cmdOptionsByLong = new Dictionary<string, Dictionary<string, (string Name, StashDictionary? Props)>>();
        var cmdOptionsByShort = new Dictionary<string, Dictionary<string, (string Name, StashDictionary? Props)>>();
        var cmdPositionalDefs = new Dictionary<string, List<StashDictionary>>();
        var cmdPositionalIndices = new Dictionary<string, int>();

        if (commandsSpec is not null)
        {
            foreach (var entry in commandsSpec.RawEntries())
            {
                string cmdName = (string)entry.Key;
                var cmdProps = entry.Value.ToObject() as StashDictionary;

                var cfl = new Dictionary<string, string>();
                var cfs = new Dictionary<string, string>();
                var col = new Dictionary<string, (string, StashDictionary?)>();
                var cos = new Dictionary<string, (string, StashDictionary?)>();
                var cpos = new List<StashDictionary>();
                cmdPositionalIndices[cmdName] = 0;

                if (cmdProps is not null)
                {
                    var subFlags = cmdProps.Has("flags") ? cmdProps.Get("flags").ToObject() as StashDictionary : null;
                    if (subFlags is not null)
                    {
                        foreach (var sf in subFlags.RawEntries())
                        {
                            string n = (string)sf.Key;
                            cfl[$"--{n}"] = n;
                            var sfProps = sf.Value.ToObject() as StashDictionary;
                            string? s = sfProps is not null && sfProps.Has("short") ? sfProps.Get("short").ToObject() as string : null;
                            if (s is not null)
                            {
                                cfs[$"-{s}"] = n;
                            }
                        }
                    }

                    var subOpts = cmdProps.Has("options") ? cmdProps.Get("options").ToObject() as StashDictionary : null;
                    if (subOpts is not null)
                    {
                        foreach (var so in subOpts.RawEntries())
                        {
                            string n = (string)so.Key;
                            var soProps = so.Value.ToObject() as StashDictionary;
                            col[$"--{n}"] = (n, soProps);
                            string? s = soProps is not null && soProps.Has("short") ? soProps.Get("short").ToObject() as string : null;
                            if (s is not null)
                            {
                                cos[$"-{s}"] = (n, soProps);
                            }
                        }
                    }

                    var subPos = cmdProps.Has("positionals") ? AsObjectList(cmdProps.Get("positionals").ToObject()) : null;
                    if (subPos is not null)
                    {
                        foreach (StashValue sp in subPos)
                        {
                            if (sp.ToObject() is StashDictionary spDict)
                            {
                                cpos.Add(spDict);
                            }
                        }
                    }
                }

                cmdFlagsByLong[cmdName] = cfl;
                cmdFlagsByShort[cmdName] = cfs;
                cmdOptionsByLong[cmdName] = col;
                cmdOptionsByShort[cmdName] = cos;
                cmdPositionalDefs[cmdName] = cpos;
            }
        }

        // Parse _scriptArgs
        int positionalIndex = 0;
        string? activeCommandName = null;

        int i = 0;
        while (i < _scriptArgs.Length)
        {
            string arg = _scriptArgs[i];

            // Check for --key=value format
            string? equalValue = null;
            if (arg.StartsWith("--") && arg.Contains('='))
            {
                int eqIdx = arg.IndexOf('=');
                equalValue = arg.Substring(eqIdx + 1);
                arg = arg.Substring(0, eqIdx);
            }

            // If we have an active command, try command-level args first
            if (activeCommandName is not null)
            {
                var cmdResult = (StashDictionary)result.Get(activeCommandName).AsObj!;

                if (cmdFlagsByLong[activeCommandName].TryGetValue(arg, out var cmdFlagName))
                {
                    cmdResult.Set(cmdFlagName, StashValue.True);
                    i++;
                    continue;
                }
                if (cmdFlagsByShort[activeCommandName].TryGetValue(arg, out cmdFlagName))
                {
                    cmdResult.Set(cmdFlagName, StashValue.True);
                    i++;
                    continue;
                }
                if (cmdOptionsByLong[activeCommandName].TryGetValue(arg, out var cmdOpt))
                {
                    string? val = equalValue;
                    if (val is null)
                    {
                        i++;
                        if (i >= _scriptArgs.Length)
                        {
                            throw new RuntimeError($"Option '{arg}' requires a value.");
                        }

                        val = _scriptArgs[i];
                    }
                    string? optType = cmdOpt.Props is not null && cmdOpt.Props.Has("type") ? cmdOpt.Props.Get("type").ToObject() as string : null;
                    cmdResult.Set(cmdOpt.Name, StashValue.FromObject(CoerceArgValue(val, optType, arg)));
                    i++;
                    continue;
                }
                if (cmdOptionsByShort[activeCommandName].TryGetValue(arg, out cmdOpt))
                {
                    string? val = equalValue;
                    if (val is null)
                    {
                        i++;
                        if (i >= _scriptArgs.Length)
                        {
                            throw new RuntimeError($"Option '{arg}' requires a value.");
                        }

                        val = _scriptArgs[i];
                    }
                    string? optType = cmdOpt.Props is not null && cmdOpt.Props.Has("type") ? cmdOpt.Props.Get("type").ToObject() as string : null;
                    cmdResult.Set(cmdOpt.Name, StashValue.FromObject(CoerceArgValue(val, optType, arg)));
                    i++;
                    continue;
                }

                // Check command-level positionals
                int cmdPosIdx = cmdPositionalIndices[activeCommandName];
                if (!arg.StartsWith("-") && cmdPosIdx < cmdPositionalDefs[activeCommandName].Count)
                {
                    var cp = cmdPositionalDefs[activeCommandName][cmdPosIdx];
                    string cpName = cp.Has("name") ? cp.Get("name").ToObject() as string ?? arg : arg;
                    string? posType = cp.Has("type") ? cp.Get("type").ToObject() as string : null;
                    cmdResult.Set(cpName, StashValue.FromObject(CoerceArgValue(arg, posType, cpName)));
                    cmdPositionalIndices[activeCommandName]++;
                    i++;
                    continue;
                }
            }

            // Top-level flag match
            if (flagsByLong.TryGetValue(arg, out var topFlagName))
            {
                result.Set(topFlagName, StashValue.True);
                i++;
                continue;
            }
            if (flagsByShort.TryGetValue(arg, out topFlagName))
            {
                result.Set(topFlagName, StashValue.True);
                i++;
                continue;
            }

            // Top-level option match
            if (optionsByLong.TryGetValue(arg, out var topOptProps) && optionNameByLong.TryGetValue(arg, out var topOptName))
            {
                string? val = equalValue;
                if (val is null)
                {
                    i++;
                    if (i >= _scriptArgs.Length)
                    {
                        throw new RuntimeError($"Option '{arg}' requires a value.");
                    }

                    val = _scriptArgs[i];
                }
                string? optType = topOptProps is not null && topOptProps.Has("type") ? topOptProps.Get("type").ToObject() as string : null;
                result.Set(topOptName, StashValue.FromObject(CoerceArgValue(val, optType, arg)));
                i++;
                continue;
            }
            if (optionsByShort.TryGetValue(arg, out topOptProps) && optionNameByShort.TryGetValue(arg, out topOptName))
            {
                string? val = equalValue;
                if (val is null)
                {
                    i++;
                    if (i >= _scriptArgs.Length)
                    {
                        throw new RuntimeError($"Option '{arg}' requires a value.");
                    }

                    val = _scriptArgs[i];
                }
                string? optType = topOptProps is not null && topOptProps.Has("type") ? topOptProps.Get("type").ToObject() as string : null;
                result.Set(topOptName, StashValue.FromObject(CoerceArgValue(val, optType, arg)));
                i++;
                continue;
            }

            // Command match
            if (!arg.StartsWith("-") && commandNames.Contains(arg))
            {
                result.Set("command", StashValue.FromObj(arg));
                activeCommandName = arg;
                i++;
                continue;
            }

            // Positional (only non-dash args when not matching a command)
            if (!arg.StartsWith("-") && positionalsSpec is not null && positionalIndex < positionalsSpec.Count)
            {
                var pos = positionalsSpec[positionalIndex].ToObject() as StashDictionary;
                string posName = pos is not null && pos.Has("name") ? pos.Get("name").ToObject() as string ?? arg : arg;
                string? posType = pos is not null && pos.Has("type") ? pos.Get("type").ToObject() as string : null;
                result.Set(posName, StashValue.FromObject(CoerceArgValue(arg, posType, posName)));
                positionalIndex++;
                i++;
                continue;
            }

            // Unknown argument
            throw new RuntimeError($"Unknown argument '{_scriptArgs[i]}'.");
        }

        // Auto-handle help flag
        if (result.Has("help") && result.Get("help").ToObject() is true)
        {
            PrintHelp(spec, result);
            System.Environment.Exit(0);
        }

        // Auto-handle version flag
        if (result.Has("version") && result.Get("version").ToObject() is true && version is not null)
        {
            Console.WriteLine(version);
            System.Environment.Exit(0);
        }

        // Validate required options
        if (optionsSpec is not null)
        {
            foreach (var entry in optionsSpec.RawEntries())
            {
                string optName = (string)entry.Key;
                var props = entry.Value.ToObject() as StashDictionary;
                bool required = props is not null && props.Has("required") && props.Get("required").ToObject() is true;
                if (required && result.Get(optName).ToObject() is null)
                {
                    throw new RuntimeError($"Required option '--{optName}' was not provided.");
                }
            }
        }

        // Validate required positionals
        if (positionalsSpec is not null)
        {
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

                bool required = posDict.Has("required") && posDict.Get("required").ToObject() is true;
                if (required && result.Get(posName).ToObject() is null)
                {
                    throw new RuntimeError($"Required positional argument '{posName}' was not provided.");
                }
            }
        }

        // Validate required command-level args if a command is active
        if (activeCommandName is not null && commandsSpec is not null)
        {
            var cmdProps = commandsSpec.Get(activeCommandName).ToObject() as StashDictionary;
            if (cmdProps is not null)
            {
                var cmdResult = (StashDictionary)result.Get(activeCommandName).AsObj!;

                var subOpts = cmdProps.Has("options") ? cmdProps.Get("options").ToObject() as StashDictionary : null;
                if (subOpts is not null)
                {
                    foreach (var entry in subOpts.RawEntries())
                    {
                        string optName = (string)entry.Key;
                        var props = entry.Value.ToObject() as StashDictionary;
                        bool required = props is not null && props.Has("required") && props.Get("required").ToObject() is true;
                        if (required && cmdResult.Get(optName).ToObject() is null)
                        {
                            throw new RuntimeError($"Required option '--{optName}' for command '{activeCommandName}' was not provided.");
                        }
                    }
                }

                var subPos = cmdProps.Has("positionals") ? AsObjectList(cmdProps.Get("positionals").ToObject()) : null;
                if (subPos is not null)
                {
                    foreach (StashValue sp in subPos)
                    {
                        if (sp.ToObject() is not StashDictionary spDict)
                        {
                            continue;
                        }

                        string? posName = spDict.Has("name") ? spDict.Get("name").ToObject() as string : null;
                        if (posName is null)
                        {
                            continue;
                        }

                        bool required = spDict.Has("required") && spDict.Get("required").ToObject() is true;
                        if (required && cmdResult.Get(posName).ToObject() is null)
                        {
                            throw new RuntimeError($"Required positional argument '{posName}' for command '{activeCommandName}' was not provided.");
                        }
                    }
                }
            }
        }

        return result;
    }

    /// <summary>Coerces a string argument value to the specified type (string, int, float, bool).</summary>
    private static object? CoerceArgValue(string value, string? type, string argName)
    {
        if (type is null || type == "string")
        {
            return value;
        }

        if (type == "int")
        {
            if (long.TryParse(value, out long result))
            {
                return result;
            }

            throw new RuntimeError($"Cannot parse '{value}' as int for argument '{argName}'.");
        }

        if (type == "float")
        {
            if (double.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double result))
            {
                return result;
            }

            throw new RuntimeError($"Cannot parse '{value}' as float for argument '{argName}'.");
        }

        if (type == "bool")
        {
            if (value == "true" || value == "1" || value == "yes")
            {
                return true;
            }

            if (value == "false" || value == "0" || value == "no")
            {
                return false;
            }

            throw new RuntimeError($"Cannot parse '{value}' as bool for argument '{argName}'.");
        }

        throw new RuntimeError($"Unknown type '{type}' for argument '{argName}'.");
    }

    /// <summary>Prints formatted help text for the argument specification.</summary>
    private void PrintHelp(StashDictionary spec, StashDictionary parsed)
    {
        var sb = new StringBuilder();

        string? scriptName = spec.Has("name") ? spec.Get("name").ToObject() as string : null;
        string? version = spec.Has("version") ? spec.Get("version").ToObject() as string : null;
        string? description = spec.Has("description") ? spec.Get("description").ToObject() as string : null;
        var flagsSpec = spec.Has("flags") ? spec.Get("flags").ToObject() as StashDictionary : null;
        var optionsSpec = spec.Has("options") ? spec.Get("options").ToObject() as StashDictionary : null;
        var commandsSpec = spec.Has("commands") ? spec.Get("commands").ToObject() as StashDictionary : null;
        var positionalsSpec = spec.Has("positionals") ? AsObjectList(spec.Get("positionals").ToObject()) : null;

        // Header
        if (scriptName is not null)
        {
            sb.Append(scriptName);
            if (version is not null)
            {
                sb.Append($" v{version}");
            }

            sb.AppendLine();
        }

        if (description is not null && description != "")
        {
            sb.AppendLine(description);
        }

        if (scriptName is not null || (description is not null && description != ""))
        {
            sb.AppendLine();
        }

        // Usage line
        sb.AppendLine("USAGE:");
        sb.Append("  ");
        sb.Append(scriptName ?? "script");
        if (commandsSpec is not null && commandsSpec.Count > 0)
        {
            sb.Append(" [command]");
        }

        if ((optionsSpec is not null && optionsSpec.Count > 0) || (flagsSpec is not null && flagsSpec.Count > 0))
        {
            sb.Append(" [options]");
        }

        if (positionalsSpec is not null)
        {
            foreach (StashValue posObj in positionalsSpec)
            {
                if (posObj.ToObject() is not StashDictionary pos)
                {
                    continue;
                }

                string posName = pos.Has("name") ? pos.Get("name").ToObject() as string ?? "arg" : "arg";
                bool required = pos.Has("required") && pos.Get("required").ToObject() is true;
                sb.Append(required ? $" <{posName}>" : $" [{posName}]");
            }
        }
        sb.AppendLine();
        sb.AppendLine();

        // Commands
        if (commandsSpec is not null && commandsSpec.Count > 0)
        {
            sb.AppendLine("COMMANDS:");
            int maxCmdLen = 0;
            foreach (var entry in commandsSpec.RawEntries())
            {
                string cmdName = (string)entry.Key;
                if (cmdName.Length > maxCmdLen)
                {
                    maxCmdLen = cmdName.Length;
                }
            }
            foreach (var entry in commandsSpec.RawEntries())
            {
                string cmdName = (string)entry.Key;
                var cmdProps = entry.Value.ToObject() as StashDictionary;
                string? cmdDesc = cmdProps is not null && cmdProps.Has("description") ? cmdProps.Get("description").ToObject() as string : null;
                sb.Append($"  {cmdName.PadRight(maxCmdLen + 2)}");
                if (cmdDesc is not null)
                {
                    sb.Append(cmdDesc);
                }

                sb.AppendLine();
            }
            sb.AppendLine();
        }

        // Positional arguments
        if (positionalsSpec is not null && positionalsSpec.Count > 0)
        {
            sb.AppendLine("ARGUMENTS:");
            int maxPosLen = 0;
            foreach (StashValue posObj in positionalsSpec)
            {
                if (posObj.ToObject() is not StashDictionary pos)
                {
                    continue;
                }

                string posName = pos.Has("name") ? pos.Get("name").ToObject() as string ?? "arg" : "arg";
                bool required = pos.Has("required") && pos.Get("required").ToObject() is true;
                string label = required ? $"<{posName}>" : $"[{posName}]";
                if (label.Length > maxPosLen)
                {
                    maxPosLen = label.Length;
                }
            }
            foreach (StashValue posObj in positionalsSpec)
            {
                if (posObj.ToObject() is not StashDictionary pos)
                {
                    continue;
                }

                string posName = pos.Has("name") ? pos.Get("name").ToObject() as string ?? "arg" : "arg";
                bool required = pos.Has("required") && pos.Get("required").ToObject() is true;
                string? posDesc = pos.Has("description") ? pos.Get("description").ToObject() as string : null;
                object? posDefault = pos.Has("default") ? pos.Get("default").ToObject() : null;
                string label = required ? $"<{posName}>" : $"[{posName}]";
                sb.Append($"  {label.PadRight(maxPosLen + 2)}");
                if (posDesc is not null)
                {
                    sb.Append(posDesc);
                }

                if (posDefault is not null)
                {
                    sb.Append($" (default: {RuntimeValues.Stringify(posDefault)})");
                }

                sb.AppendLine();
            }
            sb.AppendLine();
        }

        // Options and flags
        if ((flagsSpec is not null && flagsSpec.Count > 0) || (optionsSpec is not null && optionsSpec.Count > 0))
        {
            sb.AppendLine("OPTIONS:");
            var optLines = new List<(string Left, string? Right)>();

            if (flagsSpec is not null)
            {
                foreach (var entry in flagsSpec.RawEntries())
                {
                    string flagName = (string)entry.Key;
                    var props = entry.Value.ToObject() as StashDictionary;
                    string? shortName = props is not null && props.Has("short") ? props.Get("short").ToObject() as string : null;
                    string? flagDesc = props is not null && props.Has("description") ? props.Get("description").ToObject() as string : null;
                    string left = shortName is not null ? $"-{shortName}, --{flagName}" : $"    --{flagName}";
                    optLines.Add((left, flagDesc));
                }
            }

            if (optionsSpec is not null)
            {
                foreach (var entry in optionsSpec.RawEntries())
                {
                    string optName = (string)entry.Key;
                    var props = entry.Value.ToObject() as StashDictionary;
                    string? shortName = props is not null && props.Has("short") ? props.Get("short").ToObject() as string : null;
                    string? optType = props is not null && props.Has("type") ? props.Get("type").ToObject() as string : null;
                    string? optDesc = props is not null && props.Has("description") ? props.Get("description").ToObject() as string : null;
                    object? optDefault = props is not null && props.Has("default") ? props.Get("default").ToObject() : null;
                    bool required = props is not null && props.Has("required") && props.Get("required").ToObject() is true;
                    string typeHint = optType is not null ? $" <{optType}>" : " <value>";
                    string left = shortName is not null ? $"-{shortName}, --{optName}{typeHint}" : $"    --{optName}{typeHint}";
                    string? right = optDesc;
                    if (required)
                    {
                        right = (right ?? "") + " (required)";
                    }
                    else if (optDefault is not null)
                    {
                        right = (right ?? "") + $" (default: {RuntimeValues.Stringify(optDefault)})";
                    }

                    optLines.Add((left, right));
                }
            }

            int maxLeft = 0;
            foreach (var (left, _) in optLines)
            {
                if (left.Length > maxLeft)
                {
                    maxLeft = left.Length;
                }
            }

            foreach (var (left, right) in optLines)
            {
                sb.Append($"  {left.PadRight(maxLeft + 2)}");
                if (right is not null)
                {
                    sb.Append(right);
                }

                sb.AppendLine();
            }
            sb.AppendLine();
        }

        // Per-command details
        if (commandsSpec is not null)
        {
            foreach (var entry in commandsSpec.RawEntries())
            {
                string cmdName = (string)entry.Key;
                var cmdProps = entry.Value.ToObject() as StashDictionary;
                if (cmdProps is null)
                {
                    continue;
                }

                var subFlags = cmdProps.Has("flags") ? cmdProps.Get("flags").ToObject() as StashDictionary : null;
                var subOpts = cmdProps.Has("options") ? cmdProps.Get("options").ToObject() as StashDictionary : null;
                var subPos = cmdProps.Has("positionals") ? AsObjectList(cmdProps.Get("positionals").ToObject()) : null;

                bool hasSubArgs = (subFlags is not null && subFlags.Count > 0) ||
                                  (subOpts is not null && subOpts.Count > 0) ||
                                  (subPos is not null && subPos.Count > 0);
                if (!hasSubArgs)
                {
                    continue;
                }

                sb.AppendLine($"COMMAND '{cmdName}':");

                if (subPos is not null && subPos.Count > 0)
                {
                    foreach (StashValue posObj in subPos)
                    {
                        if (posObj.ToObject() is not StashDictionary pos)
                        {
                            continue;
                        }

                        string posName = pos.Has("name") ? pos.Get("name").ToObject() as string ?? "arg" : "arg";
                        bool required = pos.Has("required") && pos.Get("required").ToObject() is true;
                        string? posDesc = pos.Has("description") ? pos.Get("description").ToObject() as string : null;
                        string label = required ? $"<{posName}>" : $"[{posName}]";
                        sb.Append($"  {label,-20}");
                        if (posDesc is not null)
                        {
                            sb.Append(posDesc);
                        }

                        sb.AppendLine();
                    }
                }

                var cmdOptLines = new List<(string Left, string? Right)>();

                if (subFlags is not null)
                {
                    foreach (var sfEntry in subFlags.RawEntries())
                    {
                        string flagName = (string)sfEntry.Key;
                        var props = sfEntry.Value.ToObject() as StashDictionary;
                        string? shortName = props is not null && props.Has("short") ? props.Get("short").ToObject() as string : null;
                        string? flagDesc = props is not null && props.Has("description") ? props.Get("description").ToObject() as string : null;
                        string left = shortName is not null ? $"-{shortName}, --{flagName}" : $"    --{flagName}";
                        cmdOptLines.Add((left, flagDesc));
                    }
                }

                if (subOpts is not null)
                {
                    foreach (var soEntry in subOpts.RawEntries())
                    {
                        string optName = (string)soEntry.Key;
                        var props = soEntry.Value.ToObject() as StashDictionary;
                        string? shortName = props is not null && props.Has("short") ? props.Get("short").ToObject() as string : null;
                        string? optType = props is not null && props.Has("type") ? props.Get("type").ToObject() as string : null;
                        string? optDesc = props is not null && props.Has("description") ? props.Get("description").ToObject() as string : null;
                        object? optDefault = props is not null && props.Has("default") ? props.Get("default").ToObject() : null;
                        bool required = props is not null && props.Has("required") && props.Get("required").ToObject() is true;
                        string typeHint = optType is not null ? $" <{optType}>" : " <value>";
                        string left = shortName is not null ? $"-{shortName}, --{optName}{typeHint}" : $"    --{optName}{typeHint}";
                        string? right = optDesc;
                        if (required)
                        {
                            right = (right ?? "") + " (required)";
                        }
                        else if (optDefault is not null)
                        {
                            right = (right ?? "") + $" (default: {RuntimeValues.Stringify(optDefault)})";
                        }

                        cmdOptLines.Add((left, right));
                    }
                }

                if (cmdOptLines.Count > 0)
                {
                    int maxLeft = 0;
                    foreach (var (left, _) in cmdOptLines)
                    {
                        if (left.Length > maxLeft)
                        {
                            maxLeft = left.Length;
                        }
                    }

                    foreach (var (left, right) in cmdOptLines)
                    {
                        sb.Append($"  {left.PadRight(maxLeft + 2)}");
                        if (right is not null)
                        {
                            sb.Append(right);
                        }

                        sb.AppendLine();
                    }
                }
                sb.AppendLine();
            }
        }

        Console.Write(sb.ToString());
    }
}
