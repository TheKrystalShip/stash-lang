namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Runtime.Errors;
using Stash.Stdlib.Abstractions;

// Help rendering for cli.help / cli.printHelp — P6.
//
// cli.help(schema, options?)    → string
// cli.printHelp(schema, options?) → null  (writes via ctx.Output, same path as io.println)
//
// options keys:
//   command: string  — dot-separated path to a subcommand (e.g. "remote.add"), routes rendering
//   width:   int     — column width for wrapping help text (default: 80)
//
// Format produced (all sections present only when non-empty):
//
//   Usage: <program> [options] <pos1> [pos2] [subcommand]
//
//   <description>
//
//   Positional arguments:
//     <name>  METAVAR  help text
//
//   Options:
//     --long, -s METAVAR  help text  (default: value)
//
//   Commands:
//     name  help text
//
// Layout constants (named to satisfy the no-magic-strings rule):
//   LEFT_INDENT = 2 spaces
//   COL_GAP     = 2 spaces between columns
//   DEFAULT_WIDTH = 80
public static partial class CliBuiltIns
{
    // ── Layout constants ───────────────────────────────────────────────────────

    private const int HelpDefaultWidth = 80;
    private const string HelpLeftIndent = "  ";
    private const string HelpColGap = "  ";
    private const string HelpSectionPositionals = "Positional arguments:";
    private const string HelpSectionOptions = "Options:";
    private const string HelpSectionCommands = "Commands:";
    private const string HelpDefaultAnnotationFmt = "(default: {0})";
    private const string HelpUsagePrefix = "Usage: ";

    // ── cli.help ───────────────────────────────────────────────────────────────

    /// <summary>Renders the schema as --help text and returns the string.</summary>
    /// <param name="schema">A CliSchema built by cli.schema().</param>
    /// <param name="options">Optional dict: command (dot-separated subcommand path), width (int, default 80).</param>
    /// <exception cref="TypeError">if schema is not a CliSchema</exception>
    /// <returns>The formatted help text as a string.</returns>
    [StashFn(Raw = true, ReturnType = "string")]
    private static StashValue Help(IInterpreterContext _, ReadOnlySpan<StashValue> args)
    {
        if (args.Length < 1)
            throw new TypeError("'cli.help' requires at least 1 argument (schema).");

        if (!args[0].IsObj || args[0].AsObj is not StashInstance schemaInst || schemaInst.TypeName != "CliSchema")
            throw new TypeError("'cli.help': first argument must be a CliSchema (returned by cli.schema()).");

        StashDictionary? opts = args.Length >= 2 && args[1].IsObj && args[1].AsObj is StashDictionary d ? d : null;
        int width = HelpDefaultWidth;
        if (opts is not null)
        {
            StashValue wv = opts.Has("width") ? opts.Get("width") : StashValue.Null;
            if (wv.IsInt && wv.AsInt > 0)
                width = (int)wv.AsInt;
        }
        string? commandPath = opts?.GetStringOpt("command");

        StashInstance targetSchema = ResolveSubcommandSchema(schemaInst, commandPath);
        string text = RenderHelp(targetSchema, width, commandPath);
        return StashValue.FromObj(text);
    }

    // ── cli.printHelp ──────────────────────────────────────────────────────────

    /// <summary>Renders the schema as --help text and writes it to stdout via io.println.</summary>
    /// <param name="schema">A CliSchema built by cli.schema().</param>
    /// <param name="options">Optional dict: command (dot-separated subcommand path), width (int, default 80).</param>
    /// <exception cref="TypeError">if schema is not a CliSchema</exception>
    /// <returns>null</returns>
    [StashFn(Raw = true, ReturnType = "null")]
    private static StashValue PrintHelp(IInterpreterContext ctx, ReadOnlySpan<StashValue> args)
    {
        if (args.Length < 1)
            throw new TypeError("'cli.printHelp' requires at least 1 argument (schema).");

        if (!args[0].IsObj || args[0].AsObj is not StashInstance schemaInst || schemaInst.TypeName != "CliSchema")
            throw new TypeError("'cli.printHelp': first argument must be a CliSchema (returned by cli.schema()).");

        StashDictionary? opts = args.Length >= 2 && args[1].IsObj && args[1].AsObj is StashDictionary d ? d : null;
        int width = HelpDefaultWidth;
        if (opts is not null)
        {
            StashValue wv = opts.Has("width") ? opts.Get("width") : StashValue.Null;
            if (wv.IsInt && wv.AsInt > 0)
                width = (int)wv.AsInt;
        }
        string? commandPath = opts?.GetStringOpt("command");

        StashInstance targetSchema = ResolveSubcommandSchema(schemaInst, commandPath);
        string text = RenderHelp(targetSchema, width, commandPath);
        // Mirror io.println: write via ctx.Output.WriteLine + notify
        ctx.Output.WriteLine(text);
        ctx.NotifyOutput("stdout", text + "\n");
        return StashValue.Null;
    }

    // ── Subcommand routing ─────────────────────────────────────────────────────

    /// <summary>
    /// Resolves a dot-separated subcommand path against the schema.
    /// Returns the root schema when commandPath is null or empty.
    /// Throws CliUnknownCommand when any segment of the path is unknown.
    /// </summary>
    private static StashInstance ResolveSubcommandSchema(StashInstance rootSchema, string? commandPath)
    {
        if (string.IsNullOrEmpty(commandPath))
            return rootSchema;

        string[] segments = commandPath.Split('.');
        StashInstance current = rootSchema;

        foreach (string segment in segments)
        {
            StashValue commandVal = current.GetField("command", null);
            if (commandVal.IsNull || !(commandVal.IsObj && commandVal.AsObj is StashInstance cmdSpecInst &&
                    cmdSpecInst.TypeName == "CliCommandSpec"))
                throw new CliUnknownCommand(
                    $"'cli.help': subcommand segment '{segment}' not found — schema has no subcommands at this level.",
                    name: segment,
                    candidates: []);

            StashDictionary commandsDict = GetDictField(cmdSpecInst, "commands");
            if (!commandsDict.Has(segment))
            {
                List<string> available = GetSubcommandNames(commandsDict);
                throw new CliUnknownCommand(
                    $"'cli.help': unknown subcommand '{segment}'. Available: {string.Join(", ", available)}.",
                    name: segment,
                    candidates: available);
            }

            StashValue nextSchemaVal = commandsDict.Get(segment);
            if (!nextSchemaVal.IsObj || nextSchemaVal.AsObj is not StashInstance nextSchema)
                throw new TypeError($"'cli.help': internal error — subcommand '{segment}' is not a CliSchema.");
            current = nextSchema;
        }

        return current;
    }

    // ── Core renderer ──────────────────────────────────────────────────────────

    /// <summary>
    /// Renders the full help text for the given schema at the given terminal width.
    /// commandPath is used only to qualify the usage line when routing to a subcommand.
    /// </summary>
    /// <remarks>
    /// Treated as a public contract via <c>InternalsVisibleTo</c> to <c>Stash</c> (Stash.Cli,
    /// <c>StaticHelpMode</c>) for the static <c>--help</c> path. Signature changes require
    /// lockstep updates in <c>Stash.Cli/Modes/StaticHelpMode.cs</c>.
    /// </remarks>
    internal static string RenderHelp(StashInstance schemaInst, int width, string? commandPath = null)
    {
        string programName = GetStringFieldOrEmpty(schemaInst, "programName");
        string description = GetStringFieldOrEmpty(schemaInst, "description");
        bool helpFlag = GetBoolField(schemaInst, "helpFlag");
        List<StashValue> positionals = GetListField(schemaInst, "positionals");
        StashDictionary optionsDict = GetDictField(schemaInst, "options");
        StashValue commandSpecVal = schemaInst.GetField("command", null);

        List<string> subcommandNames = [];
        StashInstance? resolvedCmdSpecInst = null;
        if (!commandSpecVal.IsNull &&
            commandSpecVal.IsObj &&
            commandSpecVal.AsObj is StashInstance cmdSpecInstCheck &&
            cmdSpecInstCheck.TypeName == "CliCommandSpec")
        {
            resolvedCmdSpecInst = cmdSpecInstCheck;
            StashDictionary commandsDict = GetDictField(resolvedCmdSpecInst, "commands");
            subcommandNames = GetSubcommandNames(commandsDict);
        }

        var sb = new StringBuilder();

        // ── Usage line ─────────────────────────────────────────────────────────
        // Build: "Usage: <prog[.sub]> [options] <pos1> [pos2] [subcommand]"
        var usageParts = new List<string>();
        string displayName = string.IsNullOrEmpty(commandPath)
            ? programName
            : (string.IsNullOrEmpty(programName) ? commandPath : programName + " " + commandPath.Replace('.', ' '));

        if (string.IsNullOrEmpty(displayName))
            displayName = "program";

        usageParts.Add(HelpUsagePrefix + displayName);

        // [options] token appears if there are any options/flags (or implicit --help)
        bool hasOptions = optionsDict.RawKeys().Any() || helpFlag;
        if (hasOptions)
            usageParts.Add("[options]");

        // Positionals
        foreach (StashValue sv in positionals)
        {
            if (!sv.IsObj || sv.AsObj is not StashInstance spec) continue;
            string name = GetStringFieldOrEmpty(spec, "name");
            bool required = GetBoolFieldOrFalse(spec, "required");
            bool repeated = GetBoolFieldOrFalse(spec, "repeated");
            string token = repeated ? name + "..." : name;
            usageParts.Add(required ? "<" + token + ">" : "[" + token + "]");
        }

        // [subcommand] placeholder when subcommands are declared
        if (subcommandNames.Count > 0)
            usageParts.Add("[subcommand]");

        sb.AppendLine(string.Join(" ", usageParts));

        // ── Description ────────────────────────────────────────────────────────
        if (!string.IsNullOrEmpty(description))
        {
            sb.AppendLine();
            foreach (string line in WordWrap(description, width))
                sb.AppendLine(line);
        }

        // ── Positional arguments section ───────────────────────────────────────
        if (positionals.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(HelpSectionPositionals);

            // Collect rows: (label, helpText)
            var posRows = new List<(string label, string help, string defaultAnnotation)>();
            foreach (StashValue sv in positionals)
            {
                if (!sv.IsObj || sv.AsObj is not StashInstance spec) continue;
                string name = GetStringFieldOrEmpty(spec, "name");
                string typeTag = GetStringFieldOrEmpty(spec, "typeTag");
                string? metavar = GetStringFieldNullable(spec, "metavar");
                string? helpText = GetStringFieldNullable(spec, "help");
                StashValue defaultVal = spec.GetField("defaultVal", null);

                // label: <name> TYPE
                string mv = metavar ?? typeTag.ToUpperInvariant();
                string label = $"<{name}>";
                if (!string.IsNullOrEmpty(mv))
                    label += " " + mv;

                string defaultAnnotation = defaultVal.IsNull
                    ? ""
                    : string.Format(HelpDefaultAnnotationFmt, FormatDefaultValue(defaultVal));

                posRows.Add((label, helpText ?? "", defaultAnnotation));
            }

            AppendTableRows(sb, posRows, width);
        }

        // ── Options section ────────────────────────────────────────────────────
        // Collect all options/flags from the dict, then append implicit --help if helpFlag is on
        var optRows = new List<(string label, string help, string defaultAnnotation)>();

        foreach (object rawKey in optionsDict.RawKeys())
        {
            StashValue sv = optionsDict.Get(rawKey!);
            if (!sv.IsObj || sv.AsObj is not StashInstance spec) continue;

            string longName = GetStringFieldOrEmpty(spec, "name");
            string? shortOpt = GetStringFieldNullable(spec, "short");
            string? metavar = GetStringFieldNullable(spec, "metavar");
            string? helpText = GetStringFieldNullable(spec, "help");
            string typeTag = GetStringFieldOrEmpty(spec, "typeTag");
            string kind = GetStringFieldOrEmpty(spec, "kind");
            StashValue defaultVal = spec.GetField("defaultVal", null);

            // Build the left-column label: --long, -s [METAVAR]
            string label = "--" + longName;
            if (shortOpt is not null)
                label += ", -" + shortOpt;
            if (kind != "flag")
            {
                string mv = metavar ?? typeTag.ToUpperInvariant();
                label += " " + mv;
            }

            string defaultAnnotation = "";
            if (!defaultVal.IsNull)
            {
                // Don't annotate default:false for flags (it is the universal default)
                bool isFlagFalseDefault = kind == "flag" && defaultVal.IsBool && !defaultVal.AsBool;
                if (!isFlagFalseDefault)
                    defaultAnnotation = string.Format(HelpDefaultAnnotationFmt, FormatDefaultValue(defaultVal));
            }

            optRows.Add((label, helpText ?? "", defaultAnnotation));
        }

        if (helpFlag)
            optRows.Add(("--help, -h", "Show this help message.", ""));

        if (optRows.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(HelpSectionOptions);
            AppendTableRows(sb, optRows, width);
        }

        // ── Commands section ───────────────────────────────────────────────────
        if (subcommandNames.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(HelpSectionCommands);

            var cmdRows = new List<(string label, string help, string defaultAnnotation)>();
            StashDictionary commandsDict = GetDictField(resolvedCmdSpecInst!, "commands");
            foreach (string name in subcommandNames)
            {
                // The subcommand schema may have a description — extract it
                string cmdHelp = "";
                if (commandsDict.Has(name))
                {
                    StashValue subVal = commandsDict.Get(name);
                    if (subVal.IsObj && subVal.AsObj is StashInstance subSchemaInst)
                    {
                        string desc = GetStringFieldOrEmpty(subSchemaInst, "description");
                        if (!string.IsNullOrEmpty(desc))
                            cmdHelp = desc;
                    }
                }
                cmdRows.Add((name, cmdHelp, ""));
            }

            AppendTableRows(sb, cmdRows, width);
        }

        // Remove trailing newline from the last AppendLine so output ends cleanly
        string result = sb.ToString().TrimEnd('\n', '\r');
        return result;
    }

    // ── Table layout helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Appends indented table rows with two-column layout:
    ///   INDENT label  COLGAP  help  defaultAnnotation
    /// The help+annotation column is word-wrapped to fit within the terminal width.
    /// </summary>
    private static void AppendTableRows(
        StringBuilder sb,
        List<(string label, string help, string defaultAnnotation)> rows,
        int width)
    {
        if (rows.Count == 0) return;

        // Compute the left column width (max label length)
        int labelWidth = rows.Max(r => r.label.Length);
        int leftColWidth = HelpLeftIndent.Length + labelWidth + HelpColGap.Length;
        int rightColWidth = Math.Max(width - leftColWidth, 20);

        foreach (var (label, help, defaultAnnotation) in rows)
        {
            // Combine help text and default annotation
            string fullHelp = help;
            if (!string.IsNullOrEmpty(defaultAnnotation))
                fullHelp = string.IsNullOrEmpty(fullHelp)
                    ? defaultAnnotation
                    : fullHelp + "  " + defaultAnnotation;

            // Pad label to the common width
            string leftCol = HelpLeftIndent + label.PadRight(labelWidth) + HelpColGap;

            if (string.IsNullOrEmpty(fullHelp))
            {
                sb.AppendLine(leftCol.TrimEnd());
                continue;
            }

            List<string> wrapped = WordWrap(fullHelp, rightColWidth);
            for (int i = 0; i < wrapped.Count; i++)
            {
                if (i == 0)
                    sb.AppendLine(leftCol + wrapped[i]);
                else
                    sb.AppendLine(new string(' ', leftColWidth) + wrapped[i]);
            }
        }
    }

    /// <summary>
    /// Wraps text to at most <paramref name="maxWidth"/> characters per line.
    /// Breaks on whitespace boundaries; never splits a word.
    /// Returns a list of lines (no trailing newlines).
    /// </summary>
    private static List<string> WordWrap(string text, int maxWidth)
    {
        if (maxWidth <= 0) maxWidth = HelpDefaultWidth;
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            lines.Add("");
            return lines;
        }

        // Split on existing newlines first
        foreach (string paragraph in text.Split('\n'))
        {
            string para = paragraph.TrimEnd('\r');
            if (para.Length <= maxWidth)
            {
                lines.Add(para);
                continue;
            }

            // Break into words
            string[] words = para.Split(' ');
            var current = new StringBuilder();

            foreach (string word in words)
            {
                if (word.Length == 0) continue;

                if (current.Length == 0)
                {
                    current.Append(word);
                }
                else if (current.Length + 1 + word.Length <= maxWidth)
                {
                    current.Append(' ');
                    current.Append(word);
                }
                else
                {
                    lines.Add(current.ToString());
                    current.Clear();
                    current.Append(word);
                }
            }

            if (current.Length > 0)
                lines.Add(current.ToString());
        }

        return lines.Count > 0 ? lines : [""];
    }

    /// <summary>
    /// Formats a StashValue default for the help annotation.
    /// Produces a human-readable string (e.g. "3", "true", "\"hello\"", "30s").
    /// </summary>
    private static string FormatDefaultValue(StashValue v)
    {
        if (v.IsInt) return v.AsInt.ToString();
        if (v.IsFloat) return v.AsFloat.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (v.IsBool) return v.AsBool ? "true" : "false";
        if (v.IsObj)
        {
            object? obj = v.AsObj;
            if (obj is string s) return s;
            return RuntimeValues.Stringify(obj);
        }
        return "null";
    }
}
