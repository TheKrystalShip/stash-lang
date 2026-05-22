namespace Stash.Analysis.Cli;

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib.BuiltIns;

/// <summary>
/// Strict literal-schema builder for the <c>stash --help script.stash</c> static discovery mode.
/// </summary>
/// <remarks>
/// <para>
/// This class provides two capabilities used by <c>StaticHelpMode</c>:
/// </para>
/// <list type="number">
///   <item><description>
///     <see cref="IsLiteralExpr"/> — the strict literal-ness predicate per the P10 spec.
///     Admits exactly: integer, float, string, bool, null, IpAddress, Duration, ByteSize,
///     SemVer literals; <c>ArrayLiteral</c> (recursive); <c>DictLiteral</c> (recursive);
///     and <c>UnaryExpr(Minus, IntegerLiteral | FloatLiteral)</c> — no other unary forms.
///   </description></item>
///   <item><description>
///     <see cref="TryBuild"/> — parses source text, resolves the binding name (with optional
///     <c>// @cli-schema-binding: &lt;name&gt;</c> comment marker), and if the binding's
///     initializer is a <c>cli.schema({...})</c> call with fully-literal builder call
///     arguments, builds a runtime <c>CliSchema</c> <see cref="StashInstance"/> directly
///     from the AST without executing the script.
///   </description></item>
/// </list>
/// <para>
/// Design note: This is a separate class from <see cref="CliSchemaAnalyzer"/> (P9, LSP).
/// The LSP analyzer applies a looser check (silent degradation on non-literal entries is acceptable).
/// This class applies the exact spec-required predicate because the static-help mode must be
/// unambiguous: either it produces rendered help or it falls back — no partial output.
/// </para>
/// </remarks>
public static class LiteralSchemaBuilder
{
    // ── Comment marker ───────────────────────────────────────────────────────

    /// <summary>
    /// Regex that matches the <c>// @cli-schema-binding: &lt;name&gt;</c> comment marker.
    /// First capture group is the binding name (trimmed).
    /// The first occurrence in the source file wins; falls back to <c>schema</c> if absent.
    /// </summary>
    private static readonly Regex CliSchemaBindingMarker =
        new(@"//\s*@cli-schema-binding:\s*(\w+)", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    /// <summary>Default top-level binding name when no comment marker is present.</summary>
    internal const string DefaultBindingName = "schema";

    // ── Known builder names ──────────────────────────────────────────────────

    private static readonly HashSet<string> BuilderNames = new(StringComparer.Ordinal)
    {
        "option", "positional", "flag",
    };

    // ── Public entry point ───────────────────────────────────────────────────

    /// <summary>
    /// Attempts to build a runtime <c>CliSchema</c> from a fully-literal top-level
    /// <c>cli.schema({...})</c> binding in <paramref name="source"/>.
    /// </summary>
    /// <param name="source">Full source text of the script.</param>
    /// <param name="sourceName">File name or display name used in error messages.</param>
    /// <param name="schema">
    ///   On success, the built <see cref="StashInstance"/> whose <c>TypeName</c> is
    ///   <c>"CliSchema"</c>. On failure, <c>null</c>.
    /// </param>
    /// <returns>
    ///   <c>true</c> when a fully-literal <c>cli.schema()</c> binding was found and built;
    ///   <c>false</c> when the source has no such binding or the initializer is non-literal.
    /// </returns>
    public static bool TryBuild(string source, string sourceName, out StashInstance? schema)
    {
        schema = null;

        // Determine the binding name from the optional comment marker.
        string bindingName = ResolveBindingName(source);

        // Lex + parse the source.  Parse errors → fall back silently.
        var lexer = new Lexer(source, sourceName);
        List<Token> tokens = lexer.ScanTokens();
        if (lexer.Errors.Count > 0)
            return false;

        var parser = new Parser(tokens);
        List<Stmt> statements = parser.ParseProgram();
        if (parser.Errors.Count > 0)
            return false;

        // Find the top-level binding named <bindingName>.
        if (!TryFindSchemaBinding(statements, bindingName, out Expr? initializer))
            return false;

        // The initializer must be a literal cli.schema({...}) call.
        if (!TryExtractSchemaCall(initializer!, out DictLiteralExpr? schemaDict,
                out DictLiteralExpr? optsDictExpr))
            return false;

        // All entries in the schema dict must be cli builder calls with fully-literal args.
        if (!IsLiteralSchemaDict(schemaDict!))
            return false;

        // Build the runtime CliSchema from the literal AST.
        try
        {
            StashDictionary def = BuildSchemaDictFromAST(schemaDict!);

            // Extract program-level options (programName, description, helpFlag) if supplied.
            string programName = "";
            string description = "";
            bool helpFlag = true;
            if (optsDictExpr is not null && IsLiteralExpr(optsDictExpr))
            {
                StashDictionary optsDict = EvalDictLiteral(optsDictExpr);
                if (optsDict.Has("programName"))
                {
                    StashValue pv = optsDict.Get("programName");
                    if (pv.IsObj && pv.AsObj is string ps) programName = ps;
                }
                if (optsDict.Has("description"))
                {
                    StashValue dv = optsDict.Get("description");
                    if (dv.IsObj && dv.AsObj is string ds) description = ds;
                }
                if (optsDict.Has("helpFlag"))
                {
                    StashValue hv = optsDict.Get("helpFlag");
                    if (hv.IsBool) helpFlag = hv.AsBool;
                }
            }

            StashValue result = CliBuiltIns.BuildSchema(def, programName, description, helpFlag);
            if (result.IsObj && result.AsObj is StashInstance inst)
            {
                schema = inst;
                return true;
            }
        }
        catch
        {
            // Any runtime error building the schema → fall back silently.
        }

        return false;
    }

    // ── Comment marker resolution ────────────────────────────────────────────

    /// <summary>
    /// Scans <paramref name="source"/> for the first
    /// <c>// @cli-schema-binding: &lt;name&gt;</c> comment and returns the name.
    /// Returns <see cref="DefaultBindingName"/> (<c>"schema"</c>) when no marker is found.
    /// </summary>
    internal static string ResolveBindingName(string source)
    {
        Match m = CliSchemaBindingMarker.Match(source);
        return m.Success ? m.Groups[1].Value : DefaultBindingName;
    }

    // ── Binding finder ───────────────────────────────────────────────────────

    /// <summary>
    /// Searches <paramref name="statements"/> for a top-level <c>let</c> or <c>const</c>
    /// declaration with the given <paramref name="name"/> and returns its initializer.
    /// </summary>
    private static bool TryFindSchemaBinding(
        List<Stmt> statements,
        string name,
        out Expr? initializer)
    {
        initializer = null;
        foreach (Stmt stmt in statements)
        {
            switch (stmt)
            {
                case VarDeclStmt varDecl when varDecl.Name.Lexeme == name:
                    initializer = varDecl.Initializer;
                    return initializer is not null;
                case ConstDeclStmt constDecl when constDecl.Name.Lexeme == name:
                    initializer = constDecl.Initializer;
                    return initializer is not null;
            }
        }
        return false;
    }

    // ── cli.schema() call extractor ──────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> when <paramref name="expr"/> is a
    /// <c>cli.schema(dictLiteral)</c> or <c>cli.schema(dictLiteral, optsDict)</c> call.
    /// </summary>
    private static bool TryExtractSchemaCall(
        Expr expr,
        out DictLiteralExpr? schemaDict,
        out DictLiteralExpr? optsDict)
    {
        schemaDict = null;
        optsDict = null;

        if (expr is not CallExpr call)
            return false;

        // Must be cli.schema(...)
        if (call.Callee is not DotExpr dot)
            return false;
        if (dot.Object is not IdentifierExpr ns || ns.Name.Lexeme != "cli")
            return false;
        if (dot.Name.Lexeme != "schema")
            return false;

        // First argument must be a dict literal.
        if (call.Arguments.Count < 1 || call.Arguments[0] is not DictLiteralExpr firstArg)
            return false;

        schemaDict = firstArg;

        // Optional second argument (program-level opts dict).
        if (call.Arguments.Count >= 2 && call.Arguments[1] is DictLiteralExpr secondArg)
            optsDict = secondArg;

        return true;
    }

    // ── Schema dict literal check ────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> when every entry value in the schema dict is a
    /// <c>cli.option/flag/positional/command(...)</c> call with fully-literal arguments.
    /// </summary>
    private static bool IsLiteralSchemaDict(DictLiteralExpr dict)
    {
        foreach (var (key, value) in dict.Entries)
        {
            // Spread entries are non-literal.
            if (key is null)
                return false;

            if (!IsLiteralBuilderCallExpr(value))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="expr"/> is a
    /// <c>cli.option/flag/positional/command(...)</c> call with all fully-literal arguments.
    /// For <c>cli.command</c>, the values in the dict must each be a <c>cli.schema()</c> call
    /// with literal contents (recursive).
    /// </summary>
    private static bool IsLiteralBuilderCallExpr(Expr expr)
    {
        if (expr is not CallExpr call) return false;
        if (call.Callee is not DotExpr dot) return false;
        if (dot.Object is not IdentifierExpr ns || ns.Name.Lexeme != "cli") return false;

        string memberName = dot.Name.Lexeme;
        if (!BuilderNames.Contains(memberName)) return false;

        foreach (Expr arg in call.Arguments)
        {
            // Each argument to a builder call must be fully literal.
            // This includes string type tags and options dicts.
            if (!IsLiteralExpr(arg))
                return false;
        }
        return true;
    }

    // ── Strict literal-ness predicate ────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> when <paramref name="expr"/> is fully literal according to the
    /// P10 strict predicate.
    /// </summary>
    /// <remarks>
    /// Admitted forms:
    /// <list type="bullet">
    ///   <item><see cref="LiteralExpr"/> with any Value (integer, float, string, bool, null,
    ///     IpAddress, Duration, ByteSize, SemVer).</item>
    ///   <item><see cref="ArrayExpr"/> whose every element is literal (recursive).</item>
    ///   <item><see cref="DictLiteralExpr"/> whose every non-spread entry value is literal (recursive).</item>
    ///   <item><see cref="UnaryExpr"/> with operator <c>Minus</c> and a directly-nested
    ///     <see cref="LiteralExpr"/> whose Value is <see cref="long"/> or <see cref="double"/>.</item>
    /// </list>
    /// No other unary forms are admitted: <c>!true</c> (Bang), <c>-x</c> (non-literal operand),
    /// <c>--1</c> (nested UnaryExpr) are all non-literal.
    /// </remarks>
    public static bool IsLiteralExpr(Expr expr)
    {
        switch (expr)
        {
            case LiteralExpr:
                // Covers all literal token types: integer, float, string, bool, null,
                // IpAddressLiteral, DurationLiteral, ByteSizeLiteral, SemVerLiteral.
                return true;

            case ArrayExpr arr:
                foreach (Expr element in arr.Elements)
                {
                    if (!IsLiteralExpr(element))
                        return false;
                }
                return true;

            case DictLiteralExpr dict:
                foreach (var (key, value) in dict.Entries)
                {
                    // Spread entries (key == null) are non-literal.
                    if (key == null)
                        return false;
                    if (!IsLiteralExpr(value))
                        return false;
                }
                return true;

            case UnaryExpr unary:
                // Only admit: UnaryExpr(Minus, LiteralExpr(long | double))
                // Reject: any other operator (e.g. Bang for !true)
                // Reject: non-literal operand (e.g. -x where x is an identifier)
                // Reject: nested UnaryExpr operand (e.g. --1)
                return unary.Operator.Type == TokenType.Minus &&
                       unary.Right is LiteralExpr inner &&
                       (inner.Value is long || inner.Value is double);

            default:
                return false;
        }
    }

    // ── Runtime value builders ───────────────────────────────────────────────

    /// <summary>
    /// Converts a literal <see cref="DictLiteralExpr"/> AST node into a
    /// <see cref="StashDictionary"/> containing runtime Stash values.
    /// Assumes all values satisfy <see cref="IsLiteralExpr"/>.
    /// </summary>
    private static StashDictionary EvalDictLiteral(DictLiteralExpr dict)
    {
        var result = new StashDictionary();
        foreach (var (keyToken, valueExpr) in dict.Entries)
        {
            if (keyToken is null) continue; // skip spreads
            string key = keyToken.Type == TokenType.StringLiteral && keyToken.Literal is string strKey
                ? strKey
                : keyToken.Lexeme;
            StashValue val = EvalLiteralExpr(valueExpr);
            result.Set(key, val);
        }
        return result;
    }

    /// <summary>
    /// Evaluates a fully-literal expression to a <see cref="StashValue"/>.
    /// Caller must have already verified <see cref="IsLiteralExpr"/> returned <c>true</c>.
    /// </summary>
    private static StashValue EvalLiteralExpr(Expr expr)
    {
        switch (expr)
        {
            case LiteralExpr lit:
                return lit.Value switch
                {
                    null         => StashValue.Null,
                    bool b       => StashValue.FromBool(b),
                    long i       => StashValue.FromInt(i),
                    double d     => StashValue.FromFloat(d),
                    string s     => StashValue.FromObj(s),
                    object obj   => StashValue.FromObj(obj),
                };

            case ArrayExpr arr:
            {
                var list = new List<StashValue>(arr.Elements.Count);
                foreach (Expr el in arr.Elements)
                    list.Add(EvalLiteralExpr(el));
                return StashValue.FromObj(list);
            }

            case DictLiteralExpr dict:
                return StashValue.FromObj(EvalDictLiteral(dict));

            case UnaryExpr unary:
                // Only Minus on a numeric literal is admitted by IsLiteralExpr.
                StashValue operand = EvalLiteralExpr(unary.Right);
                if (operand.IsInt)   return StashValue.FromInt(-operand.AsInt);
                if (operand.IsFloat) return StashValue.FromFloat(-operand.AsFloat);
                return StashValue.Null; // unreachable per predicate

            default:
                return StashValue.Null; // unreachable per predicate
        }
    }

    /// <summary>
    /// Builds a <see cref="StashDictionary"/> from the schema dict AST where each value
    /// is a builder call converted to a <see cref="StashInstance"/> (<c>CliArgSpec</c> or
    /// <c>CliCommandSpec</c>).
    /// </summary>
    private static StashDictionary BuildSchemaDictFromAST(DictLiteralExpr dict)
    {
        var result = new StashDictionary();
        foreach (var (keyToken, valueExpr) in dict.Entries)
        {
            if (keyToken is null) continue;
            string propName = keyToken.Type == TokenType.StringLiteral && keyToken.Literal is string sk
                ? sk
                : keyToken.Lexeme;

            StashValue specValue = EvalBuilderCall((CallExpr)valueExpr, propName);
            result.Set(propName, specValue);
        }
        return result;
    }

    /// <summary>
    /// Evaluates a single <c>cli.option/flag/positional/command(...)</c> call node to the
    /// corresponding <c>CliArgSpec</c> or <c>CliCommandSpec</c> runtime value.
    /// </summary>
    private static StashValue EvalBuilderCall(CallExpr call, string propName)
    {
        string memberName = ((DotExpr)call.Callee).Name.Lexeme;
        List<Expr> args = call.Arguments;

        switch (memberName)
        {
            case "option":
            {
                // cli.option(typeTag: string, opts?: dict)
                if (args.Count < 1) return StashValue.Null;
                string typeTag = EvalLiteralExpr(args[0]) is var tv && tv.IsObj && tv.AsObj is string ts
                    ? ts : "";
                StashDictionary? opts = args.Count >= 2 && args[1] is DictLiteralExpr od
                    ? EvalDictLiteral(od) : null;
                return CliBuiltIns.MakeArgSpecInstance(
                    kind: "option",
                    typeTag: typeTag,
                    nameOverride: opts?.Has("name") == true ? GetStringOpt(opts!, "name") : null,
                    shortOpt: opts is not null ? GetStringOpt(opts, "short") : null,
                    aliases: opts?.Has("aliases") == true ? GetListOpt(opts!, "aliases") : [],
                    required: opts?.Has("required") == true ? GetBoolOpt(opts!, "required") : false,
                    defaultVal: opts is not null ? GetDefaultOpt(opts) : StashValue.Null,
                    repeated: opts?.Has("repeated") == true ? GetBoolOpt(opts!, "repeated") : false,
                    choices: opts?.Has("choices") == true ? GetListOptNullable(opts!, "choices") : null,
                    min: opts is not null && opts.Has("min") ? opts.Get("min") : StashValue.Null,
                    max: opts is not null && opts.Has("max") ? opts.Get("max") : StashValue.Null,
                    pattern: opts is not null ? GetStringOpt(opts, "pattern") : null,
                    validate: StashValue.Null,
                    help: opts is not null ? GetStringOpt(opts, "help") : null,
                    metavar: opts is not null ? GetStringOpt(opts, "metavar") : null,
                    env: opts is not null ? GetStringOpt(opts, "env") : null,
                    negatable: false);
            }

            case "positional":
            {
                // cli.positional(typeTag: string, opts?: dict)
                if (args.Count < 1) return StashValue.Null;
                string typeTag = EvalLiteralExpr(args[0]) is var tv2 && tv2.IsObj && tv2.AsObj is string ts2
                    ? ts2 : "";
                StashDictionary? opts = args.Count >= 2 && args[1] is DictLiteralExpr od
                    ? EvalDictLiteral(od) : null;
                return CliBuiltIns.MakeArgSpecInstance(
                    kind: "positional",
                    typeTag: typeTag,
                    nameOverride: opts is not null ? GetStringOpt(opts, "name") : null,
                    shortOpt: null,
                    aliases: [],
                    required: opts?.Has("required") == true ? GetBoolOpt(opts!, "required") : true,
                    defaultVal: opts is not null ? GetDefaultOpt(opts) : StashValue.Null,
                    repeated: opts?.Has("repeated") == true ? GetBoolOpt(opts!, "repeated") : false,
                    choices: opts?.Has("choices") == true ? GetListOptNullable(opts!, "choices") : null,
                    min: StashValue.Null,
                    max: StashValue.Null,
                    pattern: null,
                    validate: StashValue.Null,
                    help: opts is not null ? GetStringOpt(opts, "help") : null,
                    metavar: opts is not null ? GetStringOpt(opts, "metavar") : null,
                    env: null,
                    negatable: false);
            }

            case "flag":
            {
                // cli.flag(opts?: dict)
                StashDictionary? opts = args.Count >= 1 && args[0] is DictLiteralExpr od
                    ? EvalDictLiteral(od) : null;
                bool defaultBool = false;
                if (opts?.Has("defaultVal") == true || opts?.Has("default") == true)
                {
                    StashValue dv = GetDefaultOpt(opts!);
                    if (dv.IsBool) defaultBool = dv.AsBool;
                }
                return CliBuiltIns.MakeArgSpecInstance(
                    kind: "flag",
                    typeTag: "bool",
                    nameOverride: opts is not null ? GetStringOpt(opts, "name") : null,
                    shortOpt: opts is not null ? GetStringOpt(opts, "short") : null,
                    aliases: opts?.Has("aliases") == true ? GetListOpt(opts!, "aliases") : [],
                    required: false,
                    defaultVal: StashValue.FromBool(defaultBool),
                    repeated: false,
                    choices: null,
                    min: StashValue.Null,
                    max: StashValue.Null,
                    pattern: null,
                    validate: StashValue.Null,
                    help: opts is not null ? GetStringOpt(opts, "help") : null,
                    metavar: null,
                    env: null,
                    negatable: opts?.Has("negatable") == true && GetBoolOpt(opts!, "negatable"));
            }

            default:
                return StashValue.Null;
        }
    }

    // ── Mini helpers mirroring CliDictExtensions ─────────────────────────────

    private static string? GetStringOpt(StashDictionary d, string key)
    {
        if (!d.Has(key)) return null;
        StashValue v = d.Get(key);
        return v.IsObj && v.AsObj is string s ? s : null;
    }

    private static bool GetBoolOpt(StashDictionary d, string key)
    {
        if (!d.Has(key)) return false;
        StashValue v = d.Get(key);
        return v.IsBool && v.AsBool;
    }

    private static List<StashValue> GetListOpt(StashDictionary d, string key)
    {
        if (!d.Has(key)) return [];
        StashValue v = d.Get(key);
        return v.IsObj && v.AsObj is List<StashValue> list ? list : [];
    }

    private static List<StashValue>? GetListOptNullable(StashDictionary d, string key)
    {
        if (!d.Has(key)) return null;
        StashValue v = d.Get(key);
        return v.IsObj && v.AsObj is List<StashValue> list ? list : null;
    }

    private static StashValue GetDefaultOpt(StashDictionary d)
    {
        if (d.Has("defaultVal")) return d.Get("defaultVal");
        if (d.Has("default")) return d.Get("default");
        return StashValue.Null;
    }
}
