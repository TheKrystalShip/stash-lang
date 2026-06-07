namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Runtime.Errors;
using Stash.Stdlib.Abstractions;

// cli.build — inverse of cli.parse.
// Given a CliSchema and a values dict (as returned by cli.parse / cli.tryParse),
// produces an argv string array that round-trips through cli.parse for round-trippable schemas.
//
// Emission rules:
//   Options:  emitted as --name=value (long form with =). Each key in values is looked up in
//             the schema; values that fail type/choices/constraint validation raise CliSchemaError.
//   Flags:    emitted only when value differs from the spec default.
//             true (default false)  → --name
//             false (default true)  → --no-name (only when negatable:true; otherwise CliSchemaError)
//   Positionals: emitted after all options, in declaration order.
//             If the positional value starts with '-', a '--' separator is prepended.
//   Subcommands: if values contains a "command" key holding a CliCommand instance, the root
//             options are emitted first, then command.name, then the sub-values are recursed.
public static partial class CliBuiltIns
{
    // ── cli.build ──────────────────────────────────────────────────────────────

    /// <summary>Renders a values dict back to an argv string array. Round-trips for round-trippable schemas.</summary>
    /// <param name="schema">A CliSchema built by cli.schema()</param>
    /// <param name="values">The values dict to serialize (as returned by cli.parse or cli.tryParse)</param>
    /// <exception cref="TypeError">if schema is not a CliSchema or values is not a dict</exception>
    /// <exception cref="CliSchemaError">if a value cannot be serialized for the declared spec (type mismatch, choices violation, constraint violation)</exception>
    /// <returns>array&lt;string&gt; suitable for passing to cli.parse / cli.tryParse</returns>
    [StashFn(Raw = true, ReturnType = "array")]
    private static StashValue Build(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        if (args.Length < 2)
            throw new TypeError("'cli.build' requires 2 arguments (schema, values).");

        if (!args[0].IsObj || args[0].AsObj is not StashInstance schemaInst || schemaInst.TypeName != "CliSchema")
            throw new TypeError("'cli.build': first argument must be a CliSchema (returned by cli.schema()).");

        if (!args[1].IsObj || args[1].AsObj is not StashDictionary valuesDict)
            throw new TypeError("'cli.build': second argument must be a dict of values.");

        var argv = new List<string>();
        BuildArgv(schemaInst, valuesDict, argv, ctx);

        return StashValue.FromObj(argv.Select(StashValue.FromObj).ToList<StashValue>());
    }

    // ── cli.argv ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The raw script argv as supplied by the host. Cached on first access; the returned
    /// array is frozen — assignment into it raises a frozen-write error.
    /// </summary>
    [StashMember(ReturnType = "array")]
    private static List<StashValue> Argv(IInterpreterContext ctx)
    {
        var result = new List<StashValue>();
        foreach (string s in ctx.ScriptArgs ?? Array.Empty<string>())
            result.Add(StashValue.FromObj(s));
        return result;
    }

    // ── cli.argc ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The number of raw script arguments. Cached on first access and stable for the
    /// process lifetime.
    /// </summary>
    [StashMember(ReturnType = "int")]
    private static long Argc(IInterpreterContext ctx)
    {
        return (long)(ctx.ScriptArgs?.Length ?? 0);
    }

    // ── Internal build implementation ─────────────────────────────────────────

    /// <summary>
    /// Core build logic. Serializes values from valuesDict against schemaInst into argv tokens.
    /// </summary>
    private static void BuildArgv(StashInstance schemaInst, StashDictionary valuesDict, List<string> argv, IInterpreterContext ctx)
    {
        List<StashValue> positionals = GetListField(schemaInst, "positionals");
        StashDictionary optionsDict = GetDictField(schemaInst, "options");
        StashValue commandSpecVal = schemaInst.GetField("command", null);

        bool hasSubcommands = !commandSpecVal.IsNull &&
                              commandSpecVal.IsObj &&
                              commandSpecVal.AsObj is StashInstance cs &&
                              cs.TypeName == "CliCommandSpec";

        // Emit options and flags (keyed by propName in optionsDict)
        foreach (StashValue rawKey in optionsDict.RawKeys())
        {
            string propName = rawKey.ToObject()?.ToString() ?? "";
            StashValue specSv = optionsDict.Get(rawKey);
            if (!specSv.IsObj || specSv.AsObj is not StashInstance spec) continue;

            string kind = GetStringFieldOrEmpty(spec, "kind");
            string longName = GetStringFieldOrEmpty(spec, "name");
            bool isRepeated = GetBoolFieldOrFalse(spec, "repeated");

            if (!valuesDict.Has(propName)) continue;
            StashValue value = valuesDict.Get(propName);

            if (kind == "flag")
            {
                EmitFlag(spec, longName, value, argv);
            }
            else
            {
                // option
                if (isRepeated && value.IsObj && value.AsObj is List<StashValue> items)
                {
                    foreach (StashValue item in items)
                    {
                        ValidateBuildValue(spec, item, propName, ctx);
                        argv.Add($"--{longName}={SerializeValue(item, GetStringFieldOrEmpty(spec, "typeTag"))}");
                    }
                }
                else
                {
                    ValidateBuildValue(spec, value, propName, ctx);
                    argv.Add($"--{longName}={SerializeValue(value, GetStringFieldOrEmpty(spec, "typeTag"))}");
                }
            }
        }

        // If subcommands exist, emit the subcommand name + recurse into sub-schema
        if (hasSubcommands)
        {
            if (!valuesDict.Has("command")) return;
            StashValue commandVal = valuesDict.Get("command");
            if (!commandVal.IsObj || commandVal.AsObj is not StashInstance commandInst ||
                commandInst.TypeName != "CliCommand") return;

            string subName = GetStringFieldOrEmpty(commandInst, "name");
            argv.Add(subName);

            // Resolve leaf schema
            StashInstance cmdSpecInst = (StashInstance)commandSpecVal.AsObj!;
            StashValue commandsVal = cmdSpecInst.GetField("commands", null);
            if (commandsVal.IsObj && commandsVal.AsObj is StashDictionary commandsDict && commandsDict.Has(subName))
            {
                StashValue leafSchemaVal = commandsDict.Get(subName);
                if (leafSchemaVal.IsObj && leafSchemaVal.AsObj is StashInstance leafSchema)
                {
                    StashValue subValuesVal = commandInst.GetField("values", null);
                    if (subValuesVal.IsObj && subValuesVal.AsObj is StashDictionary subValues)
                        BuildArgv(leafSchema, subValues, argv, ctx);
                }
            }
            return; // positionals belong to the subcommand, not root
        }

        // Emit positionals in declaration order (after all options)
        // Guard for positionals that might start with '-' by using -- separator
        bool needsSeparator = false;
        foreach (StashValue specSv in positionals)
        {
            if (!specSv.IsObj || specSv.AsObj is not StashInstance spec) continue;
            string propName = GetStringFieldOrEmpty(spec, "propName");
            if (string.IsNullOrEmpty(propName) || !valuesDict.Has(propName)) continue;
            StashValue value = valuesDict.Get(propName);
            bool isRepeated = GetBoolFieldOrFalse(spec, "repeated");

            if (isRepeated && value.IsObj && value.AsObj is List<StashValue> items)
            {
                foreach (StashValue item in items)
                {
                    string raw = SerializeValue(item, GetStringFieldOrEmpty(spec, "typeTag"));
                    if (!needsSeparator && raw.StartsWith('-'))
                    {
                        argv.Add("--");
                        needsSeparator = true;
                    }
                    ValidateBuildValue(spec, item, propName, ctx);
                    argv.Add(raw);
                }
            }
            else
            {
                string raw = SerializeValue(value, GetStringFieldOrEmpty(spec, "typeTag"));
                if (!needsSeparator && raw.StartsWith('-'))
                {
                    argv.Add("--");
                    needsSeparator = true;
                }
                ValidateBuildValue(spec, value, propName, ctx);
                argv.Add(raw);
            }
        }
    }

    /// <summary>
    /// Emits argv tokens for a flag argument. Flags are only emitted when value differs from default.
    /// </summary>
    private static void EmitFlag(StashInstance spec, string longName, StashValue value, List<string> argv)
    {
        // Read the flag's default (usually false)
        StashValue defaultVal = spec.GetField("defaultVal", null);
        bool defaultBool = defaultVal.IsBool ? defaultVal.AsBool : false;
        bool currentBool = value.IsBool ? value.AsBool : false;

        if (currentBool == defaultBool)
            return; // no difference from default, skip

        if (currentBool)
        {
            // Value is true, default is false → emit --name
            argv.Add($"--{longName}");
        }
        else
        {
            // Value is false, default is true → need --no-name (requires negatable:true)
            bool negatable = GetBoolFieldOrFalse(spec, "negatable");
            if (!negatable)
                throw new CliSchemaError(
                    $"'cli.build': flag '--{longName}' has value false but default true, and is not negatable. Cannot round-trip this value.",
                    field: longName,
                    reason: "flag is not negatable but value differs from default true");
            argv.Add($"--no-{longName}");
        }
    }

    /// <summary>
    /// Validates a value against the spec's type tag, choices, and constraints before serializing.
    /// Raises CliSchemaError on mismatch.
    /// </summary>
    private static void ValidateBuildValue(StashInstance spec, StashValue value, string propName, IInterpreterContext ctx)
    {
        string typeTag = GetStringFieldOrEmpty(spec, "typeTag");

        // Type check
        if (!ValueMatchesTypeTag(value, typeTag))
            throw new CliSchemaError(
                $"'cli.build': value for '{propName}' cannot be serialized as type '{typeTag}'.",
                field: propName,
                reason: $"value is not compatible with type tag '{typeTag}'");

        // Choices check
        StashValue choicesVal = spec.GetField("choices", null);
        if (!choicesVal.IsNull && choicesVal.IsObj && choicesVal.AsObj is List<StashValue> choices)
        {
            bool found = choices.Any(c => StashValuesEqual(value, c));
            if (!found)
            {
                string expected = string.Join(", ", choices.Select(c => RuntimeValues.Stringify(c.ToObject())));
                throw new CliSchemaError(
                    $"'cli.build': value for '{propName}' is not in the allowed choices: {expected}.",
                    field: propName,
                    reason: $"value not in choices: {expected}");
            }
        }

        // Constraints check (min/max/pattern) — same rules as parse-time ApplyConstraints
        string rawForValidation = SerializeValue(value, typeTag);
        try
        {
            ApplyConstraints(spec, value, rawForValidation, propName, ctx);
        }
        catch (CliValidationFailed ex)
        {
            throw new CliSchemaError(
                $"'cli.build': value for '{propName}' fails constraint: {ex.Message}",
                field: propName,
                reason: ex.Message);
        }
    }

    /// <summary>Returns true when the StashValue is compatible with the given type tag.</summary>
    private static bool ValueMatchesTypeTag(StashValue value, string typeTag)
    {
        return typeTag switch
        {
            "string"   => value.IsObj && value.AsObj is string,
            "int"      => value.IsInt,
            "float"    => value.IsFloat || value.IsInt,
            "bool"     => value.IsBool,
            "duration" => value.IsObj && value.AsObj is StashDuration,
            "ip"       => value.IsObj && value.AsObj is StashIpAddress,
            "bytesize" => value.IsObj && value.AsObj is StashByteSize,
            "semver"   => value.IsObj && value.AsObj is StashSemVer,
            _          => false,
        };
    }

    /// <summary>Converts a StashValue to its canonical string representation for argv emission.</summary>
    private static string SerializeValue(StashValue value, string typeTag)
    {
        if (value.IsInt)   return value.AsInt.ToString(CultureInfo.InvariantCulture);
        if (value.IsFloat) return value.AsFloat.ToString("G", CultureInfo.InvariantCulture);
        if (value.IsBool)  return value.AsBool ? "true" : "false";
        if (value.IsObj)
        {
            return value.AsObj switch
            {
                string s          => s,
                StashDuration d   => d.ToString(),
                StashIpAddress ip => ip.ToString(),
                StashByteSize bs  => bs.ToString(),
                StashSemVer sv    => sv.ToString(),
                _                 => RuntimeValues.Stringify(value.ToObject()),
            };
        }
        return "";
    }
}
