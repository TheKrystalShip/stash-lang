namespace Stash.Analysis.Cli;

using System.Collections.Generic;
using Stash.Common;
using Stash.Parsing.AST;

/// <summary>
/// Statically analyses literal <c>cli.schema({...})</c> constructs in a script's top-level
/// statements, producing:
/// <list type="bullet">
///   <item><description>A <see cref="CliSchemaIndex"/> mapping parse-result identifiers to their declared fields.</description></item>
///   <item><description>Diagnostics for duplicate <c>short:</c> values and unknown type-tag strings inside literal schemas.</description></item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// The analyser only acts on schemas that are <em>literal</em> — i.e. the argument to
/// <c>cli.schema()</c> is a dict-literal whose entries are all calls to the builder functions
/// (<c>cli.option</c>, <c>cli.flag</c>, <c>cli.positional</c>). Dynamically-constructed schemas
/// (e.g. a variable passed as the argument, or entries produced by computed expressions) are
/// silently skipped — no diagnostics, no index entries.
/// </para>
/// <para>
/// A <c>cli.parse(schema)</c> call where <c>schema</c> is a top-level identifier bound to a
/// literal <c>cli.schema({...})</c> is tracked: the result variable name (if the call appears
/// as an initializer of a <c>let</c>/<c>const</c>) is mapped to the schema's declared fields
/// for hover and completion use.
/// </para>
/// </remarks>
public static class CliSchemaAnalyzer
{
    // ── Known type tags (mirrors CliBuiltIns.KnownTypeTags) ─────────────────
    // TODO: deduplicate with Stash.Stdlib.BuiltIns.CliBuiltIns.KnownTypeTags once
    //       a shared metadata surface exists.
    private static readonly HashSet<string> KnownTypeTags = new(System.StringComparer.Ordinal)
    {
        "string", "int", "float", "bool", "duration", "ip", "bytesize", "semver",
    };

    // ── Builder function names ───────────────────────────────────────────────
    private static readonly HashSet<string> BuilderNames = new(System.StringComparer.Ordinal)
    {
        "option", "positional", "flag", "command",
    };

    /// <summary>
    /// Runs the cli-schema analysis pass over the top-level <paramref name="statements"/>.
    /// </summary>
    /// <param name="statements">The top-level AST statements to scan.</param>
    /// <param name="diagnostics">Mutable list to append any SA1501/SA1502 diagnostics to.</param>
    /// <returns>A <see cref="CliSchemaIndex"/> with the discovered literal-schema bindings.</returns>
    public static CliSchemaIndex Analyze(List<Stmt> statements, List<SemanticDiagnostic> diagnostics)
    {
        // Pass 1 — collect top-level identifier → literal cli.schema() bindings
        var schemaBound = new Dictionary<string, CliSchemaInfo>();
        foreach (var stmt in statements)
        {
            if (!TryGetDeclaredName(stmt, out var name, out var init) || init == null)
                continue;

            if (!IsCliSchemaCall(init, out var dictArg))
                continue;

            // dictArg is the DictLiteralExpr passed to cli.schema(). Analyse it.
            var (fields, schemaDiags) = AnalyseSchemaDict(dictArg!);
            diagnostics.AddRange(schemaDiags);
            schemaBound[name!] = new CliSchemaInfo(fields);
        }

        // Pass 2 — find cli.parse(schemaIdent) bindings to map result variables
        var index = new CliSchemaIndex();
        foreach (var stmt in statements)
        {
            if (!TryGetDeclaredName(stmt, out var resultName, out var init) || init == null)
                continue;

            if (!IsCliParseCall(init, out var schemaName))
                continue;

            if (schemaName != null && schemaBound.TryGetValue(schemaName, out var schemaInfo))
            {
                index.Add(resultName!, schemaInfo);
            }
        }

        return index;
    }

    // ── Pass helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the declared name and initializer from a <c>let</c> or <c>const</c> statement.
    /// </summary>
    private static bool TryGetDeclaredName(Stmt stmt, out string? name, out Expr? init)
    {
        switch (stmt)
        {
            case VarDeclStmt varDecl:
                name = varDecl.Name.Lexeme;
                init = varDecl.Initializer;
                return true;
            case ConstDeclStmt constDecl:
                name = constDecl.Name.Lexeme;
                init = constDecl.Initializer;
                return true;
            default:
                name = null;
                init = null;
                return false;
        }
    }

    /// <summary>
    /// Returns true when <paramref name="expr"/> is a literal <c>cli.schema(dictLiteral)</c> call.
    /// </summary>
    private static bool IsCliSchemaCall(Expr expr, out DictLiteralExpr? dictArg)
    {
        dictArg = null;
        if (expr is not CallExpr call)
            return false;
        if (!IsCliMemberCall(call.Callee, "schema"))
            return false;
        if (call.Arguments.Count != 1)
            return false;
        if (call.Arguments[0] is not DictLiteralExpr dict)
            return false;
        dictArg = dict;
        return true;
    }

    /// <summary>
    /// Returns true when <paramref name="expr"/> is a <c>cli.parse(identifier)</c> call.
    /// </summary>
    private static bool IsCliParseCall(Expr expr, out string? schemaIdentifier)
    {
        schemaIdentifier = null;
        if (expr is not CallExpr call)
            return false;
        if (!IsCliMemberCall(call.Callee, "parse"))
            return false;
        // Accept both cli.parse(schemaIdent) and cli.parse(schemaIdent, argv)
        if (call.Arguments.Count < 1)
            return false;
        if (call.Arguments[0] is not IdentifierExpr identExpr)
            return false;
        schemaIdentifier = identExpr.Name.Lexeme;
        return true;
    }

    /// <summary>
    /// Returns true when <paramref name="callee"/> is a dot expression of the form <c>cli.&lt;memberName&gt;</c>.
    /// </summary>
    private static bool IsCliMemberCall(Expr callee, string memberName)
    {
        return callee is DotExpr dot &&
               dot.Object is IdentifierExpr ns &&
               ns.Name.Lexeme == "cli" &&
               dot.Name.Lexeme == memberName;
    }

    // ── Schema dict analysis ─────────────────────────────────────────────────

    /// <summary>
    /// Walks a literal <c>cli.schema({...})</c> dict, collecting field names and emitting
    /// diagnostics for duplicate shorts and unknown type tags.
    /// </summary>
    private static (List<CliFieldInfo> Fields, List<SemanticDiagnostic> Diagnostics)
        AnalyseSchemaDict(DictLiteralExpr dict)
    {
        var fields = new List<CliFieldInfo>();
        var diagnostics = new List<SemanticDiagnostic>();

        // Track seen short values for duplicate detection: shortChar -> first span of the value expr
        var seenShorts = new Dictionary<string, SourceSpan>(System.StringComparer.Ordinal);

        foreach (var (keyToken, valueExpr) in dict.Entries)
        {
            // Null key = spread entry — skip
            if (keyToken == null)
                continue;

            string fieldName = GetTokenText(keyToken);

            // Determine field type from the builder call
            string? typeTag = null;
            string? shortVal = null;
            SourceSpan? typeTagSpan = null;
            SourceSpan? shortSpan = null;

            if (valueExpr is CallExpr builderCall && IsCliBuilderCall(builderCall, out var builderName))
            {
                (typeTag, typeTagSpan, shortVal, shortSpan) =
                    ExtractBuilderInfo(builderName!, builderCall);
            }
            else
            {
                // Non-literal value — skip diagnostics for this entry but still record it
                fields.Add(new CliFieldInfo(fieldName, typeTag: null));
                continue;
            }

            // Check unknown type tag
            if (typeTag != null && typeTagSpan.HasValue &&
                !KnownTypeTags.Contains(typeTag))
            {
                diagnostics.Add(DiagnosticDescriptors.SA1502.CreateDiagnostic(
                    typeTagSpan.Value, typeTag));
            }

            // Check duplicate short
            if (shortVal != null && shortSpan.HasValue)
            {
                if (seenShorts.TryGetValue(shortVal, out _))
                {
                    diagnostics.Add(DiagnosticDescriptors.SA1501.CreateDiagnostic(
                        shortSpan.Value, shortVal));
                }
                else
                {
                    seenShorts[shortVal] = shortSpan.Value;
                }
            }

            fields.Add(new CliFieldInfo(fieldName, typeTag));
        }

        return (fields, diagnostics);
    }

    /// <summary>
    /// Returns true when <paramref name="call"/> is a call to one of the cli builder functions
    /// (cli.option, cli.flag, cli.positional, cli.command).
    /// </summary>
    private static bool IsCliBuilderCall(CallExpr call, out string? builderName)
    {
        builderName = null;
        if (call.Callee is not DotExpr dot)
            return false;
        if (dot.Object is not IdentifierExpr ns || ns.Name.Lexeme != "cli")
            return false;
        if (!BuilderNames.Contains(dot.Name.Lexeme))
            return false;
        builderName = dot.Name.Lexeme;
        return true;
    }

    /// <summary>
    /// Extracts type-tag and short-option info from a builder call.
    /// </summary>
    /// <returns>Tuple of (typeTag, typeTagSpan, shortVal, shortSpan), any of which may be null.</returns>
    private static (string? TypeTag, SourceSpan? TypeTagSpan, string? ShortVal, SourceSpan? ShortSpan)
        ExtractBuilderInfo(string builderName, CallExpr call)
    {
        string? typeTag = null;
        SourceSpan? typeTagSpan = null;
        string? shortVal = null;
        SourceSpan? shortSpan = null;

        switch (builderName)
        {
            case "option":
            case "positional":
                // cli.option(typeTag: string, opts?: dict)
                // cli.positional(typeTag: string, opts?: dict)
                if (call.Arguments.Count >= 1 && call.Arguments[0] is LiteralExpr tagLit && tagLit.Value is string tag)
                {
                    typeTag = tag;
                    typeTagSpan = tagLit.Span;
                }
                // Look for short in opts dict (second argument)
                if (call.Arguments.Count >= 2)
                {
                    (shortVal, shortSpan) = ExtractShortFromOptsArg(call.Arguments[1]);
                }
                break;

            case "flag":
                // cli.flag(opts?: dict) — no typeTag arg; type is always "bool"
                typeTag = "bool";
                // typeTagSpan stays null — no literal to point at
                if (call.Arguments.Count >= 1)
                {
                    (shortVal, shortSpan) = ExtractShortFromOptsArg(call.Arguments[0]);
                }
                break;

            case "command":
                // cli.command(...) — type tag not applicable
                typeTag = null;
                break;
        }

        return (typeTag, typeTagSpan, shortVal, shortSpan);
    }

    /// <summary>
    /// Looks inside an options dict literal for a <c>short: "x"</c> entry.
    /// Returns (shortChar, span) if found as a literal, otherwise (null, null).
    /// </summary>
    private static (string? ShortVal, SourceSpan? ShortSpan) ExtractShortFromOptsArg(Expr optsArg)
    {
        if (optsArg is not DictLiteralExpr optsDict)
            return (null, null);

        foreach (var (key, val) in optsDict.Entries)
        {
            if (key == null)
                continue;
            string keyText = GetTokenText(key);
            if (keyText != "short")
                continue;
            if (val is LiteralExpr litVal && litVal.Value is string shortStr)
                return (shortStr, litVal.Span);
            // Non-literal short value — skip diagnostic for this entry
            break;
        }

        return (null, null);
    }

    // ── Token text helper ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns the textual content of a dict key token.
    /// For identifier tokens, returns <see cref="Token.Lexeme"/> directly.
    /// For string tokens, returns the string value (stripped of quotes via <see cref="Token.Literal"/>).
    /// </summary>
    private static string GetTokenText(Stash.Lexing.Token key)
    {
        if (key.Type == Stash.Lexing.TokenType.StringLiteral && key.Literal is string strLit)
            return strLit;
        return key.Lexeme;
    }
}
