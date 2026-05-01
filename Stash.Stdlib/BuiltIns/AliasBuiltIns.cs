namespace Stash.Stdlib.BuiltIns;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.Registration;
using static Stash.Stdlib.Registration.P;

/// <summary>
/// Registers the <c>alias</c> namespace built-in functions.
/// </summary>
/// <remarks>
/// <para>
/// Provides programmatic access to the session-local alias registry. Template aliases
/// store a body string with <c>${args}</c>, <c>${args[N]}</c>, and <c>${argv}</c>
/// placeholders; function aliases store a first-class Stash callable.
/// </para>
/// <para>
/// Shell-mode dispatch (bare-word invocation) is wired in Phase B. Persistence
/// (<c>alias.save</c> / <c>alias.load</c>) is implemented in Phase F.
/// </para>
/// <para>
/// Gated on <see cref="StashCapabilities.None"/> so the namespace is available on all
/// platforms, including Windows embedded hosts, even though shell mode is POSIX-only.
/// </para>
/// </remarks>
public static class AliasBuiltIns
{
    // ---------------------------------------------------------------------------
    // Static delegate slots — populated by Stash.Cli in later phases
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Optional executor for template aliases invoked via <c>alias.exec</c>.
    /// Receives the alias entry, the parsed string arguments, and the interpreter
    /// context; returns the exit code of the executed command.
    /// Set by <c>Stash.Cli</c> in Phase B when ShellRunner integration is wired.
    /// When <see langword="null"/> (embedded / Phase A), template-alias execution
    /// throws <see cref="StashErrorTypes.AliasError"/>.
    /// </summary>
    public static Func<AliasRegistry.AliasEntry, string[], IInterpreterContext, int>? AliasExecutor { get; set; }

    // ---------------------------------------------------------------------------
    // Template substitution regex
    // ---------------------------------------------------------------------------

    // Matches:
    //   ${args}      — all args, shell-quoted and space-joined
    //   ${args[N]}   — single arg at index N (0-based)
    //   ${argv}      — Stash array literal of all args
    //   ${...}       — any other interpolation (passed through verbatim)
    private static readonly Regex _substRegex = new(
        @"\$\{args(?:\[(\d+)\])?\}|\$\{argv\}|\$\{[^}]*\}",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    // Matches only argument placeholders: ${args}, ${args[N]}, ${argv}.
    // Used by the strict-args guard to decide whether a template accepts arguments.
    private static readonly Regex _argPlaceholderCheck = new(
        @"\$\{args(?:\[\d+\])?\}|\$\{argv\}",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    // ---------------------------------------------------------------------------
    // Namespace definition
    // ---------------------------------------------------------------------------

    public static NamespaceDefinition Define()
    {
        var ns = new NamespaceBuilder("alias");

        // ── alias.define(name, body, opts? = null) ──────────────────────────
        ns.Function("define",
            [Param("name", "string"), Param("body"), Param("opts", "AliasOptions?")],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
            {
                string name = SvArgs.String(args, 0, "alias.define");
                StashValue body = args[1];
                StashValue optsVal = args.Length > 2 ? args[2] : StashValue.Null;

                AliasRegistry.AliasEntry entry;

                if (body.IsObj && body.AsObj is string templateBody)
                {
                    entry = new AliasRegistry.AliasEntry
                    {
                        Name = name,
                        Kind = AliasRegistry.AliasKind.Template,
                        TemplateBody = templateBody,
                    };
                }
                else if (body.IsObj && body.AsObj is IStashCallable callable)
                {
                    entry = new AliasRegistry.AliasEntry
                    {
                        Name = name,
                        Kind = AliasRegistry.AliasKind.Function,
                        FunctionBody = callable,
                    };
                }
                else
                {
                    throw new RuntimeError(
                        "2nd argument to 'alias.define' must be a string (template body) or a function.",
                        null,
                        StashErrorTypes.TypeError);
                }

                // Parse AliasOptions struct if provided
                if (!optsVal.IsNull && optsVal.IsObj && optsVal.AsObj is StashInstance opts &&
                    opts.TypeName == "AliasOptions")
                {
                    if (opts.VMTryGetField("description", out StashValue descVal, null) &&
                        descVal.IsObj && descVal.AsObj is string desc)
                        entry.Description = desc;

                    if (opts.VMTryGetField("before", out StashValue beforeVal, null) &&
                        beforeVal.IsObj && beforeVal.AsObj is IStashCallable beforeFn)
                        entry.Before = beforeFn;

                    if (opts.VMTryGetField("after", out StashValue afterVal, null) &&
                        afterVal.IsObj && afterVal.AsObj is IStashCallable afterFn)
                        entry.After = afterFn;

                    if (opts.VMTryGetField("confirm", out StashValue confirmVal, null) &&
                        confirmVal.IsObj && confirmVal.AsObj is string confirmStr)
                        entry.Confirm = confirmStr;

                    if (opts.VMTryGetField("override", out StashValue overrideVal, null) &&
                        overrideVal.IsBool)
                        entry.Override = overrideVal.AsBool;
                }

                ctx.AliasRegistry.Define(entry);
                return StashValue.Null;
            },
            returnType: null,
            isVariadic: true,
            documentation: "Defines a new alias. `body` may be a string (template alias) or a function (function alias). " +
                           "Optional `opts` is an AliasOptions struct for description, hooks, and override flag.\n" +
                           "@param name Alias name — must be a valid identifier\n" +
                           "@param body Template string or callable\n" +
                           "@param opts Optional AliasOptions struct\n" +
                           "@return null");

        // ── alias.list() ────────────────────────────────────────────────────
        ns.Function("list", [],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> _) =>
            {
                var result = new List<StashValue>();
                foreach (var entry in ctx.AliasRegistry.All())
                    result.Add(MakeAliasInfo(entry));
                return StashValue.FromObj(result);
            },
            returnType: "array",
            documentation: "Returns an array of AliasInfo structs for all registered aliases, sorted by name.\n@return Array of AliasInfo");

        // ── alias.names() ───────────────────────────────────────────────────
        ns.Function("names", [],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> _) =>
            {
                var result = ctx.AliasRegistry.Names()
                    .Select(StashValue.FromObj)
                    .ToList();
                return StashValue.FromObj(result);
            },
            returnType: "array",
            documentation: "Returns a sorted array of all registered alias names.\n@return Array of strings");

        // ── alias.get(name) ─────────────────────────────────────────────────
        ns.Function("get", [Param("name", "string")],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
            {
                string name = SvArgs.String(args, 0, "alias.get");
                if (!ctx.AliasRegistry.TryGet(name, out AliasRegistry.AliasEntry? entry) || entry is null)
                    return StashValue.Null;
                return MakeAliasInfo(entry);
            },
            returnType: "AliasInfo?",
            documentation: "Returns the AliasInfo struct for the given alias name, or null if not found.\n@param name Alias name\n@return AliasInfo or null");

        // ── alias.exists(name) ──────────────────────────────────────────────
        ns.Function("exists", [Param("name", "string")],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
            {
                string name = SvArgs.String(args, 0, "alias.exists");
                return StashValue.FromBool(ctx.AliasRegistry.Exists(name));
            },
            returnType: "bool",
            documentation: "Returns true if an alias with the given name is registered.\n@param name Alias name\n@return bool");

        // ── alias.remove(name) ──────────────────────────────────────────────
        ns.Function("remove", [Param("name", "string")],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
            {
                string name = SvArgs.String(args, 0, "alias.remove");
                return StashValue.FromBool(ctx.AliasRegistry.Remove(name));
            },
            returnType: "bool",
            documentation: "Removes the alias with the given name. Returns true if removed, false if not found. " +
                           "Throws AliasError if the alias is a built-in.\n" +
                           "@param name Alias name\n@return bool");

        // ── alias.clear() ───────────────────────────────────────────────────
        ns.Function("clear", [],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> _) =>
            {
                return StashValue.FromInt((long)ctx.AliasRegistry.Clear());
            },
            returnType: "int",
            documentation: "Removes all non-built-in aliases and returns the count removed.\n@return int");

        // ── alias.exec(name, args) ──────────────────────────────────────────
        ns.Function("exec",
            [Param("name", "string"), Param("args", "array")],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
            {
                string name = SvArgs.String(args, 0, "alias.exec");
                List<StashValue> argList = SvArgs.StashList(args, 1, "alias.exec");

                if (!ctx.AliasRegistry.TryGet(name, out AliasRegistry.AliasEntry? entry) || entry is null)
                {
                    throw new RuntimeError(
                        $"alias '{name}' is not defined",
                        null,
                        StashErrorTypes.AliasError);
                }

                string[] stringArgs = ParseStringArgs(argList, "alias.exec");

                if (entry.Kind == AliasRegistry.AliasKind.Function)
                {
                    // Function alias: invoke via the interpreter context
                    StashValue[] fnArgs = Array.ConvertAll(stringArgs, StashValue.FromObj);
                    ctx.InvokeCallbackDirect(entry.FunctionBody!, fnArgs);
                    return StashValue.FromInt((long)ctx.GetLastExitCode());
                }
                else
                {
                    // Template alias: delegate to AliasExecutor (wired in Phase B)
                    if (AliasExecutor is null)
                    {
                        throw new RuntimeError(
                            "alias execution requires shell mode; alias.exec for template aliases is not available in embedded mode",
                            null,
                            StashErrorTypes.AliasError);
                    }

                    int exitCode = AliasExecutor(entry, stringArgs, ctx);
                    return StashValue.FromInt((long)exitCode);
                }
            },
            returnType: "int",
            documentation: "Executes the alias with the given name and string arguments. " +
                           "For template aliases, delegates to the shell runner (requires shell mode). " +
                           "For function aliases, invokes the callable directly.\n" +
                           "@param name Alias name\n@param args Array of string arguments\n@return Exit code (int)");

        // ── alias.expand(name, args) ─────────────────────────────────────────
        ns.Function("expand",
            [Param("name", "string"), Param("args", "array")],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
            {
                string name = SvArgs.String(args, 0, "alias.expand");
                List<StashValue> argList = SvArgs.StashList(args, 1, "alias.expand");

                if (!ctx.AliasRegistry.TryGet(name, out AliasRegistry.AliasEntry? entry) || entry is null)
                {
                    throw new RuntimeError(
                        $"alias '{name}' is not defined",
                        null,
                        StashErrorTypes.AliasError);
                }

                if (entry.Kind == AliasRegistry.AliasKind.Function)
                    return StashValue.FromObj($"<function alias `{entry.Name}`>");

                string[] stringArgs = ParseStringArgs(argList, "alias.expand");
                string expanded = ExpandTemplate(entry.Name, entry.TemplateBody!, stringArgs);
                return StashValue.FromObj(expanded);
            },
            returnType: "string",
            documentation: "Returns the expanded form of a template alias body with arguments substituted. " +
                           "For function aliases, returns a '<function alias `name`>' placeholder.\n" +
                           "@param name Alias name\n@param args Array of string arguments\n@return Expanded string");

        // ── alias.save(name? = null) — Phase F stub ──────────────────────────
        ns.Function("save", [Param("name", "string?")],
            static (IInterpreterContext _, ReadOnlySpan<StashValue> __) =>
            {
                throw new RuntimeError(
                    "alias.save is not yet implemented (Phase F)",
                    null,
                    StashErrorTypes.NotSupportedError);
            },
            returnType: "string",
            isVariadic: true,
            documentation: "Persists one or all aliases to the managed aliases file. " +
                           "Phase F — not yet implemented.\n" +
                           "@param name (optional) Alias name to save; saves all when omitted\n@return Path written");

        // ── alias.load(path? = null) — Phase F stub ──────────────────────────
        ns.Function("load", [Param("path", "string?")],
            static (IInterpreterContext _, ReadOnlySpan<StashValue> __) =>
            {
                throw new RuntimeError(
                    "alias.load is not yet implemented (Phase F)",
                    null,
                    StashErrorTypes.NotSupportedError);
            },
            returnType: "int",
            isVariadic: true,
            documentation: "Loads aliases from the managed aliases file (or a custom path). " +
                           "Phase F — not yet implemented.\n" +
                           "@param path (optional) Custom file path; defaults to the managed aliases file\n@return Count of aliases loaded");

        // ── alias.__listPretty() — internal shell sugar helper (Phase C) ─────
        // Not part of the public API; called by the alias shell sugar to pretty-print
        // all registered aliases grouped by source.
        ns.Function("__listPretty", [],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> _) =>
            {
                PrettyPrintAll(ctx);
                return StashValue.Null;
            },
            returnType: null,
            documentation: "Internal: pretty-prints all aliases grouped by source. Used by alias shell sugar.");

        // ── alias.__getPretty(name) — internal shell sugar helper (Phase C) ──
        // Not part of the public API; called by the alias shell sugar to pretty-print
        // a single alias, or report that it is not defined.
        ns.Function("__getPretty", [Param("name", "string")],
            static (IInterpreterContext ctx, ReadOnlySpan<StashValue> args) =>
            {
                string name = SvArgs.String(args, 0, "alias.__getPretty");
                PrettyPrintOne(ctx, name);
                return StashValue.Null;
            },
            returnType: null,
            documentation: "Internal: pretty-prints a single alias definition. Used by alias shell sugar.");

        return ns.Build();
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Builds an <c>AliasInfo</c> <see cref="StashInstance"/> from a registry entry.
    /// </summary>
    private static StashValue MakeAliasInfo(AliasRegistry.AliasEntry entry)
    {
        StashValue sourceLoc;
        if (entry.SourceFile is not null)
        {
            sourceLoc = StashValue.FromObj(new StashInstance("SourceLoc", new Dictionary<string, StashValue>
            {
                ["file"] = StashValue.FromObj(entry.SourceFile),
                ["line"] = StashValue.FromInt((long)(entry.SourceLine ?? 0)),
            }));
        }
        else
        {
            sourceLoc = StashValue.Null;
        }

        string bodyText = entry.Kind == AliasRegistry.AliasKind.Template
            ? (entry.TemplateBody ?? "")
            : $"<function alias `{entry.Name}`>";

        var fields = new Dictionary<string, StashValue>
        {
            ["name"]        = StashValue.FromObj(entry.Name),
            ["kind"]        = StashValue.FromObj(entry.Kind == AliasRegistry.AliasKind.Template ? "template" : "function"),
            ["body"]        = StashValue.FromObj(bodyText),
            ["params"]      = StashValue.FromObj(new List<StashValue>()),
            ["description"] = entry.Description is not null ? StashValue.FromObj(entry.Description) : StashValue.Null,
            ["hasBefore"]   = StashValue.FromBool(entry.Before is not null),
            ["hasAfter"]    = StashValue.FromBool(entry.After is not null),
            ["confirm"]     = entry.Confirm is not null ? StashValue.FromObj(entry.Confirm) : StashValue.Null,
            ["source"]      = StashValue.FromObj(SourceToString(entry.Source)),
            ["sourceLoc"]   = sourceLoc,
        };

        return StashValue.FromObj(new StashInstance("AliasInfo", fields));
    }

    private static string SourceToString(AliasRegistry.AliasSource source) => source switch
    {
        AliasRegistry.AliasSource.Rc      => "rc",
        AliasRegistry.AliasSource.Repl    => "repl",
        AliasRegistry.AliasSource.Saved   => "saved",
        AliasRegistry.AliasSource.Builtin => "builtin",
        _                                  => "repl",
    };

    // ── Pretty-print helpers (used by __listPretty / __getPretty) ─────────────

    private static void PrettyPrintAll(IInterpreterContext ctx)
    {
        var all = ctx.AliasRegistry.All().ToList();
        if (all.Count == 0)
        {
            ctx.Output.WriteLine("(no aliases defined)");
            return;
        }

        var groups = all
            .GroupBy(e => e.Source)
            .OrderBy(g => SourceOrder(g.Key));

        bool first = true;
        foreach (var group in groups)
        {
            if (!first) ctx.Output.WriteLine();
            first = false;

            ctx.Output.WriteLine($"[{SourceToString(group.Key)}]");
            foreach (var entry in group.OrderBy(e => e.Name, StringComparer.Ordinal))
            {
                string body = entry.Kind == AliasRegistry.AliasKind.Function
                    ? "function alias"
                    : $"= \"{entry.TemplateBody}\"";
                string desc = entry.Description is not null ? $"  — {entry.Description}" : "";
                ctx.Output.WriteLine($"  {entry.Name,-12}{body}{desc}");
            }
        }
    }

    private static void PrettyPrintOne(IInterpreterContext ctx, string name)
    {
        if (!ctx.AliasRegistry.TryGet(name, out AliasRegistry.AliasEntry? entry) || entry is null)
        {
            ctx.Output.WriteLine($"alias '{name}' is not defined");
            return;
        }

        if (entry.Kind == AliasRegistry.AliasKind.Template)
            ctx.Output.WriteLine($"{entry.Name} = \"{entry.TemplateBody}\"");
        else
            ctx.Output.WriteLine($"{entry.Name} — function alias");

        ctx.Output.WriteLine($"  kind:   {(entry.Kind == AliasRegistry.AliasKind.Template ? "template" : "function")}");

        string sourceInfo = SourceToString(entry.Source);
        if (entry.SourceFile is not null)
            sourceInfo += $" ({entry.SourceFile}:{entry.SourceLine ?? 0})";
        ctx.Output.WriteLine($"  source: {sourceInfo}");

        if (entry.Description is not null)
            ctx.Output.WriteLine($"  desc:   {entry.Description}");
    }

    private static int SourceOrder(AliasRegistry.AliasSource source) => source switch
    {
        AliasRegistry.AliasSource.Builtin => 0,
        AliasRegistry.AliasSource.Rc      => 1,
        AliasRegistry.AliasSource.Repl    => 2,
        AliasRegistry.AliasSource.Saved   => 3,
        _                                  => 4,
    };

    /// <summary>
    /// Converts a Stash array of values to a C# <c>string[]</c>, coercing each element
    /// to its string representation.
    /// </summary>
    private static string[] ParseStringArgs(List<StashValue> argList, string callerName)
    {
        var result = new string[argList.Count];
        for (int i = 0; i < argList.Count; i++)
        {
            StashValue sv = argList[i];
            if (sv.IsObj && sv.AsObj is string s)
                result[i] = s;
            else
                throw new RuntimeError(
                    $"each element of the args array passed to '{callerName}' must be a string.",
                    null,
                    StashErrorTypes.TypeError);
        }
        return result;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="template"/> contains at least one
    /// argument placeholder (<c>${args}</c>, <c>${args[N]}</c>, or <c>${argv}</c>).
    /// Used by the dispatcher to enforce the strict-args policy (spec §15).
    /// </summary>
    public static bool HasArgPlaceholder(string template) =>
        _argPlaceholderCheck.IsMatch(template);

    /// <summary>
    /// Substitutes <c>${args}</c>, <c>${args[N]}</c>, and <c>${argv}</c> placeholders
    /// in a template alias body, leaving other <c>${...}</c> expressions untouched.
    /// </summary>
    public static string ExpandTemplate(string aliasName, string template, string[] args)
    {
        return _substRegex.Replace(template, m =>
        {
            string full = m.Value;

            // ${argv} — Stash array literal
            if (full == "${argv}")
            {
                var sb = new StringBuilder("[");
                for (int i = 0; i < args.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append('"');
                    sb.Append(args[i].Replace("\\", "\\\\").Replace("\"", "\\\""));
                    sb.Append('"');
                }
                sb.Append(']');
                return sb.ToString();
            }

            // ${args} or ${args[N]}
            if (full.StartsWith("${args", StringComparison.Ordinal))
            {
                if (m.Groups[1].Success)
                {
                    // ${args[N]}
                    int n = int.Parse(m.Groups[1].Value);
                    if (n < 0 || n >= args.Length)
                    {
                        throw new RuntimeError(
                            $"alias '{aliasName}' references args[{n}] but only {args.Length} argument(s) were provided",
                            null,
                            StashErrorTypes.AliasError);
                    }
                    return args[n];
                }
                else
                {
                    // ${args} — shell-quoted, space-joined
                    return string.Join(" ", args.Select(ShellQuote));
                }
            }

            // Other ${...} — leave in place
            return full;
        });
    }

    /// <summary>
    /// Shell-quotes a single argument using POSIX single-quote rules.
    /// Safe arguments (no special characters) are returned unquoted.
    /// </summary>
    private static string ShellQuote(string arg)
    {
        if (arg.Length > 0 && arg.All(IsShellSafe))
            return arg;

        // Wrap in single quotes, escaping any embedded single quotes as '\''
        return "'" + arg.Replace("'", "'\\''") + "'";
    }

    private static bool IsShellSafe(char c) =>
        char.IsLetterOrDigit(c) || c is '_' or '-' or '.' or '/' or ':' or '=' or '+' or '@' or ',';
}
