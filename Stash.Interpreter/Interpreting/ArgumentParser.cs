namespace Stash.Interpreting;

using System;
using System.Collections.Generic;
using Stash.Interpreting.Types;

internal sealed class ArgumentParser
{
    private readonly string[] _scriptArgs;

    public ArgumentParser(string[] scriptArgs)
    {
        _scriptArgs = scriptArgs;
    }

    /// <summary>
    /// Implements the parseArgs() built-in function.
    /// Takes an ArgTree StashInstance and parses _scriptArgs against it.
    /// Returns a StashInstance with all parsed argument values.
    /// </summary>
    public object? Parse(object? treeObj)
    {
        if (treeObj is not StashInstance tree || tree.TypeName != "ArgTree")
        {
            throw new RuntimeError("Argument to 'parseArgs' must be an ArgTree instance.");
        }

        string? scriptName = tree.GetField("name", null) as string;
        string? version = tree.GetField("version", null) as string;
        var flagDefs = tree.GetField("flags", null) as List<object?> ?? new();
        var optionDefs = tree.GetField("options", null) as List<object?> ?? new();
        var commandDefs = tree.GetField("commands", null) as List<object?> ?? new();
        var positionalDefs = tree.GetField("positionals", null) as List<object?> ?? new();

        var fields = new Dictionary<string, object?>();

        // Initialize all flags to false
        foreach (var flagObj in flagDefs)
        {
            var flag = CastArgDef(flagObj, "flags");
            fields[GetArgDefName(flag)] = false;
        }

        // Initialize all options with defaults
        foreach (var optObj in optionDefs)
        {
            var opt = CastArgDef(optObj, "options");
            fields[GetArgDefName(opt)] = opt.GetField("default", null);
        }

        // Initialize all positionals with defaults
        foreach (var posObj in positionalDefs)
        {
            var pos = CastArgDef(posObj, "positionals");
            fields[GetArgDefName(pos)] = pos.GetField("default", null);
        }

        // Initialize command to null
        if (commandDefs.Count > 0)
        {
            fields["command"] = null;
        }

        // Initialize subcommand containers
        foreach (var cmdObj in commandDefs)
        {
            var cmd = CastArgDef(cmdObj, "commands");
            string cmdName = GetArgDefName(cmd);
            var subTree = cmd.GetField("args", null) as StashInstance;
            var cmdFields = new Dictionary<string, object?>();

            if (subTree is not null)
            {
                var subFlags = subTree.GetField("flags", null) as List<object?> ?? new();
                var subOpts = subTree.GetField("options", null) as List<object?> ?? new();
                var subPos = subTree.GetField("positionals", null) as List<object?> ?? new();

                foreach (var f in subFlags)
                {
                    var fd = CastArgDef(f, "flags");
                    cmdFields[GetArgDefName(fd)] = false;
                }
                foreach (var o in subOpts)
                {
                    var od = CastArgDef(o, "options");
                    cmdFields[GetArgDefName(od)] = od.GetField("default", null);
                }
                foreach (var p in subPos)
                {
                    var pd = CastArgDef(p, "positionals");
                    cmdFields[GetArgDefName(pd)] = pd.GetField("default", null);
                }
            }
            fields[cmdName] = new StashInstance("ArgsCommand", cmdFields);
        }

        // Build lookup maps for efficient parsing
        var flagsByLong = new Dictionary<string, StashInstance>();
        var flagsByShort = new Dictionary<string, StashInstance>();
        var optionsByLong = new Dictionary<string, StashInstance>();
        var optionsByShort = new Dictionary<string, StashInstance>();
        var commandsByName = new Dictionary<string, StashInstance>();

        foreach (var flagObj in flagDefs)
        {
            var flag = (StashInstance)flagObj!;
            string name = GetArgDefName(flag);
            flagsByLong[$"--{name}"] = flag;
            string? shortName = flag.GetField("short", null) as string;
            if (shortName is not null)
            {
                flagsByShort[$"-{shortName}"] = flag;
            }
        }
        foreach (var optObj in optionDefs)
        {
            var opt = (StashInstance)optObj!;
            string name = GetArgDefName(opt);
            optionsByLong[$"--{name}"] = opt;
            string? shortName = opt.GetField("short", null) as string;
            if (shortName is not null)
            {
                optionsByShort[$"-{shortName}"] = opt;
            }
        }
        foreach (var cmdObj in commandDefs)
        {
            var cmd = (StashInstance)cmdObj!;
            commandsByName[GetArgDefName(cmd)] = cmd;
        }

        // Parse _scriptArgs
        int positionalIndex = 0;
        StashInstance? activeCommand = null;
        string? activeCommandName = null;

        // Build per-command lookup maps
        var cmdFlagsByLong = new Dictionary<string, Dictionary<string, StashInstance>>();
        var cmdFlagsByShort = new Dictionary<string, Dictionary<string, StashInstance>>();
        var cmdOptionsByLong = new Dictionary<string, Dictionary<string, StashInstance>>();
        var cmdOptionsByShort = new Dictionary<string, Dictionary<string, StashInstance>>();
        var cmdPositionalDefs = new Dictionary<string, List<StashInstance>>();
        var cmdPositionalIndices = new Dictionary<string, int>();

        foreach (var cmdObj in commandDefs)
        {
            var cmd = (StashInstance)cmdObj!;
            string cmdName = GetArgDefName(cmd);
            var subTree = cmd.GetField("args", null) as StashInstance;

            var cfl = new Dictionary<string, StashInstance>();
            var cfs = new Dictionary<string, StashInstance>();
            var col = new Dictionary<string, StashInstance>();
            var cos = new Dictionary<string, StashInstance>();
            var cpos = new List<StashInstance>();
            cmdPositionalIndices[cmdName] = 0;

            if (subTree is not null)
            {
                var subFlags = subTree.GetField("flags", null) as List<object?> ?? new();
                var subOpts = subTree.GetField("options", null) as List<object?> ?? new();
                var subPos = subTree.GetField("positionals", null) as List<object?> ?? new();

                foreach (var f in subFlags)
                {
                    var fd = (StashInstance)f!;
                    string n = GetArgDefName(fd);
                    cfl[$"--{n}"] = fd;
                    string? s = fd.GetField("short", null) as string;
                    if (s is not null)
                    {
                        cfs[$"-{s}"] = fd;
                    }
                }
                foreach (var o in subOpts)
                {
                    var od = (StashInstance)o!;
                    string n = GetArgDefName(od);
                    col[$"--{n}"] = od;
                    string? s = od.GetField("short", null) as string;
                    if (s is not null)
                    {
                        cos[$"-{s}"] = od;
                    }
                }
                foreach (var p in subPos)
                {
                    cpos.Add((StashInstance)p!);
                }
            }

            cmdFlagsByLong[cmdName] = cfl;
            cmdFlagsByShort[cmdName] = cfs;
            cmdOptionsByLong[cmdName] = col;
            cmdOptionsByShort[cmdName] = cos;
            cmdPositionalDefs[cmdName] = cpos;
        }

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
            if (activeCommand is not null && activeCommandName is not null)
            {
                var cmdInstance = (StashInstance)fields[activeCommandName]!;

                if (cmdFlagsByLong[activeCommandName].TryGetValue(arg, out var cmdFlag))
                {
                    cmdInstance.SetField(GetArgDefName(cmdFlag), true, null);
                    i++;
                    continue;
                }
                if (cmdFlagsByShort[activeCommandName].TryGetValue(arg, out cmdFlag))
                {
                    cmdInstance.SetField(GetArgDefName(cmdFlag), true, null);
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
                    string? optType = cmdOpt.GetField("type", null) as string;
                    cmdInstance.SetField(GetArgDefName(cmdOpt), CoerceArgValue(val, optType, arg), null);
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
                    string? optType = cmdOpt.GetField("type", null) as string;
                    cmdInstance.SetField(GetArgDefName(cmdOpt), CoerceArgValue(val, optType, arg), null);
                    i++;
                    continue;
                }

                // Check command-level positionals
                int cmdPosIdx = cmdPositionalIndices[activeCommandName];
                if (!arg.StartsWith("-") && cmdPosIdx < cmdPositionalDefs[activeCommandName].Count)
                {
                    var cp = cmdPositionalDefs[activeCommandName][cmdPosIdx];
                    string? posType = cp.GetField("type", null) as string;
                    cmdInstance.SetField(GetArgDefName(cp), CoerceArgValue(arg, posType, GetArgDefName(cp)), null);
                    cmdPositionalIndices[activeCommandName]++;
                    i++;
                    continue;
                }
            }

            // Top-level flag match
            if (flagsByLong.TryGetValue(arg, out var topFlag))
            {
                fields[GetArgDefName(topFlag)] = true;
                i++;
                continue;
            }
            if (flagsByShort.TryGetValue(arg, out topFlag))
            {
                fields[GetArgDefName(topFlag)] = true;
                i++;
                continue;
            }

            // Top-level option match
            if (optionsByLong.TryGetValue(arg, out var topOpt))
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
                string? optType = topOpt.GetField("type", null) as string;
                fields[GetArgDefName(topOpt)] = CoerceArgValue(val, optType, arg);
                i++;
                continue;
            }
            if (optionsByShort.TryGetValue(arg, out topOpt))
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
                string? optType = topOpt.GetField("type", null) as string;
                fields[GetArgDefName(topOpt)] = CoerceArgValue(val, optType, arg);
                i++;
                continue;
            }

            // Command match
            if (!arg.StartsWith("-") && commandsByName.TryGetValue(arg, out var matchedCmd))
            {
                fields["command"] = arg;
                activeCommand = matchedCmd;
                activeCommandName = arg;
                i++;
                continue;
            }

            // Positional (only non-dash args when not matching a command)
            if (!arg.StartsWith("-") && positionalIndex < positionalDefs.Count)
            {
                var pos = (StashInstance)positionalDefs[positionalIndex]!;
                string? posType = pos.GetField("type", null) as string;
                fields[GetArgDefName(pos)] = CoerceArgValue(arg, posType, GetArgDefName(pos));
                positionalIndex++;
                i++;
                continue;
            }

            // Unknown argument
            throw new RuntimeError($"Unknown argument '{_scriptArgs[i]}'.");
        }

        // Auto-handle help flag
        if (fields.TryGetValue("help", out var helpVal) && helpVal is true)
        {
            PrintArgsHelp(tree, fields);
            System.Environment.Exit(0);
        }

        // Auto-handle version flag
        if (fields.TryGetValue("version", out var versionFlag) && versionFlag is true && version is not null)
        {
            Console.WriteLine(version);
            System.Environment.Exit(0);
        }

        // Validate required options
        foreach (var optObj in optionDefs)
        {
            var opt = (StashInstance)optObj!;
            string optName = GetArgDefName(opt);
            bool required = opt.GetField("required", null) is true;
            if (required && fields[optName] is null)
            {
                throw new RuntimeError($"Required option '--{optName}' was not provided.");
            }
        }

        // Validate required positionals
        foreach (var posObj in positionalDefs)
        {
            var pos = (StashInstance)posObj!;
            string posName = GetArgDefName(pos);
            bool required = pos.GetField("required", null) is true;
            if (required && fields[posName] is null)
            {
                throw new RuntimeError($"Required positional argument '{posName}' was not provided.");
            }
        }

        // Validate required command-level args if a command is active
        if (activeCommand is not null && activeCommandName is not null)
        {
            var subTree = activeCommand.GetField("args", null) as StashInstance;
            if (subTree is not null)
            {
                var cmdInstance = (StashInstance)fields[activeCommandName]!;
                var subOpts = subTree.GetField("options", null) as List<object?> ?? new();
                var subPos = subTree.GetField("positionals", null) as List<object?> ?? new();

                foreach (var optObj in subOpts)
                {
                    var opt = (StashInstance)optObj!;
                    string optName = GetArgDefName(opt);
                    bool required = opt.GetField("required", null) is true;
                    if (required && cmdInstance.GetField(optName, null) is null)
                    {
                        throw new RuntimeError($"Required option '--{optName}' for command '{activeCommandName}' was not provided.");
                    }
                }
                foreach (var posObj in subPos)
                {
                    var pos = (StashInstance)posObj!;
                    string posName = GetArgDefName(pos);
                    bool required = pos.GetField("required", null) is true;
                    if (required && cmdInstance.GetField(posName, null) is null)
                    {
                        throw new RuntimeError($"Required positional argument '{posName}' for command '{activeCommandName}' was not provided.");
                    }
                }
            }
        }

        return new StashInstance("Args", fields);
    }

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

    /// <summary>
    /// Validates that an object from an ArgTree list is an ArgDef StashInstance.
    /// </summary>
    private static StashInstance CastArgDef(object? obj, string listName)
    {
        if (obj is not StashInstance inst || inst.TypeName != "ArgDef")
        {
            throw new RuntimeError($"All entries in ArgTree '{listName}' must be ArgDef instances.");
        }
        return inst;
    }

    /// <summary>
    /// Gets the 'name' field from an ArgDef instance. Throws if null.
    /// </summary>
    private static string GetArgDefName(StashInstance argDef)
    {
        if (argDef.GetField("name", null) is not string name || name == "")
        {
            throw new RuntimeError("ArgDef 'name' field is required and must be a non-empty string.");
        }
        return name;
    }

    private void PrintArgsHelp(StashInstance tree, Dictionary<string, object?> fields)
    {
        var sb = new System.Text.StringBuilder();

        string? scriptName = tree.GetField("name", null) as string;
        string? version = tree.GetField("version", null) as string;
        string? description = tree.GetField("description", null) as string;
        var flagDefs = tree.GetField("flags", null) as List<object?> ?? new();
        var optionDefs = tree.GetField("options", null) as List<object?> ?? new();
        var commandDefs = tree.GetField("commands", null) as List<object?> ?? new();
        var positionalDefs = tree.GetField("positionals", null) as List<object?> ?? new();

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
        if (commandDefs.Count > 0)
        {
            sb.Append(" [command]");
        }

        if (optionDefs.Count > 0 || flagDefs.Count > 0)
        {
            sb.Append(" [options]");
        }

        foreach (var posObj in positionalDefs)
        {
            var pos = (StashInstance)posObj!;
            string posName = (string)pos.GetField("name", null)!;
            bool required = pos.GetField("required", null) is true;
            if (required)
            {
                sb.Append($" <{posName}>");
            }
            else
            {
                sb.Append($" [{posName}]");
            }
        }
        sb.AppendLine();
        sb.AppendLine();

        // Commands
        if (commandDefs.Count > 0)
        {
            sb.AppendLine("COMMANDS:");
            int maxCmdLen = 0;
            foreach (var cmdObj in commandDefs)
            {
                var cmd = (StashInstance)cmdObj!;
                string cmdName = (string)cmd.GetField("name", null)!;
                if (cmdName.Length > maxCmdLen)
                {
                    maxCmdLen = cmdName.Length;
                }
            }
            foreach (var cmdObj in commandDefs)
            {
                var cmd = (StashInstance)cmdObj!;
                string cmdName = (string)cmd.GetField("name", null)!;
                string? cmdDesc = cmd.GetField("description", null) as string;
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
        if (positionalDefs.Count > 0)
        {
            sb.AppendLine("ARGUMENTS:");
            int maxPosLen = 0;
            foreach (var posObj in positionalDefs)
            {
                var pos = (StashInstance)posObj!;
                string posName = (string)pos.GetField("name", null)!;
                bool required = pos.GetField("required", null) is true;
                string label = required ? $"<{posName}>" : $"[{posName}]";
                if (label.Length > maxPosLen)
                {
                    maxPosLen = label.Length;
                }
            }
            foreach (var posObj in positionalDefs)
            {
                var pos = (StashInstance)posObj!;
                string posName = (string)pos.GetField("name", null)!;
                bool required = pos.GetField("required", null) is true;
                string? posDesc = pos.GetField("description", null) as string;
                object? posDefault = pos.GetField("default", null);
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
        if (flagDefs.Count > 0 || optionDefs.Count > 0)
        {
            sb.AppendLine("OPTIONS:");
            var optLines = new List<(string Left, string? Right)>();

            foreach (var flagObj in flagDefs)
            {
                var flag = (StashInstance)flagObj!;
                string flagName = (string)flag.GetField("name", null)!;
                string? shortName = flag.GetField("short", null) as string;
                string? flagDesc = flag.GetField("description", null) as string;
                string left;
                if (shortName is not null)
                {
                    left = $"-{shortName}, --{flagName}";
                }
                else
                {
                    left = $"    --{flagName}";
                }

                optLines.Add((left, flagDesc));
            }

            foreach (var optObj in optionDefs)
            {
                var opt = (StashInstance)optObj!;
                string optName = (string)opt.GetField("name", null)!;
                string? shortName = opt.GetField("short", null) as string;
                string? optType = opt.GetField("type", null) as string;
                string? optDesc = opt.GetField("description", null) as string;
                object? optDefault = opt.GetField("default", null);
                bool required = opt.GetField("required", null) is true;

                string typeHint = optType is not null ? $" <{optType}>" : " <value>";
                string left;
                if (shortName is not null)
                {
                    left = $"-{shortName}, --{optName}{typeHint}";
                }
                else
                {
                    left = $"    --{optName}{typeHint}";
                }

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
        foreach (var cmdObj in commandDefs)
        {
            var cmd = (StashInstance)cmdObj!;
            string cmdName = (string)cmd.GetField("name", null)!;
            var subTree = cmd.GetField("args", null) as StashInstance;
            if (subTree is null)
            {
                continue;
            }

            var subFlags = subTree.GetField("flags", null) as List<object?> ?? new();
            var subOpts = subTree.GetField("options", null) as List<object?> ?? new();
            var subPos = subTree.GetField("positionals", null) as List<object?> ?? new();

            if (subFlags.Count == 0 && subOpts.Count == 0 && subPos.Count == 0)
            {
                continue;
            }

            sb.AppendLine($"COMMAND '{cmdName}':");

            if (subPos.Count > 0)
            {
                foreach (var posObj in subPos)
                {
                    var pos = (StashInstance)posObj!;
                    string posName = (string)pos.GetField("name", null)!;
                    bool required = pos.GetField("required", null) is true;
                    string? posDesc = pos.GetField("description", null) as string;
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
            foreach (var flagObj in subFlags)
            {
                var flag = (StashInstance)flagObj!;
                string flagName = (string)flag.GetField("name", null)!;
                string? shortName = flag.GetField("short", null) as string;
                string? flagDesc = flag.GetField("description", null) as string;
                string left;
                if (shortName is not null)
                {
                    left = $"-{shortName}, --{flagName}";
                }
                else
                {
                    left = $"    --{flagName}";
                }

                cmdOptLines.Add((left, flagDesc));
            }
            foreach (var optObj in subOpts)
            {
                var opt = (StashInstance)optObj!;
                string optName = (string)opt.GetField("name", null)!;
                string? shortName = opt.GetField("short", null) as string;
                string? optType = opt.GetField("type", null) as string;
                string? optDesc = opt.GetField("description", null) as string;
                object? optDefault = opt.GetField("default", null);
                bool required = opt.GetField("required", null) is true;
                string typeHint = optType is not null ? $" <{optType}>" : " <value>";
                string left;
                if (shortName is not null)
                {
                    left = $"-{shortName}, --{optName}{typeHint}";
                }
                else
                {
                    left = $"    --{optName}{typeHint}";
                }

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

        Console.Write(sb.ToString());
    }
}
