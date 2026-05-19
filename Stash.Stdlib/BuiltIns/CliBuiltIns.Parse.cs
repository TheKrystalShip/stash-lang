namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.Linq;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Runtime.Errors;
using Stash.Stdlib.Abstractions;

// Parsing engine for cli.tryParse — P3.
// Handles positionals, options, flags, repeated, choices, defaults, env fallback, -- boundary,
// short-flag bundling (-abc), and -nVALUE short option with inline value.
//
// Not implemented in P3 (deferred phases noted inline):
//   - Subcommand dispatch           → P4
//   - min/max/pattern/validate      → P5 (accept but ignore with TODO comments)
//   - cli.help / cli.printHelp      → P6
//   - cli.parse exit wrapper        → P7
public static partial class CliBuiltIns
{
    // ── Help-requested sentinel ───────────────────────────────────────────────

    /// <summary>
    /// Thrown internally by RunParser when --help / -h is detected.
    /// Caught by TryParse and converted to a CliParseResult with helpRequested:true
    /// (ok:false, no value dict, no error). The spec doesn't prescribe ok's value for the
    /// help case; we choose ok:false to signal that parsing did not complete normally.
    /// </summary>
    private sealed class HelpRequestedException : Exception
    {
        public HelpRequestedException() : base("--help requested") { }
    }

    // ── cli.tryParse ──────────────────────────────────────────────────────────

    /// <summary>Parses an argv array against a CliSchema. Never exits; returns a CliParseResult.</summary>
    /// <param name="schema">A CliSchema built by cli.schema()</param>
    /// <param name="argv">Optional array of strings to parse (defaults to cli.argv())</param>
    /// <exception cref="TypeError">if schema is not a CliSchema</exception>
    /// <returns>CliParseResult with ok, value, error, and helpRequested fields</returns>
    [StashFn(Raw = true, ReturnType = "CliParseResult")]
    private static StashValue TryParse(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        if (args.Length < 1)
            throw new TypeError("'cli.tryParse' requires at least 1 argument (schema).");

        if (!args[0].IsObj || args[0].AsObj is not StashInstance schemaInst || schemaInst.TypeName != "CliSchema")
            throw new TypeError("'cli.tryParse': first argument must be a CliSchema (returned by cli.schema()).");

        // Resolve argv: explicit second argument, or fall back to ScriptArgs
        string[] argv;
        if (args.Length >= 2)
        {
            if (!args[1].IsObj || args[1].AsObj is not List<StashValue> argvList)
                throw new TypeError("'cli.tryParse': second argument must be an array of strings.");
            argv = new string[argvList.Count];
            for (int i = 0; i < argvList.Count; i++)
            {
                if (!argvList[i].IsObj || argvList[i].AsObj is not string s)
                    throw new TypeError("'cli.tryParse': argv elements must be strings.");
                argv[i] = s;
            }
        }
        else
        {
            argv = ctx.ScriptArgs ?? [];
        }

        try
        {
            StashDictionary parsed = RunParser(schemaInst, argv);
            return MakeParseResult(ok: true, value: parsed, error: StashValue.Null, helpRequested: false);
        }
        catch (HelpRequestedException)
        {
            // --help / -h detected: return helpRequested:true, ok:false, no error, no value
            return MakeParseResult(ok: false, value: new StashDictionary(), error: StashValue.Null, helpRequested: true);
        }
        catch (RuntimeError ex) when (ex is CliMissingRequired or CliUnknownOption or CliMissingValue
                                         or CliInvalidValue or CliUnexpectedPositional or CliAmbiguousOption
                                         or CliValidationFailed or CliUnknownCommand)
        {
            // Wrap the CLR exception as a Stash-accessible StashError so field access (.type, .name, etc.) works
            StashError stashErr = StashError.FromRuntimeError(ex, stackLines: null);
            return MakeParseResult(ok: false, value: new StashDictionary(), error: StashValue.FromObj(stashErr), helpRequested: false);
        }
    }

    // ── Parser core ───────────────────────────────────────────────────────────

    private static StashDictionary RunParser(StashInstance schemaInst, string[] argv)
    {
        // ── Read schema fields ─────────────────────────────────────────────
        bool helpFlag = GetBoolField(schemaInst, "helpFlag");

        List<StashValue> positionalSpecs = GetListField(schemaInst, "positionals");
        StashDictionary optionsDict = GetDictField(schemaInst, "options");

        // ── Build lookup tables ────────────────────────────────────────────
        // propName -> spec instance (for all options and flags)
        var specByPropName = new Dictionary<string, StashInstance>(StringComparer.Ordinal);
        // longName -> propName (the CLI-facing --foo name maps back to propName)
        var longToProp = new Dictionary<string, string>(StringComparer.Ordinal);
        // short char -> propName
        var shortToProp = new Dictionary<string, string>(StringComparer.Ordinal);
        // also track aliases: longAlias -> propName
        var aliasToProp = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (object rawKey in optionsDict.RawKeys())
        {
            string propKey = rawKey?.ToString() ?? "";
            StashValue sv = optionsDict.Get(rawKey!);
            if (!sv.IsObj || sv.AsObj is not StashInstance spec) continue;

            specByPropName[propKey] = spec;
            string longName = GetStringFieldOrEmpty(spec, "name");
            longToProp[longName] = propKey;

            string? shortOpt = GetStringFieldNullable(spec, "short");
            if (shortOpt is not null)
                shortToProp[shortOpt] = propKey;

            StashValue aliasesVal = spec.GetField("aliases", null);
            if (aliasesVal.IsObj && aliasesVal.AsObj is List<StashValue> aliases)
            {
                foreach (StashValue aliasV in aliases)
                {
                    if (aliasV.IsObj && aliasV.AsObj is string alias)
                        aliasToProp[alias] = propKey;
                }
            }
        }

        // ── Parse state ────────────────────────────────────────────────────
        // Accumulate values keyed by propName
        var values = new Dictionary<string, StashValue>(StringComparer.Ordinal);
        // Track which options/flags were actually supplied (for missing-required checks)
        var supplied = new HashSet<string>(StringComparer.Ordinal);
        // Positional consumption cursor
        int positionalCursor = 0;
        bool pastDoubleDash = false;

        // ── Main argv loop ─────────────────────────────────────────────────
        int i = 0;
        while (i < argv.Length)
        {
            string token = argv[i];
            i++;

            // ── Past -- boundary: all remaining tokens are positionals ──────
            if (pastDoubleDash)
            {
                ConsumePositional(token, positionalSpecs, ref positionalCursor, values, supplied);
                continue;
            }

            // ── The -- boundary token itself ────────────────────────────────
            if (token == "--")
            {
                pastDoubleDash = true;
                continue;
            }

            // ── Long option: --name, --name=value, --no-name ─────────────
            if (token.StartsWith("--", StringComparison.Ordinal))
            {
                string longPart = token[2..];

                // Help flag short-circuit
                if (helpFlag && longPart == "help")
                    throw new HelpRequestedException();

                // --name=value split
                string? inlineValue = null;
                int eqIdx = longPart.IndexOf('=');
                if (eqIdx >= 0)
                {
                    inlineValue = longPart[(eqIdx + 1)..];
                    longPart = longPart[..eqIdx];
                }

                // Detect --no-<name> negation early (before ResolveLongName)
                bool isNegation = false;
                string resolvedPropName;

                if (longPart.StartsWith("no-", StringComparison.Ordinal))
                {
                    string baseName = longPart[3..];
                    if (longToProp.TryGetValue(baseName, out string? negProp))
                    {
                        StashInstance negSpec = specByPropName[negProp];
                        if (GetStringFieldOrEmpty(negSpec, "kind") == "flag" && GetBoolFieldOrFalse(negSpec, "negatable"))
                        {
                            isNegation = true;
                            resolvedPropName = negProp;
                        }
                        else
                        {
                            // --no-<name> exists as a long name but the spec doesn't allow negation
                            // Try resolving --no-<name> as a normal long name first
                            resolvedPropName = ResolveLongName(longPart, longToProp, aliasToProp, token);
                        }
                    }
                    else
                    {
                        resolvedPropName = ResolveLongName(longPart, longToProp, aliasToProp, token);
                    }
                }
                else
                {
                    resolvedPropName = ResolveLongName(longPart, longToProp, aliasToProp, token);
                }

                StashInstance spec = specByPropName[resolvedPropName];
                string kind = GetStringFieldOrEmpty(spec, "kind");

                if (kind == "flag")
                {
                    if (isNegation)
                    {
                        if (inlineValue is not null)
                            throw new CliUnknownOption($"Flag '--{longPart}' does not accept a value.", option: $"--{longPart}");
                        SetValue(values, resolvedPropName, StashValue.False, spec, isRepeated: false);
                    }
                    else
                    {
                        if (inlineValue is not null)
                            throw new CliUnknownOption($"Flag '--{longPart}' does not accept a value.", option: $"--{longPart}");
                        SetValue(values, resolvedPropName, StashValue.True, spec, isRepeated: false);
                    }
                    supplied.Add(resolvedPropName);
                }
                else
                {
                    // Option: requires a value
                    string rawValue;
                    if (inlineValue is not null)
                    {
                        rawValue = inlineValue;
                    }
                    else if (i < argv.Length)
                    {
                        rawValue = argv[i];
                        i++;
                    }
                    else
                    {
                        throw new CliMissingValue($"Option '--{longPart}' requires a value.", option: $"--{longPart}");
                    }
                    string typeTag = GetStringFieldOrEmpty(spec, "typeTag");
                    bool isRepeated = GetBoolFieldOrFalse(spec, "repeated");
                    StashValue converted = ConvertValue(rawValue, typeTag, $"--{longPart}");
                    ValidateChoices(converted, rawValue, spec, $"--{longPart}");
                    // TODO(P5): apply min/max/pattern/validate here
                    SetValue(values, resolvedPropName, converted, spec, isRepeated);
                    supplied.Add(resolvedPropName);
                }
                continue;
            }

            // ── Short option: -x, -xVALUE, -abc (bundling) ───────────────
            if (token.StartsWith("-", StringComparison.Ordinal) && token.Length > 1)
            {
                // Help -h short-circuit
                if (helpFlag && token == "-h")
                    throw new HelpRequestedException();

                string shortsPart = token[1..];
                int si = 0;
                while (si < shortsPart.Length)
                {
                    string shortChar = shortsPart[si].ToString();
                    si++;

                    if (!shortToProp.TryGetValue(shortChar, out string? sPropName))
                        throw new CliUnknownOption($"Unknown option '-{shortChar}'.", option: $"-{shortChar}");

                    StashInstance sSpec = specByPropName[sPropName];
                    string sKind = GetStringFieldOrEmpty(sSpec, "kind");

                    if (sKind == "flag")
                    {
                        SetValue(values, sPropName, StashValue.True, sSpec, isRepeated: false);
                        supplied.Add(sPropName);
                    }
                    else
                    {
                        // Option: rest of current bundle token is the inline value (e.g. -nVALUE → -n FILE)
                        string rawValue;
                        if (si < shortsPart.Length)
                        {
                            // Remaining chars of the current argv token are the inline value
                            rawValue = shortsPart[si..];
                            si = shortsPart.Length; // consume rest of bundle
                        }
                        else if (i < argv.Length)
                        {
                            rawValue = argv[i];
                            i++;
                        }
                        else
                        {
                            string sLong = GetStringFieldOrEmpty(sSpec, "name");
                            throw new CliMissingValue($"Option '-{shortChar}' (--{sLong}) requires a value.", option: $"-{shortChar}");
                        }
                        string sTypeTag = GetStringFieldOrEmpty(sSpec, "typeTag");
                        bool sIsRepeated = GetBoolFieldOrFalse(sSpec, "repeated");
                        StashValue converted = ConvertValue(rawValue, sTypeTag, $"-{shortChar}");
                        ValidateChoices(converted, rawValue, sSpec, $"-{shortChar}");
                        // TODO(P5): apply min/max/pattern/validate here
                        SetValue(values, sPropName, converted, sSpec, sIsRepeated);
                        supplied.Add(sPropName);
                    }
                }
                continue;
            }

            // ── Positional token ──────────────────────────────────────────
            ConsumePositional(token, positionalSpecs, ref positionalCursor, values, supplied);
        }

        // ── Post-parse: apply env fallbacks then defaults ──────────────────
        ApplyEnvFallbacks(specByPropName, supplied, values);
        ApplyDefaults(specByPropName, positionalSpecs, supplied, values);

        // ── Missing-required checks ────────────────────────────────────────
        CheckMissingRequired(specByPropName, positionalSpecs, supplied, values);

        return BuildResultDict(specByPropName, positionalSpecs, values);
    }

    // ── Long name resolution ──────────────────────────────────────────────────

    private static string ResolveLongName(
        string longPart,
        Dictionary<string, string> longToProp,
        Dictionary<string, string> aliasToProp,
        string originalToken)
    {
        // Exact match wins first
        if (longToProp.TryGetValue(longPart, out string? exact))
            return exact;
        if (aliasToProp.TryGetValue(longPart, out string? aliasExact))
            return aliasExact;

        // Prefix match across all longs + aliases
        var allLongs = longToProp.Keys.Concat(aliasToProp.Keys)
            .Where(k => k.StartsWith(longPart, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (allLongs.Count == 0)
            throw new CliUnknownOption($"Unknown option '{originalToken}'.", option: originalToken);

        if (allLongs.Count > 1)
        {
            var candidateLongs = allLongs
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();
            throw new CliAmbiguousOption(
                $"Option '{originalToken}' is ambiguous; could be: {string.Join(", ", candidateLongs.Select(c => "--" + c))}.",
                option: originalToken,
                candidates: candidateLongs.Select(c => "--" + c).ToList());
        }

        string winner = allLongs[0];
        return longToProp.TryGetValue(winner, out string? winnerProp) ? winnerProp : aliasToProp[winner];
    }

    // ── Positional consumption ────────────────────────────────────────────────

    private static void ConsumePositional(
        string token,
        List<StashValue> positionalSpecs,
        ref int cursor,
        Dictionary<string, StashValue> values,
        HashSet<string> supplied)
    {
        // Check if we have a current positional spec at cursor
        if (cursor < positionalSpecs.Count)
        {
            StashValue specSv = positionalSpecs[cursor];
            if (!specSv.IsObj || specSv.AsObj is not StashInstance spec)
            {
                cursor++;
                return;
            }

            string sPropName = GetStringFieldOrEmpty(spec, "propName");
            string sTypeTag = GetStringFieldOrEmpty(spec, "typeTag");
            bool isRepeated = GetBoolFieldOrFalse(spec, "repeated");

            StashValue converted = ConvertValue(token, sTypeTag, sPropName);
            ValidateChoices(converted, token, spec, sPropName);
            // TODO(P5): apply min/max/pattern/validate here

            if (isRepeated)
            {
                AppendToList(values, sPropName, converted);
                supplied.Add(sPropName);
                // Don't advance cursor — repeated positional stays current and absorbs subsequent tokens
            }
            else
            {
                values[sPropName] = converted;
                supplied.Add(sPropName);
                cursor++;
            }
            return;
        }

        // cursor is past all specs — check if last spec is repeated trailing
        if (positionalSpecs.Count > 0)
        {
            StashValue lastSv = positionalSpecs[positionalSpecs.Count - 1];
            if (lastSv.IsObj && lastSv.AsObj is StashInstance lastSpec)
            {
                if (GetBoolFieldOrFalse(lastSpec, "repeated"))
                {
                    string propName = GetStringFieldOrEmpty(lastSpec, "propName");
                    string typeTag = GetStringFieldOrEmpty(lastSpec, "typeTag");
                    StashValue converted = ConvertValue(token, typeTag, propName);
                    ValidateChoices(converted, token, lastSpec, propName);
                    // TODO(P5): apply min/max/pattern/validate here
                    AppendToList(values, propName, converted);
                    supplied.Add(propName);
                    return;
                }
            }
        }

        throw new CliUnexpectedPositional($"Unexpected positional argument: '{token}'.", value: token);
    }

    // ── SetValue (handles repeated accumulation for options/flags) ────────────

    private static void SetValue(
        Dictionary<string, StashValue> values,
        string propName,
        StashValue converted,
        StashInstance spec,
        bool isRepeated)
    {
        if (isRepeated)
        {
            AppendToList(values, propName, converted);
        }
        else
        {
            values[propName] = converted;
        }
    }

    private static void AppendToList(Dictionary<string, StashValue> values, string propName, StashValue item)
    {
        if (values.TryGetValue(propName, out StashValue existing) &&
            existing.IsObj && existing.AsObj is List<StashValue> list)
        {
            list.Add(item);
        }
        else
        {
            values[propName] = StashValue.FromObj(new List<StashValue> { item });
        }
    }

    // ── Env fallbacks ─────────────────────────────────────────────────────────

    private static void ApplyEnvFallbacks(
        Dictionary<string, StashInstance> specByPropName,
        HashSet<string> supplied,
        Dictionary<string, StashValue> values)
    {
        foreach (var (propName, spec) in specByPropName)
        {
            if (supplied.Contains(propName)) continue;

            string? envVar = GetStringFieldNullable(spec, "env");
            if (envVar is null) continue;

            string? envVal = System.Environment.GetEnvironmentVariable(envVar);
            if (envVal is null) continue;

            string typeTag = GetStringFieldOrEmpty(spec, "typeTag");
            string kind = GetStringFieldOrEmpty(spec, "kind");
            bool isRepeated = kind != "flag" && GetBoolFieldOrFalse(spec, "repeated");

            StashValue converted = ConvertValue(envVal, typeTag, $"${envVar}");
            ValidateChoices(converted, envVal, spec, propName);
            // TODO(P5): apply min/max/pattern/validate here
            SetValue(values, propName, converted, spec, isRepeated);
            supplied.Add(propName);
        }
    }

    // ── Apply defaults ────────────────────────────────────────────────────────

    private static void ApplyDefaults(
        Dictionary<string, StashInstance> specByPropName,
        List<StashValue> positionalSpecs,
        HashSet<string> supplied,
        Dictionary<string, StashValue> values)
    {
        // Options and flags
        foreach (var (propName, spec) in specByPropName)
        {
            if (supplied.Contains(propName)) continue;

            StashValue defaultVal = spec.GetField("defaultVal", null);
            if (!defaultVal.IsNull)
            {
                string typeTag = GetStringFieldOrEmpty(spec, "typeTag");
                StashValue finalDefault = ConvertDefaultValue(defaultVal, typeTag);
                values[propName] = finalDefault;
            }
        }

        // Positionals
        foreach (StashValue specSv in positionalSpecs)
        {
            if (!specSv.IsObj || specSv.AsObj is not StashInstance spec) continue;
            string propName = GetStringFieldOrEmpty(spec, "propName");
            if (string.IsNullOrEmpty(propName) || supplied.Contains(propName)) continue;

            StashValue defaultVal = spec.GetField("defaultVal", null);
            if (!defaultVal.IsNull)
            {
                string typeTag = GetStringFieldOrEmpty(spec, "typeTag");
                StashValue finalDefault = ConvertDefaultValue(defaultVal, typeTag);
                values[propName] = finalDefault;
            }
        }
    }

    // ── Missing-required checks ───────────────────────────────────────────────

    private static void CheckMissingRequired(
        Dictionary<string, StashInstance> specByPropName,
        List<StashValue> positionalSpecs,
        HashSet<string> supplied,
        Dictionary<string, StashValue> values)
    {
        // Options and flags (check required:true)
        foreach (var (propName, spec) in specByPropName)
        {
            bool required = GetBoolFieldOrFalse(spec, "required");
            if (!required) continue;
            // Either explicitly supplied (argv/env) or a default was applied
            if (supplied.Contains(propName) || values.ContainsKey(propName)) continue;
            string longName = GetStringFieldOrEmpty(spec, "name");
            throw new CliMissingRequired(
                $"Missing required option '--{longName}'.",
                name: $"--{longName}");
        }

        // Positionals (required:true by default from cli.positional)
        foreach (StashValue specSv in positionalSpecs)
        {
            if (!specSv.IsObj || specSv.AsObj is not StashInstance spec) continue;
            bool required = GetBoolFieldOrFalse(spec, "required");
            if (!required) continue;
            string propName = GetStringFieldOrEmpty(spec, "propName");
            if (string.IsNullOrEmpty(propName)) continue;
            if (supplied.Contains(propName) || values.ContainsKey(propName)) continue;
            string longName = GetStringFieldOrEmpty(spec, "name");
            throw new CliMissingRequired(
                $"Missing required positional '<{longName}>'.",
                name: longName);
        }
    }

    // ── Result dict construction ──────────────────────────────────────────────

    private static StashDictionary BuildResultDict(
        Dictionary<string, StashInstance> specByPropName,
        List<StashValue> positionalSpecs,
        Dictionary<string, StashValue> values)
    {
        var result = new StashDictionary();

        // Options and flags — keyed by propName
        foreach (string propName in specByPropName.Keys)
        {
            if (values.TryGetValue(propName, out StashValue v))
                result.Set(propName, v);
            // Unprovided non-required options/flags with no default are omitted
        }

        // Positionals — keyed by propName
        foreach (StashValue specSv in positionalSpecs)
        {
            if (!specSv.IsObj || specSv.AsObj is not StashInstance spec) continue;
            string propName = GetStringFieldOrEmpty(spec, "propName");
            if (string.IsNullOrEmpty(propName)) continue;
            if (values.TryGetValue(propName, out StashValue v))
                result.Set(propName, v);
        }

        return result;
    }

    // ── Type conversion ───────────────────────────────────────────────────────

    /// <summary>
    /// Converts a raw string value from argv into the Stash type dictated by typeTag.
    /// All failures funnel into CliInvalidValue — no raw ParseError or TypeError leaks out.
    /// </summary>
    private static StashValue ConvertValue(string raw, string typeTag, string optionName)
    {
        try
        {
            return typeTag switch
            {
                "string"   => StashValue.FromObj(raw),
                "int"      => ConvertToInt(raw, optionName),
                "float"    => ConvertToFloat(raw, optionName),
                "bool"     => ConvertToBool(raw, optionName),
                "duration" => ConvertToDuration(raw, optionName),
                "ip"       => ConvertToIp(raw, optionName),
                "bytesize" => ConvertToByteSize(raw, optionName),
                "semver"   => ConvertToSemVer(raw, optionName),
                _          => throw new CliInvalidValue($"Unknown type tag '{typeTag}'.", option: optionName, value: raw, expected: typeTag),
            };
        }
        catch (CliInvalidValue)
        {
            throw; // already a CliInvalidValue — propagate as-is
        }
        catch (RuntimeError ex)
        {
            // Funnel ParseError / TypeError from conv.* engines into CliInvalidValue
            throw new CliInvalidValue(
                $"Invalid value '{raw}' for '{optionName}': {ex.Message}",
                option: optionName, value: raw, expected: typeTag);
        }
    }

    private static StashValue ConvertToInt(string raw, string optionName)
    {
        if (long.TryParse(raw, out long iv))
            return StashValue.FromInt(iv);
        throw new CliInvalidValue(
            $"Invalid value '{raw}' for '{optionName}': expected an integer.",
            option: optionName, value: raw, expected: "int");
    }

    private static StashValue ConvertToFloat(string raw, string optionName)
    {
        if (double.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double dv))
            return StashValue.FromFloat(dv);
        throw new CliInvalidValue(
            $"Invalid value '{raw}' for '{optionName}': expected a float.",
            option: optionName, value: raw, expected: "float");
    }

    /// <summary>
    /// Bool conversion per spec: true/false/1/0/yes/no (case-insensitive).
    /// NOT RuntimeValues.IsTruthy — explicit set only, matching the brief's declared behavior.
    /// </summary>
    private static StashValue ConvertToBool(string raw, string optionName)
    {
        return raw.ToLowerInvariant() switch
        {
            "true" or "1" or "yes" => StashValue.True,
            "false" or "0" or "no" => StashValue.False,
            _ => throw new CliInvalidValue(
                $"Invalid value '{raw}' for '{optionName}': expected true/false/1/0/yes/no.",
                option: optionName, value: raw, expected: "true/false/1/0/yes/no"),
        };
    }

    private static StashValue ConvertToDuration(string raw, string optionName)
    {
        if (StashDuration.TryParse(raw, out StashDuration? dur) && dur is not null)
            return StashValue.FromObj(dur);
        throw new CliInvalidValue(
            $"Invalid value '{raw}' for '{optionName}': expected a duration (e.g. 30s, 5m, 1h30m).",
            option: optionName, value: raw, expected: "duration (e.g. 30s, 5m, 1h30m)");
    }

    private static StashValue ConvertToIp(string raw, string optionName)
    {
        if (StashIpAddress.TryParse(raw, out StashIpAddress? ip) && ip is not null)
            return StashValue.FromObj(ip);
        throw new CliInvalidValue(
            $"Invalid value '{raw}' for '{optionName}': expected an IP address.",
            option: optionName, value: raw, expected: "ip address");
    }

    private static StashValue ConvertToByteSize(string raw, string optionName)
    {
        if (StashByteSize.TryParse(raw, out StashByteSize? bs) && bs is not null)
            return StashValue.FromObj(bs);
        throw new CliInvalidValue(
            $"Invalid value '{raw}' for '{optionName}': expected a byte size (e.g. 512b, 1kb, 4mb).",
            option: optionName, value: raw, expected: "byte size (e.g. 512b, 1kb, 4mb)");
    }

    private static StashValue ConvertToSemVer(string raw, string optionName)
    {
        if (StashSemVer.TryParse(raw, out StashSemVer? sv) && sv is not null)
            return StashValue.FromObj(sv);
        throw new CliInvalidValue(
            $"Invalid value '{raw}' for '{optionName}': expected a semantic version (e.g. 1.2.3).",
            option: optionName, value: raw, expected: "semver (e.g. 1.2.3)");
    }

    /// <summary>
    /// Converts a default StashValue (already validated at schema time) to the native Stash type
    /// for complex tags whose defaults are stored as strings (duration, ip, bytesize, semver).
    /// For simple types (string, int, float, bool) the default is already the correct runtime type.
    /// </summary>
    private static StashValue ConvertDefaultValue(StashValue defaultVal, string typeTag)
    {
        if (typeTag is "string" or "int" or "float" or "bool")
            return defaultVal;

        if (defaultVal.IsObj && defaultVal.AsObj is string s)
        {
            return typeTag switch
            {
                "duration" => ConvertToDuration(s, "(default)"),
                "ip"       => ConvertToIp(s, "(default)"),
                "bytesize" => ConvertToByteSize(s, "(default)"),
                "semver"   => ConvertToSemVer(s, "(default)"),
                _          => defaultVal,
            };
        }
        return defaultVal;
    }

    // ── Choices validation ────────────────────────────────────────────────────

    private static void ValidateChoices(StashValue converted, string raw, StashInstance spec, string optionName)
    {
        StashValue choicesVal = spec.GetField("choices", null);
        if (choicesVal.IsNull || !(choicesVal.IsObj && choicesVal.AsObj is List<StashValue> choices))
            return;

        foreach (StashValue choice in choices)
        {
            if (StashValuesEqual(converted, choice))
                return;
        }

        string expectedStr = string.Join(", ", choices.Select(c => RuntimeValues.Stringify(c.ToObject())));
        throw new CliInvalidValue(
            $"Invalid value '{raw}' for '{optionName}': must be one of {expectedStr}.",
            option: optionName, value: raw, expected: expectedStr);
    }

    private static bool StashValuesEqual(StashValue a, StashValue b)
    {
        if (a.IsInt && b.IsInt) return a.AsInt == b.AsInt;
        if (a.IsFloat && b.IsFloat) return Math.Abs(a.AsFloat - b.AsFloat) < double.Epsilon;
        if (a.IsInt && b.IsFloat) return (double)a.AsInt == b.AsFloat;
        if (a.IsFloat && b.IsInt) return a.AsFloat == (double)b.AsInt;
        if (a.IsBool && b.IsBool) return a.AsBool == b.AsBool;
        if (a.IsObj && b.IsObj) return Equals(a.AsObj, b.AsObj);
        return false;
    }

    // ── Result factory ────────────────────────────────────────────────────────

    private static StashValue MakeParseResult(bool ok, StashDictionary value, StashValue error, bool helpRequested)
    {
        var fields = new Dictionary<string, StashValue>
        {
            ["ok"]            = StashValue.FromBool(ok),
            ["value"]         = StashValue.FromObj(value),
            ["error"]         = error,
            ["helpRequested"] = StashValue.FromBool(helpRequested),
        };
        return StashValue.FromObj(new StashInstance("CliParseResult", fields));
    }

    // ── Schema field helpers ──────────────────────────────────────────────────

    private static List<StashValue> GetListField(StashInstance inst, string field)
    {
        StashValue v = inst.GetField(field, null);
        if (v.IsObj && v.AsObj is List<StashValue> list) return list;
        return [];
    }

    private static StashDictionary GetDictField(StashInstance inst, string field)
    {
        StashValue v = inst.GetField(field, null);
        if (v.IsObj && v.AsObj is StashDictionary d) return d;
        return new StashDictionary();
    }

    private static bool GetBoolField(StashInstance inst, string field)
    {
        StashValue v = inst.GetField(field, null);
        return v.IsBool && v.AsBool;
    }

    private static bool GetBoolFieldOrFalse(StashInstance inst, string field)
    {
        StashValue v = inst.GetField(field, null);
        return v.IsBool && v.AsBool;
    }

    private static string GetStringFieldOrEmpty(StashInstance inst, string field)
    {
        StashValue v = inst.GetField(field, null);
        if (v.IsObj && v.AsObj is string s) return s;
        return "";
    }

    private static string? GetStringFieldNullable(StashInstance inst, string field)
    {
        StashValue v = inst.GetField(field, null);
        if (v.IsObj && v.AsObj is string s) return s;
        return null;
    }
}
