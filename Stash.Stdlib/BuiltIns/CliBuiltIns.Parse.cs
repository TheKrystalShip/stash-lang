namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.Linq;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Runtime.Errors;
using Stash.Stdlib.Abstractions;

// Parsing engine for cli.tryParse / cli.parse — P3 + P7.
// Handles positionals, options, flags, repeated, choices, defaults, env fallback, -- boundary,
// short-flag bundling (-abc), and -nVALUE short option with inline value.
//
// P7 additions:
//   - cli.parse: exit wrapper around cli.tryParse
//       - reads ScriptArgs when no explicit argv is provided
//       - on --help / -h: prints full help to stdout, calls exit(0)
//       - on parse failure: prints short error + abbreviated usage to stderr, calls exit(2)
//       - on success: returns the parsed dict directly
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
    /// <param name="argv">Optional array of strings to parse (defaults to cli.argv)</param>
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
            StashDictionary parsed = RunParser(schemaInst, argv, ctx);
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

    // ── cli.parse ─────────────────────────────────────────────────────────────

    /// <summary>Parses an argv array against a CliSchema. On --help, prints help and exits 0. On failure, prints a short error to stderr and exits 2. On success, returns the parsed values dict.</summary>
    /// <param name="schema">A CliSchema built by cli.schema()</param>
    /// <param name="argv">Optional array of strings to parse (defaults to cli.argv / ScriptArgs)</param>
    /// <exception cref="TypeError">if schema is not a CliSchema</exception>
    /// <returns>dict of parsed values (same shape as cli.tryParse(...).value)</returns>
    [StashFn(Raw = true, Capability = StashCapabilities.Environment, ReturnType = "dict")]
    private static StashValue Parse(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        if (args.Length < 1)
            throw new TypeError("'cli.parse' requires at least 1 argument (schema).");

        if (!args[0].IsObj || args[0].AsObj is not StashInstance schemaInst || schemaInst.TypeName != "CliSchema")
            throw new TypeError("'cli.parse': first argument must be a CliSchema (returned by cli.schema()).");

        // Resolve argv: explicit second argument, or fall back to ScriptArgs
        string[] argv;
        if (args.Length >= 2)
        {
            if (!args[1].IsObj || args[1].AsObj is not List<StashValue> argvList)
                throw new TypeError("'cli.parse': second argument must be an array of strings.");
            argv = new string[argvList.Count];
            for (int i = 0; i < argvList.Count; i++)
            {
                if (!argvList[i].IsObj || argvList[i].AsObj is not string s)
                    throw new TypeError("'cli.parse': argv elements must be strings.");
                argv[i] = s;
            }
        }
        else
        {
            argv = ctx.ScriptArgs ?? [];
        }

        try
        {
            StashDictionary parsed = RunParser(schemaInst, argv, ctx);
            return StashValue.FromObj(parsed);
        }
        catch (HelpRequestedException)
        {
            // --help / -h detected: print full help text to stdout, then exit(0)
            string helpText = RenderHelp(schemaInst, HelpDefaultWidth);
            ctx.Output.WriteLine(helpText);
            ctx.NotifyOutput("stdout", helpText + "\n");
            GlobalBuiltIns.EmitExitImpl(ctx, ParseExitCodeHelp);
            // Unreachable: EmitExitImpl always throws ExitException
            return StashValue.Null;
        }
        catch (RuntimeError ex) when (ex is CliMissingRequired or CliUnknownOption or CliMissingValue
                                         or CliInvalidValue or CliUnexpectedPositional or CliAmbiguousOption
                                         or CliValidationFailed or CliUnknownCommand)
        {
            // Parse failure: print short error + abbreviated usage to stderr, then exit(2)
            string usageLine = BuildAbbreviatedUsage(schemaInst);
            ctx.ErrorOutput.WriteLine(ex.Message);
            ctx.ErrorOutput.WriteLine(usageLine);
            GlobalBuiltIns.EmitExitImpl(ctx, ParseExitCodeError);
            // Unreachable: EmitExitImpl always throws ExitException
            return StashValue.Null;
        }
    }

    // ── Exit code constants ────────────────────────────────────────────────────

    private const int ParseExitCodeHelp  = 0;
    private const int ParseExitCodeError = 2;

    // ── Abbreviated usage builder ──────────────────────────────────────────────

    /// <summary>
    /// Builds a one-line abbreviated usage string for the error-path output.
    /// Format: "Usage: &lt;program&gt; [options] &lt;positionals...&gt; [subcommand]"
    /// This mirrors the first line of cli.help but is derived independently so it
    /// does not depend on the full RenderHelp invocation path.
    /// </summary>
    private static string BuildAbbreviatedUsage(StashInstance schemaInst)
    {
        string programName = GetStringFieldOrEmpty(schemaInst, "programName");
        bool helpFlag = GetBoolField(schemaInst, "helpFlag");
        List<StashValue> positionals = GetListField(schemaInst, "positionals");
        StashDictionary optionsDict = GetDictField(schemaInst, "options");
        StashValue commandSpecVal = schemaInst.GetField("command", null);

        var parts = new List<string>();
        string displayName = string.IsNullOrEmpty(programName) ? "program" : programName;
        parts.Add(HelpUsagePrefix + displayName);

        bool hasOptions = optionsDict.RawKeys().Any() || helpFlag;
        if (hasOptions)
            parts.Add("[options]");

        foreach (StashValue sv in positionals)
        {
            if (!sv.IsObj || sv.AsObj is not StashInstance spec) continue;
            string name = GetStringFieldOrEmpty(spec, "name");
            bool required = GetBoolFieldOrFalse(spec, "required");
            bool repeated = GetBoolFieldOrFalse(spec, "repeated");
            string token = repeated ? name + "..." : name;
            parts.Add(required ? "<" + token + ">" : "[" + token + "]");
        }

        bool hasSubcommands = !commandSpecVal.IsNull &&
                              commandSpecVal.IsObj &&
                              commandSpecVal.AsObj is StashInstance cmdSpec &&
                              cmdSpec.TypeName == "CliCommandSpec";
        if (hasSubcommands)
            parts.Add("[subcommand]");

        return string.Join(" ", parts);
    }

    // ── Parser core ───────────────────────────────────────────────────────────

    private static StashDictionary RunParser(StashInstance schemaInst, string[] argv, IInterpreterContext ctx)
    {
        // P4: Subcommand-aware entry point.
        // If the schema has a CliCommandSpec, we parse in two stages:
        //   1. Scan argv for root-level options/flags and detect the first non-option token.
        //   2. Use that token as the subcommand selector and parse the remainder against the
        //      leaf schema, with root specs still available for "global flag passthrough".
        // If no subcommand spec is present, delegate directly to RunFlatParser.

        bool helpFlag = GetBoolField(schemaInst, "helpFlag");
        StashValue commandSpecVal = schemaInst.GetField("command", null);

        if (!commandSpecVal.IsNull &&
            commandSpecVal.IsObj &&
            commandSpecVal.AsObj is StashInstance cmdSpecInst &&
            cmdSpecInst.TypeName == "CliCommandSpec")
        {
            return RunSubcommandParser(schemaInst, cmdSpecInst, argv, helpFlag, pathSoFar: [], ctx);
        }

        return RunFlatParser(schemaInst, argv, helpFlag,
            inheritedSpecByPropName: null,
            inheritedValues: null,
            inheritedSupplied: null,
            ctx);
    }

    /// <summary>
    /// Subcommand-aware parser.
    /// Scans argv for the first non-option token to use as a subcommand selector.
    /// Root-level options/flags are parsed before and after the subcommand token so that
    /// "global flags carry through" — root flags may appear anywhere in the argv stream.
    /// Returns a flat result dict that contains all root-parsed values PLUS a "command" key
    /// holding a CliCommand instance (name, path, values).
    /// </summary>
    private static StashDictionary RunSubcommandParser(
        StashInstance rootSchemaInst,
        StashInstance cmdSpecInst,
        string[] argv,
        bool helpFlag,
        List<string> pathSoFar,
        IInterpreterContext ctx)
    {
        // ── Build root lookup tables (options/flags only — no positionals at root when subcommands exist) ──
        List<StashValue> rootPositionalSpecs = GetListField(rootSchemaInst, "positionals");
        StashDictionary rootOptionsDict = GetDictField(rootSchemaInst, "options");

        var rootSpecByPropName = new Dictionary<string, StashInstance>(StringComparer.Ordinal);
        var rootLongToProp     = new Dictionary<string, string>(StringComparer.Ordinal);
        var rootShortToProp    = new Dictionary<string, string>(StringComparer.Ordinal);
        var rootAliasToProp    = new Dictionary<string, string>(StringComparer.Ordinal);

        BuildLookupTables(rootOptionsDict, rootSpecByPropName, rootLongToProp, rootShortToProp, rootAliasToProp);

        // ── Parse state ────────────────────────────────────────────────────
        var rootValues    = new Dictionary<string, StashValue>(StringComparer.Ordinal);
        var rootSupplied  = new HashSet<string>(StringComparer.Ordinal);
        int rootPosCursor = 0;
        bool pastDoubleDash = false;

        // ── Available subcommand names ─────────────────────────────────────
        StashValue commandsVal = cmdSpecInst.GetField("commands", null);
        StashDictionary commandsDict = commandsVal.IsObj && commandsVal.AsObj is StashDictionary cd
            ? cd : new StashDictionary();

        // ── First pass: scan argv for root options and the subcommand selector ──
        int i = 0;
        string? selectedSubcommand = null;
        int subcommandArgvStart = argv.Length; // index of first token AFTER the subcommand selector

        while (i < argv.Length)
        {
            string token = argv[i];

            // ── Past -- boundary ────────────────────────────────────────────
            if (pastDoubleDash)
            {
                // After --, everything is positional. If we don't have a subcommand yet,
                // the first positional after -- is NOT a subcommand selector.
                if (selectedSubcommand is null)
                {
                    // Treat as a positional for the root — but subcommands mode means
                    // we don't have root positionals declared. Raise CliUnexpectedPositional.
                    if (rootPositionalSpecs.Count == 0)
                        throw new CliUnexpectedPositional(
                            $"Unexpected positional argument '{token}' — no subcommand was selected.",
                            value: token);
                    ConsumePositional(token, rootPositionalSpecs, ref rootPosCursor, rootValues, rootSupplied, ctx);
                }
                i++;
                continue;
            }

            if (token == "--")
            {
                pastDoubleDash = true;
                i++;
                continue;
            }

            // ── Long option / short option ─────────────────────────────────
            if (token.StartsWith("--", StringComparison.Ordinal))
            {
                // Help flag
                if (helpFlag && token == "--help")
                    throw new HelpRequestedException();

                string longPart = token[2..];
                string? inlineValue = null;
                int eqIdx = longPart.IndexOf('=');
                if (eqIdx >= 0)
                {
                    inlineValue = longPart[(eqIdx + 1)..];
                    longPart = longPart[..eqIdx];
                }

                // Try to match against root options
                if (TryResolveRootLongName(longPart, rootLongToProp, rootAliasToProp, out string? rootPropName))
                {
                    i++; // consume token
                    StashInstance spec = rootSpecByPropName[rootPropName!];
                    string kind = GetStringFieldOrEmpty(spec, "kind");

                    bool isNegation = false;
                    if (longPart.StartsWith("no-", StringComparison.Ordinal))
                    {
                        string baseName = longPart[3..];
                        if (rootLongToProp.TryGetValue(baseName, out string? negProp))
                        {
                            StashInstance negSpec = rootSpecByPropName[negProp];
                            if (GetStringFieldOrEmpty(negSpec, "kind") == "flag" && GetBoolFieldOrFalse(negSpec, "negatable"))
                            {
                                isNegation = true;
                                rootPropName = negProp;
                                spec = negSpec;
                                kind = "flag";
                            }
                        }
                    }

                    if (kind == "flag")
                    {
                        SetValue(rootValues, rootPropName!, isNegation ? StashValue.False : StashValue.True, spec, isRepeated: false);
                        rootSupplied.Add(rootPropName!);
                    }
                    else
                    {
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
                        ApplyConstraints(spec, converted, rawValue, $"--{longPart}", ctx);
                        SetValue(rootValues, rootPropName!, converted, spec, isRepeated);
                        rootSupplied.Add(rootPropName!);
                    }
                    continue;
                }

                // Not a known root option — might be a subcommand-specific flag; stop root scan
                // and treat remaining as subcommand argv. If no subcommand was selected yet, this
                // is an unknown root option.
                if (selectedSubcommand is null)
                {
                    // We need to resolve properly — check if it truly is unknown at root
                    // (CliUnknownOption) or if it belongs to the subcommand (treat as sub argv).
                    // The spec says unknown options raise CliUnknownOption; we'll defer them to
                    // the subcommand parser if a subcommand was already selected, otherwise error.
                    i++;
                    throw new CliUnknownOption($"Unknown option '{token}'.", option: token);
                }
                // We have a selected subcommand — this token plus remainder are subcommand argv
                subcommandArgvStart = i;
                break;
            }

            if (token.StartsWith("-", StringComparison.Ordinal) && token.Length > 1)
            {
                // Help -h
                if (helpFlag && token == "-h")
                    throw new HelpRequestedException();

                // Try root short options
                string shortsPart = token[1..];
                bool allHandled = true;
                int si = 0;
                int savedI = i + 1;
                int tempI = i + 1;

                while (si < shortsPart.Length)
                {
                    string shortChar = shortsPart[si].ToString();
                    si++;

                    if (!rootShortToProp.TryGetValue(shortChar, out string? sPropName))
                    {
                        allHandled = false;
                        break;
                    }

                    StashInstance sSpec = rootSpecByPropName[sPropName];
                    string sKind = GetStringFieldOrEmpty(sSpec, "kind");

                    if (sKind == "flag")
                    {
                        SetValue(rootValues, sPropName, StashValue.True, sSpec, isRepeated: false);
                        rootSupplied.Add(sPropName);
                    }
                    else
                    {
                        string rawValue;
                        if (si < shortsPart.Length)
                        {
                            rawValue = shortsPart[si..];
                            si = shortsPart.Length;
                        }
                        else if (tempI < argv.Length)
                        {
                            rawValue = argv[tempI];
                            tempI++;
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
                        ApplyConstraints(sSpec, converted, rawValue, $"-{shortChar}", ctx);
                        SetValue(rootValues, sPropName, converted, sSpec, sIsRepeated);
                        rootSupplied.Add(sPropName);
                    }
                }

                if (allHandled)
                {
                    i = tempI;
                    continue;
                }

                // Unknown short option — if we have a subcommand, pass to sub; otherwise error
                if (selectedSubcommand is null)
                {
                    // Reset the partially parsed state and report the unknown option
                    string unknown = "-" + shortsPart[si - 1];
                    throw new CliUnknownOption($"Unknown option '{unknown}'.", option: unknown);
                }
                subcommandArgvStart = i;
                break;
            }

            // ── Non-option token: this is the subcommand selector ─────────
            i++;
            selectedSubcommand = token;
            subcommandArgvStart = i;
            break;
        }

        // ── Remaining root-level options after subcommand selector ─────────
        // (Handled by the subcommand parser via "inherited specs" mechanism.)

        // ── Validate the selected subcommand ──────────────────────────────
        if (selectedSubcommand is null)
        {
            // No subcommand token found — help?
            // All remaining tokens were root options. The subcommand is required.
            // Check if there's a help request, else raise CliMissingRequired.
            List<string> allSubcmds = GetSubcommandNames(commandsDict);
            throw new CliMissingRequired(
                $"A subcommand is required. Available: {string.Join(", ", allSubcmds)}.",
                name: "<subcommand>");
        }

        // Check that the selected subcommand is known
        if (!commandsDict.Has(selectedSubcommand))
        {
            List<string> allSubcmds = GetSubcommandNames(commandsDict);
            List<string> candidates = FindCandidates(selectedSubcommand, allSubcmds);
            throw new CliUnknownCommand(
                $"Unknown subcommand '{selectedSubcommand}'. Did you mean: {string.Join(", ", candidates.Count > 0 ? candidates : allSubcmds)}?",
                name: selectedSubcommand,
                candidates: candidates.Count > 0 ? candidates : allSubcmds);
        }

        // ── Resolve the leaf schema ────────────────────────────────────────
        StashValue leafSchemaVal = commandsDict.Get(selectedSubcommand);
        if (!leafSchemaVal.IsObj || leafSchemaVal.AsObj is not StashInstance leafSchema)
            throw new TypeError($"Internal error: subcommand '{selectedSubcommand}' is not a CliSchema.");

        List<string> newPath = [..pathSoFar, selectedSubcommand];

        // ── Build the subcommand argv: remaining tokens from subcommandArgvStart ──
        string[] subArgv = argv[subcommandArgvStart..];

        // ── Check if the leaf schema also has subcommands (two-level recursion) ──
        StashValue leafCommandVal = leafSchema.GetField("command", null);
        bool leafHelpFlag = GetBoolField(leafSchema, "helpFlag");

        StashDictionary leafValues;

        if (!leafCommandVal.IsNull &&
            leafCommandVal.IsObj &&
            leafCommandVal.AsObj is StashInstance leafCmdSpec &&
            leafCmdSpec.TypeName == "CliCommandSpec")
        {
            // Recurse into the next level of subcommands.
            // The recursive call builds the complete result dict (with the innermost CliCommand)
            // and handles its own root env/defaults/missing-required checks.
            // We merge any root-level values from rootValues into the recursive result and return.
            leafValues = RunSubcommandParser(leafSchema, leafCmdSpec, subArgv, leafHelpFlag, newPath, ctx);

            // Apply root-level env fallbacks / defaults / missing-required
            ApplyEnvFallbacks(rootSpecByPropName, rootSupplied, rootValues, ctx);
            ApplyDefaults(rootSpecByPropName, rootPositionalSpecs, rootSupplied, rootValues);
            CheckMissingRequired(rootSpecByPropName, rootPositionalSpecs, rootSupplied, rootValues);

            // The innermost CliCommand (with full path) is already the "command" key in leafValues.
            // Build our root result and propagate the inner command key.
            StashDictionary rootResult = BuildResultDict(rootSpecByPropName, rootPositionalSpecs, rootValues);
            if (leafValues.Has("command"))
                rootResult.Set("command", leafValues.Get("command"));
            return rootResult;
        }

        // Flat leaf: parse the leaf schema's argv with root specs inherited for global flag passthrough
        leafValues = RunFlatParser(
            leafSchema, subArgv, leafHelpFlag,
            inheritedSpecByPropName: rootSpecByPropName,
            inheritedValues: rootValues,
            inheritedSupplied: rootSupplied,
            ctx);

        // ── Apply root-level env fallbacks / defaults / missing-required ──
        ApplyEnvFallbacks(rootSpecByPropName, rootSupplied, rootValues, ctx);
        ApplyDefaults(rootSpecByPropName, rootPositionalSpecs, rootSupplied, rootValues);
        CheckMissingRequired(rootSpecByPropName, rootPositionalSpecs, rootSupplied, rootValues);

        // ── Build the CliCommand struct ────────────────────────────────────
        // The command's "values" dict is the leaf's parsed values (excluding any root keys
        // that were written into rootValues via inherited passthrough).
        // Extract only the leaf schema's keys from leafValues:
        StashDictionary commandValues = ExtractLeafValues(leafSchema, leafValues);

        var commandFields = new Dictionary<string, StashValue>
        {
            ["name"]   = StashValue.FromObj(selectedSubcommand),
            ["path"]   = StashValue.FromObj(newPath.Select(StashValue.FromObj).ToList<StashValue>()),
            ["values"] = StashValue.FromObj(commandValues),
        };
        StashValue commandInst = StashValue.FromObj(new StashInstance("CliCommand", commandFields));

        // ── Build final result dict: root values + "command" key ──────────
        StashDictionary rootResult2 = BuildResultDict(rootSpecByPropName, rootPositionalSpecs, rootValues);
        rootResult2.Set("command", commandInst);
        return rootResult2;
    }

    /// <summary>
    /// Extracts a dict containing only the keys declared in leafSchema (positionals + options),
    /// looking them up from the given source values dict.
    /// </summary>
    private static StashDictionary ExtractLeafValues(StashInstance leafSchema, StashDictionary sourceValues)
    {
        var result = new StashDictionary();

        List<StashValue> positionals = GetListField(leafSchema, "positionals");
        StashDictionary options = GetDictField(leafSchema, "options");

        foreach (object rawKey in options.RawKeys())
        {
            string propName = rawKey?.ToString() ?? "";
            if (sourceValues.Has(propName))
                result.Set(propName, sourceValues.Get(propName));
        }

        foreach (StashValue sv in positionals)
        {
            if (!sv.IsObj || sv.AsObj is not StashInstance spec) continue;
            string propName = GetStringFieldOrEmpty(spec, "propName");
            if (!string.IsNullOrEmpty(propName) && sourceValues.Has(propName))
                result.Set(propName, sourceValues.Get(propName));
        }

        return result;
    }

    /// <summary>Returns subcommand names from a commands dict as a sorted list.</summary>
    private static List<string> GetSubcommandNames(StashDictionary commandsDict)
    {
        var names = new List<string>();
        foreach (object rawKey in commandsDict.RawKeys())
        {
            string name = rawKey?.ToString() ?? "";
            if (!string.IsNullOrEmpty(name))
                names.Add(name);
        }
        names.Sort(StringComparer.Ordinal);
        return names;
    }

    /// <summary>
    /// Finds candidate subcommand names for an unknown command token.
    /// Uses substring matching: returns names that contain the token (case-insensitive)
    /// or share a common prefix. Falls back to all names if no match.
    /// </summary>
    private static List<string> FindCandidates(string unknown, List<string> allNames)
    {
        // Exact prefix match
        var prefixMatches = allNames
            .Where(n => n.StartsWith(unknown, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (prefixMatches.Count > 0) return prefixMatches;

        // Substring match
        var subMatches = allNames
            .Where(n => n.Contains(unknown, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (subMatches.Count > 0) return subMatches;

        // Return all as candidates when nothing matches
        return allNames;
    }

    /// <summary>
    /// Tries to resolve a long option name against root option tables.
    /// Unlike ResolveLongName, this returns false instead of throwing when unresolved.
    /// </summary>
    private static bool TryResolveRootLongName(
        string longPart,
        Dictionary<string, string> longToProp,
        Dictionary<string, string> aliasToProp,
        out string? propName)
    {
        if (longToProp.TryGetValue(longPart, out propName))
            return true;
        if (aliasToProp.TryGetValue(longPart, out propName))
            return true;

        // Prefix matching
        var allLongs = longToProp.Keys.Concat(aliasToProp.Keys)
            .Where(k => k.StartsWith(longPart, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (allLongs.Count == 1)
        {
            string winner = allLongs[0];
            propName = longToProp.TryGetValue(winner, out string? p) ? p : aliasToProp[winner];
            return true;
        }

        propName = null;
        return false;
    }

    /// <summary>Builds the lookup tables for a given options dict.</summary>
    private static void BuildLookupTables(
        StashDictionary optionsDict,
        Dictionary<string, StashInstance> specByPropName,
        Dictionary<string, string> longToProp,
        Dictionary<string, string> shortToProp,
        Dictionary<string, string> aliasToProp)
    {
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
    }

    /// <summary>
    /// The original flat parser, refactored to accept optional "inherited" root specs
    /// so that global flags declared at root can appear in the subcommand argv segment.
    /// When inherited specs are provided, tokens matching inherited options are written
    /// into inheritedValues (root's values dict) rather than the sub's values dict.
    /// </summary>
    private static StashDictionary RunFlatParser(
        StashInstance schemaInst,
        string[] argv,
        bool helpFlag,
        Dictionary<string, StashInstance>? inheritedSpecByPropName,
        Dictionary<string, StashValue>? inheritedValues,
        HashSet<string>? inheritedSupplied,
        IInterpreterContext ctx)
    {
        List<StashValue> positionalSpecs = GetListField(schemaInst, "positionals");
        StashDictionary optionsDict = GetDictField(schemaInst, "options");

        // ── Build lookup tables ────────────────────────────────────────────
        var specByPropName = new Dictionary<string, StashInstance>(StringComparer.Ordinal);
        var longToProp     = new Dictionary<string, string>(StringComparer.Ordinal);
        var shortToProp    = new Dictionary<string, string>(StringComparer.Ordinal);
        var aliasToProp    = new Dictionary<string, string>(StringComparer.Ordinal);

        BuildLookupTables(optionsDict, specByPropName, longToProp, shortToProp, aliasToProp);

        // ── Parse state ────────────────────────────────────────────────────
        var values = new Dictionary<string, StashValue>(StringComparer.Ordinal);
        var supplied = new HashSet<string>(StringComparer.Ordinal);
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
                ConsumePositional(token, positionalSpecs, ref positionalCursor, values, supplied, ctx);
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

                // Check if this is an inherited (root-level) option that should be handled
                if (inheritedSpecByPropName is not null &&
                    TryResolveRootLongName(longPart, BuildLongMap(inheritedSpecByPropName), new Dictionary<string, string>(), out string? inheritedPropName))
                {
                    StashInstance iSpec = inheritedSpecByPropName[inheritedPropName!];
                    string iKind = GetStringFieldOrEmpty(iSpec, "kind");

                    if (iKind == "flag")
                    {
                        SetValue(inheritedValues!, inheritedPropName!, StashValue.True, iSpec, isRepeated: false);
                        inheritedSupplied!.Add(inheritedPropName!);
                    }
                    else
                    {
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
                        string typeTag = GetStringFieldOrEmpty(iSpec, "typeTag");
                        bool isRepeated = GetBoolFieldOrFalse(iSpec, "repeated");
                        StashValue converted = ConvertValue(rawValue, typeTag, $"--{longPart}");
                        ValidateChoices(converted, rawValue, iSpec, $"--{longPart}");
                        ApplyConstraints(iSpec, converted, rawValue, $"--{longPart}", ctx);
                        SetValue(inheritedValues!, inheritedPropName!, converted, iSpec, isRepeated);
                        inheritedSupplied!.Add(inheritedPropName!);
                    }
                    continue;
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
                    ApplyConstraints(spec, converted, rawValue, $"--{longPart}", ctx);
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
                        ApplyConstraints(sSpec, converted, rawValue, $"-{shortChar}", ctx);
                        SetValue(values, sPropName, converted, sSpec, sIsRepeated);
                        supplied.Add(sPropName);
                    }
                }
                continue;
            }

            // ── Positional token ──────────────────────────────────────────
            ConsumePositional(token, positionalSpecs, ref positionalCursor, values, supplied, ctx);
        }

        // ── Post-parse: apply env fallbacks then defaults ──────────────────
        ApplyEnvFallbacks(specByPropName, supplied, values, ctx);
        ApplyDefaults(specByPropName, positionalSpecs, supplied, values);

        // ── Missing-required checks ────────────────────────────────────────
        CheckMissingRequired(specByPropName, positionalSpecs, supplied, values);

        return BuildResultDict(specByPropName, positionalSpecs, values);
    }

    /// <summary>
    /// Builds a longName -> propName lookup from a specByPropName dict.
    /// Used for inherited (root-level) option resolution during subcommand parsing.
    /// </summary>
    private static Dictionary<string, string> BuildLongMap(Dictionary<string, StashInstance> specByPropName)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (propName, spec) in specByPropName)
        {
            string longName = GetStringFieldOrEmpty(spec, "name");
            if (!string.IsNullOrEmpty(longName))
                map[longName] = propName;
        }
        return map;
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
        HashSet<string> supplied,
        IInterpreterContext ctx)
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
            ApplyConstraints(spec, converted, token, sPropName, ctx);

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
                    ApplyConstraints(lastSpec, converted, token, propName, ctx);
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
        Dictionary<string, StashValue> values,
        IInterpreterContext ctx)
    {
        foreach (var (propName, spec) in specByPropName)
        {
            if (supplied.Contains(propName)) continue;

            string? envVar = GetStringFieldNullable(spec, "env");
            if (envVar is null) continue;

            string? envVal = ctx.GetEnv(envVar);
            if (envVal is null) continue;

            string typeTag = GetStringFieldOrEmpty(spec, "typeTag");
            string kind = GetStringFieldOrEmpty(spec, "kind");
            bool isRepeated = kind != "flag" && GetBoolFieldOrFalse(spec, "repeated");

            StashValue converted = ConvertValue(envVal, typeTag, $"${envVar}");
            ValidateChoices(converted, envVal, spec, propName);
            ApplyConstraints(spec, converted, envVal, propName, ctx);
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
