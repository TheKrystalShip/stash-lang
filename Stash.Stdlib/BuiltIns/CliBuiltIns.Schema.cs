namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Runtime.Errors;

// Partial class that holds the BuildSchema() validation method called from cli.schema().
public static partial class CliBuiltIns
{
    /// <summary>
    /// Validates a definition dict and constructs a <c>CliSchema</c> StashInstance.
    /// Validation rules (all throw ValueError in P1 — rewired to CliSchemaError in P2):
    ///   - unknown-type-tag                (already validated per spec in builders; redundant but defensive)
    ///   - duplicate short option           (two specs with the same short char)
    ///   - duplicate long option            (two specs map to the same --long-name)
    ///   - repeated-positional-not-last     (a positional with repeated:true is not the last positional)
    ///   - default-fails-conversion         (default value cannot be converted via the declared type tag)
    ///   - --help / -h shadowing            (when helpFlag is enabled)
    /// </summary>
    internal static StashValue BuildSchema(
        StashDictionary def,
        string programName,
        string description,
        bool helpFlag)
    {
        var positionals = new List<StashValue>();
        var options = new StashDictionary();
        StashValue commandSpec = StashValue.Null;

        // Tracking for duplicate detection
        var seenShorts = new Dictionary<string, string>(StringComparer.Ordinal);    // short -> property name
        var seenLongs = new Dictionary<string, string>(StringComparer.Ordinal);     // long  -> property name

        // Reserve --help / -h when helpFlag is on
        if (helpFlag)
        {
            seenShorts["h"] = "--help (implicit)";
            seenLongs["help"] = "--help (implicit)";
        }

        foreach (object rawKey in def.RawKeys())
        {
            string propName = rawKey?.ToString() ?? "";
            StashValue entry = def.Get(rawKey!);

            if (!entry.IsObj) continue;

            // ── CliCommandSpec ─────────────────────────────────────────────
            if (entry.AsObj is StashInstance cmdInstCheck && cmdInstCheck.TypeName == "CliCommandSpec")
            {
                if (!commandSpec.IsNull)
                    throw new CliSchemaError(
                        "'cli.schema': only one cli.command() entry is allowed per schema.",
                        field: propName,
                        reason: "duplicate command spec");
                commandSpec = entry;
                continue;
            }

            // ── CliArgSpec ─────────────────────────────────────────────────
            if (entry.AsObj is not StashInstance specInst || specInst.TypeName != "CliArgSpec")
                throw new TypeError($"'cli.schema': value for key '{propName}' must be a CliArgSpec or CliCommandSpec.");

            // Read fields from the StashInstance
            string kind    = GetStringField(specInst, "kind", propName);
            string typeTag = GetStringField(specInst, "typeTag", propName);
            StashValue nameField = specInst.GetField("name", null);
            string? nameOverride = nameField.IsObj && nameField.AsObj is string ns ? ns : null;
            StashValue shortField = specInst.GetField("short", null);
            string? shortOpt = shortField.IsObj && shortField.AsObj is string ss ? ss : null;
            StashValue defaultVal = specInst.GetField("defaultVal", null);

            // Determine the CLI-facing long name
            string longName = nameOverride ?? ToKebabCase(propName);

            // Check for --help / -h shadowing
            if (helpFlag && longName == "help")
                throw new CliSchemaError(
                    $"'cli.schema': option '{propName}' shadows the reserved '--help' flag. Set helpFlag: false to disable the implicit help flag.",
                    field: propName,
                    reason: "shadows --help");
            if (helpFlag && shortOpt == "h")
                throw new CliSchemaError(
                    $"'cli.schema': option '{propName}' shadows the reserved '-h' (help) short flag. Set helpFlag: false or use a different short.",
                    field: propName,
                    reason: "shadows -h");

            // ── Duplicate long name detection ──────────────────────────────
            if (seenLongs.TryGetValue(longName, out string? prevLong))
                throw new CliSchemaError(
                    $"'cli.schema': duplicate long option '--{longName}' from both '{prevLong}' and '{propName}'.",
                    field: propName,
                    reason: $"duplicate long option '--{longName}'");
            seenLongs[longName] = propName;

            // ── Duplicate short option detection ──────────────────────────
            if (shortOpt is not null)
            {
                if (seenShorts.TryGetValue(shortOpt, out string? prevShort))
                    throw new CliSchemaError(
                        $"'cli.schema': duplicate short option '-{shortOpt}' from both '{prevShort}' and '{propName}'.",
                        field: propName,
                        reason: $"duplicate short option '-{shortOpt}'");
                seenShorts[shortOpt] = propName;
            }

            // ── Default value conversion check ────────────────────────────
            if (!defaultVal.IsNull)
                ValidateDefault(defaultVal, typeTag, propName);

            // ── Write resolved long name back onto a new StashInstance ─────
            // Build a fresh copy of the spec dict with the resolved name and propName filled in.
            StashValue resolvedSpec = ReplaceNameField(specInst, longName, propName);

            if (kind == "positional")
                positionals.Add(resolvedSpec);
            else
                options.Set(propName, resolvedSpec);
        }

        // ── repeated-positional-not-last check ────────────────────────────
        for (int i = 0; i < positionals.Count - 1; i++)
        {
            StashValue posVal = positionals[i];
            if (posVal.IsObj && posVal.AsObj is StashInstance pos)
            {
                StashValue repeatedVal = pos.GetField("repeated", null);
                string posName = pos.GetField("name", null) is { IsObj: true } nf && nf.AsObj is string ns2 ? ns2 : $"positional[{i}]";
                if (repeatedVal.IsBool && repeatedVal.AsBool)
                    throw new CliSchemaError(
                        "'cli.schema': a positional with repeated: true must be the last positional argument.",
                        field: posName,
                        reason: "repeated positional is not last");
            }
        }

        // Build the CliSchema StashInstance
        var schemaDict = new Dictionary<string, StashValue>
        {
            ["positionals"] = StashValue.FromObj(positionals),
            ["options"]     = StashValue.FromObj(options),
            ["command"]     = commandSpec,
            ["programName"] = StashValue.FromObj(programName),
            ["description"] = StashValue.FromObj(description),
            ["helpFlag"]    = StashValue.FromBool(helpFlag),
        };

        return StashValue.FromObj(new StashInstance("CliSchema", schemaDict));
    }

    /// <summary>
    /// Copies a CliArgSpec StashInstance, replacing the <c>name</c> field with the resolved long name
    /// and recording the Stash property name (dict key) as <c>propName</c>.
    /// The <c>propName</c> field is used by the parsing engine to populate the result dict.
    /// </summary>
    internal static StashValue ReplaceNameField(StashInstance specInst, string longName, string propName = "")
    {
        // We rebuild the dict from the known CliArgSpec fields.
        var fields = new Dictionary<string, StashValue>
        {
            ["kind"]        = specInst.GetField("kind",      null),
            ["typeTag"]     = specInst.GetField("typeTag",   null),
            ["name"]        = StashValue.FromObj(longName),
            // propName is the Stash-side property key (camelCase dict key from cli.schema() definition).
            // The parsing engine uses this to populate the result dict with the correct key.
            ["propName"]    = StashValue.FromObj(propName),
            ["short"]       = specInst.GetField("short",     null),
            ["aliases"]     = specInst.GetField("aliases",   null),
            ["required"]    = specInst.GetField("required",  null),
            ["defaultVal"]  = specInst.GetField("defaultVal", null),
            ["repeated"]    = specInst.GetField("repeated",  null),
            ["choices"]     = specInst.GetField("choices",   null),
            ["min"]         = specInst.GetField("min",       null),
            ["max"]         = specInst.GetField("max",       null),
            ["pattern"]     = specInst.GetField("pattern",   null),
            ["validate"]    = specInst.GetField("validate",  null),
            ["help"]        = specInst.GetField("help",      null),
            ["metavar"]     = specInst.GetField("metavar",   null),
            ["env"]         = specInst.GetField("env",       null),
            ["negatable"]   = specInst.GetField("negatable", null),
        };
        return StashValue.FromObj(new StashInstance("CliArgSpec", fields));
    }

    private static string GetStringField(StashInstance inst, string field, string propName)
    {
        StashValue v = inst.GetField(field, null);
        if (v.IsObj && v.AsObj is string s) return s;
        throw new TypeError($"'cli.schema': internal error — '{propName}'.{field} is not a string.");
    }

    /// <summary>
    /// Validates that a default value can be converted by the declared type tag.
    /// Raises CliSchemaError when the default is incompatible with the declared type tag.
    /// </summary>
    private static void ValidateDefault(StashValue defaultValue, string typeTag, string propName)
    {
        switch (typeTag)
        {
            case "string":
                if (!(defaultValue.IsObj && defaultValue.AsObj is string))
                    throw new CliSchemaError(
                        $"'cli.schema': default for '{propName}' cannot be used as type 'string'.",
                        field: propName,
                        reason: "default is not a string");
                break;

            case "int":
                if (!defaultValue.IsInt)
                {
                    if (defaultValue.IsObj && defaultValue.AsObj is string strInt)
                    {
                        if (!long.TryParse(strInt, out _))
                            throw new CliSchemaError(
                                $"'cli.schema': default for '{propName}' cannot be converted to type 'int'.",
                                field: propName,
                                reason: "default cannot be converted to int");
                    }
                    else
                    {
                        throw new CliSchemaError(
                            $"'cli.schema': default for '{propName}' cannot be used as type 'int'.",
                            field: propName,
                            reason: "default is not an int");
                    }
                }
                break;

            case "float":
                if (!defaultValue.IsFloat && !defaultValue.IsInt)
                {
                    if (defaultValue.IsObj && defaultValue.AsObj is string strFloat)
                    {
                        if (!double.TryParse(strFloat, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                            throw new CliSchemaError(
                                $"'cli.schema': default for '{propName}' cannot be converted to type 'float'.",
                                field: propName,
                                reason: "default cannot be converted to float");
                    }
                    else
                    {
                        throw new CliSchemaError(
                            $"'cli.schema': default for '{propName}' cannot be used as type 'float'.",
                            field: propName,
                            reason: "default is not a float");
                    }
                }
                break;

            case "bool":
                if (!defaultValue.IsBool)
                {
                    if (defaultValue.IsObj && defaultValue.AsObj is string strBool)
                    {
                        string lower = strBool.ToLowerInvariant();
                        if (lower != "true" && lower != "false" &&
                            lower != "1" && lower != "0" &&
                            lower != "yes" && lower != "no")
                            throw new CliSchemaError(
                                $"'cli.schema': default for '{propName}' cannot be converted to type 'bool'.",
                                field: propName,
                                reason: "default cannot be converted to bool");
                    }
                    else
                    {
                        throw new CliSchemaError(
                            $"'cli.schema': default for '{propName}' cannot be used as type 'bool'.",
                            field: propName,
                            reason: "default is not a bool");
                    }
                }
                break;

            // For duration, ip, bytesize, semver: only validate that the default is a string at schema time.
            // Full conversion is validated in P3 when actual argument parsing occurs.
            case "duration":
            case "ip":
            case "bytesize":
            case "semver":
                if (!(defaultValue.IsObj && defaultValue.AsObj is string))
                    throw new CliSchemaError(
                        $"'cli.schema': default for '{propName}' with type '{typeTag}' must be a string.",
                        field: propName,
                        reason: $"default for type '{typeTag}' must be a string");
                break;
        }
    }

    /// <summary>Converts a camelCase or PascalCase property name to kebab-case.</summary>
    internal static string ToKebabCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;

        var sb = new StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0) sb.Append('-');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
