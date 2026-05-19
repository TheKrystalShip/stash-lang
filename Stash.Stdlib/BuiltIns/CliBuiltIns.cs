namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Abstractions;
using Stash.Runtime.Errors;

/// <summary>
/// Registers the <c>cli</c> namespace built-in functions for declarative CLI argument parsing.
/// </summary>
[StashNamespace]
public static partial class CliBuiltIns
{
    // ── Internal model (C# side only) ──────────────────────────────────────

    /// <summary>
    /// Known v1 type tags for CLI argument values.
    /// Unrecognised tags are caught at cli.schema() construction time.
    /// </summary>
    internal static readonly HashSet<string> KnownTypeTags = new(StringComparer.Ordinal)
    {
        "string", "int", "float", "bool", "duration", "ip", "bytesize", "semver",
    };

    // ── Stash-visible struct types ──────────────────────────────────────────
    // These are [StashStruct] declarations; they drive LSP/doc metadata.
    // Runtime instances are StashInstance objects built from field dictionaries.

    /// <summary>A single argument specification (positional, option, or flag).</summary>
    [StashStruct]
    public sealed record CliArgSpec
    {
        /// <summary>Argument kind: "positional", "option", or "flag".</summary>
        public string Kind { get; init; } = "option";

        /// <summary>Type tag: "string", "int", "float", "bool", "duration", "ip", "bytesize", "semver".</summary>
        public string TypeTag { get; init; } = "string";

        /// <summary>CLI-facing long name (kebab-case).</summary>
        public string Name { get; init; } = "";

        /// <summary>Single-character short option (e.g. "v" → -v). Null for none.</summary>
        public string? Short { get; init; }

        /// <summary>Additional long-option aliases.</summary>
        [StashField(Type = "array")]
        public List<StashValue> Aliases { get; init; } = [];

        /// <summary>Whether the argument is required.</summary>
        public bool Required { get; init; }

        /// <summary>Default value (any type). Null means no default. Named 'defaultVal' because 'default' is a reserved keyword in Stash.</summary>
        [StashField(Name = "defaultVal", Type = "any")]
        public StashValue DefaultVal { get; init; } = StashValue.Null;

        /// <summary>Whether the argument may be repeated (accumulates into an array).</summary>
        public bool Repeated { get; init; }

        /// <summary>Allowed values. Null means no restriction.</summary>
        [StashField(Type = "array")]
        public List<StashValue>? Choices { get; init; }

        /// <summary>Minimum numeric value (options only).</summary>
        [StashField(Type = "any")]
        public StashValue Min { get; init; } = StashValue.Null;

        /// <summary>Maximum numeric value (options only).</summary>
        [StashField(Type = "any")]
        public StashValue Max { get; init; } = StashValue.Null;

        /// <summary>Regex pattern constraint (options only).</summary>
        public string? Pattern { get; init; }

        /// <summary>Validate callback (IStashCallable). Recorded but not invoked until P5.</summary>
        [StashField(Type = "any")]
        public StashValue Validate { get; init; } = StashValue.Null;

        /// <summary>Help text shown in --help output.</summary>
        public string? Help { get; init; }

        /// <summary>Metavar for usage lines (e.g. "FILE").</summary>
        public string? Metavar { get; init; }

        /// <summary>Environment variable name whose value is used as a fallback (options only).</summary>
        public string? Env { get; init; }

        /// <summary>Whether the flag can be negated via --no-name (flags only).</summary>
        public bool Negatable { get; init; }
    }

    /// <summary>Maps subcommand names to their nested CliSchema values.</summary>
    [StashStruct]
    public sealed record CliCommandSpec
    {
        /// <summary>Subcommand name-to-schema mapping.</summary>
        [StashField(Type = "dict")]
        public StashDictionary Commands { get; init; } = new();
    }

    /// <summary>A fully-validated schema that describes a script's CLI surface.</summary>
    [StashStruct]
    public sealed record CliSchema
    {
        /// <summary>Positional argument specs in declaration order.</summary>
        [StashField(Type = "array")]
        public List<StashValue> Positionals { get; init; } = [];

        /// <summary>Named option and flag specs keyed by their Stash property name.</summary>
        [StashField(Type = "dict")]
        public StashDictionary Options { get; init; } = new();

        /// <summary>Subcommand spec, or null if there are no subcommands.</summary>
        [StashField(Type = "any")]
        public StashValue Command { get; init; } = StashValue.Null;

        /// <summary>Program name shown in help text (default: script filename).</summary>
        public string ProgramName { get; init; } = "";

        /// <summary>Short description shown in help text.</summary>
        public string Description { get; init; } = "";

        /// <summary>Whether --help / -h are implicitly accepted.</summary>
        public bool HelpFlag { get; init; } = true;
    }

    /// <summary>The parsed subcommand result.</summary>
    [StashStruct]
    public sealed record CliCommand
    {
        /// <summary>The selected subcommand name.</summary>
        public string Name { get; init; } = "";

        /// <summary>Full path of subcommand names from root to leaf.</summary>
        [StashField(Type = "array")]
        public List<StashValue> Path { get; init; } = [];

        /// <summary>Parsed values for the selected subcommand's schema.</summary>
        [StashField(Type = "dict")]
        public StashDictionary Values { get; init; } = new();
    }

    /// <summary>Result returned by cli.tryParse.</summary>
    [StashStruct]
    public sealed record CliParseResult
    {
        /// <summary>True if parsing succeeded.</summary>
        public bool Ok { get; init; }

        /// <summary>Parsed values dict (meaningful when Ok is true).</summary>
        [StashField(Type = "dict")]
        public StashDictionary Value { get; init; } = new();

        /// <summary>The parse error (meaningful when Ok is false).</summary>
        [StashField(Type = "any")]
        public StashValue Error { get; init; } = StashValue.Null;

        /// <summary>True if --help was requested (no other fields are populated).</summary>
        public bool HelpRequested { get; init; }
    }

    // ── Builder functions ──────────────────────────────────────────────────

    /// <summary>Declares a positional argument.</summary>
    /// <param name="typeTag">Type tag: "string", "int", "float", "bool", "duration", "ip", "bytesize", "semver".</param>
    /// <param name="options">Optional dict of spec keys: name, required, default, repeated, choices, validate, help, metavar.</param>
    /// <exception cref="ValueError">if typeTag is not a recognised type tag</exception>
    /// <returns>A CliArgSpec for use inside cli.schema()</returns>
    [StashFn(Raw = true, ReturnType = "CliArgSpec")]
    private static StashValue Positional(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        if (args.Length < 1)
            throw new ValueError("'cli.positional' requires at least 1 argument (typeTag).");

        string typeTag = SvArgs.String(args, 0, "cli.positional");
        ValidateTypeTag(typeTag, "cli.positional");

        StashDictionary? opts = args.Length >= 2 && args[1].IsObj && args[1].AsObj is StashDictionary d ? d : null;

        return MakeArgSpecInstance(
            kind: "positional",
            typeTag: typeTag,
            nameOverride: opts?.GetStringOpt("name"),
            shortOpt: null,
            aliases: [],
            required: opts?.GetBoolOpt("required") ?? true,
            defaultVal: opts?.GetOpt("default") ?? StashValue.Null,
            repeated: opts?.GetBoolOpt("repeated") ?? false,
            choices: opts?.GetListOpt("choices"),
            min: StashValue.Null,
            max: StashValue.Null,
            pattern: null,
            validate: opts?.GetOpt("validate") ?? StashValue.Null,
            help: opts?.GetStringOpt("help"),
            metavar: opts?.GetStringOpt("metavar"),
            env: null,
            negatable: false);
    }

    /// <summary>Declares a named option that takes a value.</summary>
    /// <param name="typeTag">Type tag: "string", "int", "float", "bool", "duration", "ip", "bytesize", "semver".</param>
    /// <param name="options">Optional dict of spec keys: name, short, aliases, required, default, repeated, choices, min, max, pattern, validate, help, metavar, env.</param>
    /// <exception cref="ValueError">if typeTag is not a recognised type tag</exception>
    /// <returns>A CliArgSpec for use inside cli.schema()</returns>
    [StashFn(Raw = true, ReturnType = "CliArgSpec")]
    private static StashValue Option(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        if (args.Length < 1)
            throw new ValueError("'cli.option' requires at least 1 argument (typeTag).");

        string typeTag = SvArgs.String(args, 0, "cli.option");
        ValidateTypeTag(typeTag, "cli.option");

        StashDictionary? opts = args.Length >= 2 && args[1].IsObj && args[1].AsObj is StashDictionary d ? d : null;

        return MakeArgSpecInstance(
            kind: "option",
            typeTag: typeTag,
            nameOverride: opts?.GetStringOpt("name"),
            shortOpt: opts?.GetStringOpt("short"),
            aliases: opts?.GetListOpt("aliases") ?? [],
            required: opts?.GetBoolOpt("required") ?? false,
            defaultVal: opts?.GetOpt("default") ?? StashValue.Null,
            repeated: opts?.GetBoolOpt("repeated") ?? false,
            choices: opts?.GetListOpt("choices"),
            min: opts?.GetOpt("min") ?? StashValue.Null,
            max: opts?.GetOpt("max") ?? StashValue.Null,
            pattern: opts?.GetStringOpt("pattern"),
            validate: opts?.GetOpt("validate") ?? StashValue.Null,
            help: opts?.GetStringOpt("help"),
            metavar: opts?.GetStringOpt("metavar"),
            env: opts?.GetStringOpt("env"),
            negatable: false);
    }

    /// <summary>Declares a boolean flag.</summary>
    /// <param name="options">Optional dict of spec keys: name, short, aliases, help, default, negatable.</param>
    /// <returns>A CliArgSpec for use inside cli.schema()</returns>
    [StashFn(Raw = true, ReturnType = "CliArgSpec")]
    private static StashValue Flag(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        StashDictionary? opts = args.Length >= 1 && args[0].IsObj && args[0].AsObj is StashDictionary d ? d : null;

        return MakeArgSpecInstance(
            kind: "flag",
            typeTag: "bool",
            nameOverride: opts?.GetStringOpt("name"),
            shortOpt: opts?.GetStringOpt("short"),
            aliases: opts?.GetListOpt("aliases") ?? [],
            required: false,
            defaultVal: opts?.GetOpt("default") ?? StashValue.False,
            repeated: false,
            choices: null,
            min: StashValue.Null,
            max: StashValue.Null,
            pattern: null,
            validate: StashValue.Null,
            help: opts?.GetStringOpt("help"),
            metavar: null,
            env: null,
            negatable: opts?.GetBoolOpt("negatable") ?? false);
    }

    /// <summary>Declares a set of named subcommands.</summary>
    /// <param name="definition">Dict mapping subcommand names to CliSchema values.</param>
    /// <exception cref="TypeError">if definition is not a dict</exception>
    /// <returns>A CliCommandSpec for use inside cli.schema()</returns>
    [StashFn(Raw = true, ReturnType = "CliCommandSpec")]
    private static StashValue Command(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        if (args.Length < 1 || !args[0].IsObj || args[0].AsObj is not StashDictionary def)
            throw new TypeError("'cli.command' requires a dict mapping subcommand names to CliSchema values.");

        // Validate each value is a CliSchema instance
        foreach (object key in def.RawKeys())
        {
            StashValue v = def.Get(key);
            if (!v.IsObj || v.AsObj is not StashInstance inst || inst.TypeName != "CliSchema")
                throw new TypeError($"'cli.command': value for subcommand '{key}' must be a CliSchema (returned by cli.schema).");
        }

        var fields = new Dictionary<string, StashValue>
        {
            ["commands"] = StashValue.FromObj(def),
        };
        return StashValue.FromObj(new StashInstance("CliCommandSpec", fields));
    }

    /// <summary>Validates a dict definition and builds a reusable schema value.</summary>
    /// <param name="definition">Dict mapping Stash property names to CliArgSpec / CliCommandSpec values.</param>
    /// <exception cref="TypeError">if definition is not a dict</exception>
    /// <exception cref="ValueError">if any schema-time validation rule is violated</exception>
    /// <returns>A validated CliSchema</returns>
    [StashFn(Raw = true, ReturnType = "CliSchema")]
    private static StashValue Schema(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        if (args.Length < 1 || !args[0].IsObj || args[0].AsObj is not StashDictionary def)
            throw new TypeError("'cli.schema' requires a dict definition.");

        // opts: optional second arg for program-level metadata
        StashDictionary? opts = args.Length >= 2 && args[1].IsObj && args[1].AsObj is StashDictionary d2 ? d2 : null;
        string programName = opts?.GetStringOpt("programName") ?? "";
        string description = opts?.GetStringOpt("description") ?? "";
        bool helpFlag = opts?.GetBoolOpt("helpFlag") ?? true;

        return BuildSchema(def, programName, description, helpFlag);
    }

    // ── Factory helper ──────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <c>StashInstance("CliArgSpec", ...)</c> from the given fields.
    /// The <c>name</c> field is the override if supplied; the schema assigns the final
    /// kebab-cased long name after it knows the property key.
    /// </summary>
    internal static StashValue MakeArgSpecInstance(
        string kind,
        string typeTag,
        string? nameOverride,
        string? shortOpt,
        List<StashValue> aliases,
        bool required,
        StashValue defaultVal,
        bool repeated,
        List<StashValue>? choices,
        StashValue min,
        StashValue max,
        string? pattern,
        StashValue validate,
        string? help,
        string? metavar,
        string? env,
        bool negatable)
    {
        var fields = new Dictionary<string, StashValue>
        {
            ["kind"]        = StashValue.FromObj(kind),
            ["typeTag"]     = StashValue.FromObj(typeTag),
            // name is stored as the override string (or null/empty) initially;
            // cli.schema() replaces this with the resolved kebab-case long name.
            ["name"]        = nameOverride is null ? StashValue.Null : StashValue.FromObj(nameOverride),
            ["short"]       = shortOpt is null ? StashValue.Null : StashValue.FromObj(shortOpt),
            ["aliases"]     = StashValue.FromObj(aliases),
            ["required"]    = StashValue.FromBool(required),
            // Note: "default" is a reserved keyword in Stash; the field is exposed as "defaultVal".
            ["defaultVal"]  = defaultVal,
            ["repeated"]    = StashValue.FromBool(repeated),
            ["choices"]     = choices is null ? StashValue.Null : StashValue.FromObj(choices),
            ["min"]         = min,
            ["max"]         = max,
            ["pattern"]     = pattern is null ? StashValue.Null : StashValue.FromObj(pattern),
            ["validate"]    = validate,
            ["help"]        = help is null ? StashValue.Null : StashValue.FromObj(help),
            ["metavar"]     = metavar is null ? StashValue.Null : StashValue.FromObj(metavar),
            ["env"]         = env is null ? StashValue.Null : StashValue.FromObj(env),
            ["negatable"]   = StashValue.FromBool(negatable),
        };
        return StashValue.FromObj(new StashInstance("CliArgSpec", fields));
    }

    // ── Type validation helper ─────────────────────────────────────────────

    private static void ValidateTypeTag(string tag, string funcName)
    {
        if (!KnownTypeTags.Contains(tag))
            throw new ValueError(
                $"'{funcName}': unknown type tag \"{tag}\". Supported tags: {string.Join(", ", KnownTypeTags)}.");
    }
}

/// <summary>Extension methods on StashDictionary for option extraction in CliBuiltIns.</summary>
internal static class CliDictExtensions
{
    public static StashValue GetOpt(this StashDictionary d, string key)
    {
        return d.Has(key) ? d.Get(key) : StashValue.Null;
    }

    public static string? GetStringOpt(this StashDictionary d, string key)
    {
        if (!d.Has(key)) return null;
        StashValue v = d.Get(key);
        return v.IsObj && v.AsObj is string s ? s : null;
    }

    public static bool? GetBoolOpt(this StashDictionary d, string key)
    {
        if (!d.Has(key)) return null;
        StashValue v = d.Get(key);
        return v.IsBool ? v.AsBool : null;
    }

    public static List<StashValue>? GetListOpt(this StashDictionary d, string key)
    {
        if (!d.Has(key)) return null;
        StashValue v = d.Get(key);
        if (v.IsObj && v.AsObj is List<StashValue> list) return list;
        return null;
    }
}
