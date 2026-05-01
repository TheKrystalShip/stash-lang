namespace Stash.Cli.Shell;

using System;
using System.Collections.Generic;
using System.Text;
using Stash.Runtime;

/// <summary>
/// Desugars <c>alias</c> and <c>unalias</c> shell-mode lines into Stash source snippets
/// that are then compiled and evaluated against the REPL VM.
///
/// <para>
/// Supported forms (Phase C + Phase F):
/// <list type="bullet">
///   <item><c>alias</c>                                 → <c>alias.__listPretty()</c></item>
///   <item><c>alias --help</c>                          → <c>io.println(&lt;help text&gt;)</c></item>
///   <item><c>alias --save &lt;name&gt; = &lt;body&gt;</c> → <c>{ alias.define(...); alias.save("name"); }</c></item>
///   <item><c>alias &lt;name&gt;</c>                    → <c>alias.__getPretty("name")</c></item>
///   <item><c>alias &lt;name&gt; = &lt;body&gt;</c>     → <c>alias.define("name", "body")</c></item>
///   <item><c>alias &lt;name&gt; = (params) =&gt; &lt;expr&gt;</c> → <c>alias.define("name", (params) =&gt; expr)</c></item>
///   <item><c>alias &lt;name&gt; = (params) =&gt; { stmts }</c> → <c>alias.define("name", (params) =&gt; { stmts })</c></item>
///   <item><c>unalias &lt;name&gt;</c>                  → <c>alias.remove("name")</c></item>
///   <item><c>unalias --all</c>                         → <c>alias.clear()</c></item>
///   <item><c>unalias --save &lt;name&gt;</c>           → <c>{ alias.remove("name"); alias.__removeSaved("name"); }</c></item>
///   <item><c>unalias --force &lt;name&gt;</c>          → <c>alias.__forceDisable("name")</c></item>
/// </list>
/// </para>
/// </summary>
internal static class AliasShellSugar
{
    // ── Help text ─────────────────────────────────────────────────────────────

    private static readonly string HelpText = BuildHelpText();

    // ── Public entry points ───────────────────────────────────────────────────

    /// <summary>
    /// Desugars an <c>alias</c> command line. Returns Stash source or <see langword="null"/>
    /// if the form was not recognised (falls through to PATH lookup / error).
    /// Throws <see cref="RuntimeError"/> for structurally invalid inputs (e.g. multi-word
    /// unquoted body, Phase F stubs).
    /// </summary>
    public static string? TryDesugarAlias(ShellCommandLine line, IReadOnlyList<string> _expandedArgs)
    {
        string raw = line.Stages[0].RawArgs.Trim();

        // Case 1: no args → list all aliases
        if (raw.Length == 0)
            return "alias.__listPretty();";

        // Case 2: --help
        if (StartsWithFlag(raw, "--help"))
            return $"io.println(\"{EscapeBodyForStash(HelpText)}\");";

        // Case 3: --save <name> = <body>  →  alias.define(...); alias.save("name");
        if (StartsWithFlag(raw, "--save"))
        {
            string afterSave = raw["--save".Length..].TrimStart();
            if (afterSave.Length == 0)
                throw new RuntimeError(
                    "alias --save: missing alias definition after '--save'",
                    null, StashErrorTypes.CommandError);

            int j = 0;
            string? saveName = ReadIdentifier(afterSave, ref j);
            if (saveName is null)
                throw new RuntimeError(
                    "alias --save: expected alias name",
                    null, StashErrorTypes.CommandError);

            SkipWhitespace(afterSave, ref j);
            if (j >= afterSave.Length)
                throw new RuntimeError(
                    $"alias --save: missing '=' or '(' after alias name '{saveName}'",
                    null, StashErrorTypes.CommandError);

            string? defineSource = afterSave[j] == '='
                ? DesugarBodyAfterEq(saveName, afterSave, j + 1)
                : null;

            if (defineSource is null)
                throw new RuntimeError(
                    $"alias --save: expected '=' after alias name '{saveName}'",
                    null, StashErrorTypes.CommandError);

            string escapedSaveName = ShellSugarDesugarer.EscapeForStashString(saveName);
            return $"{{ {defineSource} alias.save(\"{escapedSaveName}\"); }}";
        }

        // Parse the alias name
        int i = 0;
        string? name = ReadIdentifier(raw, ref i);
        if (name is null) return null;

        SkipWhitespace(raw, ref i);

        // Case 4: `alias <name>` alone → inspect single alias
        if (i >= raw.Length)
            return $"alias.__getPretty(\"{ShellSugarDesugarer.EscapeForStashString(name)}\");";

        // Case 5: `alias <name> = <body>` — body is either a template string
        //         or a lambda `(params) => expr | { stmts }`.
        if (raw[i] == '=')
            return DesugarBodyAfterEq(name, raw, i + 1);

        return null;
    }

    /// <summary>
    /// Dispatches to lambda-form or template-form desugaring based on what follows '='.
    /// A body that begins with '(' AND has '=&gt;' after the matching ')' is treated as a
    /// lambda function alias; otherwise it is a template alias.
    /// </summary>
    private static string DesugarBodyAfterEq(string name, string raw, int afterEqPos)
    {
        int peek = afterEqPos;
        SkipWhitespace(raw, ref peek);
        if (peek < raw.Length && raw[peek] == '(' && IsLambdaShape(raw, peek))
            return DesugarLambdaForm(name, raw, peek);
        return DesugarTemplateForm(name, raw, afterEqPos);
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="raw"/> at <paramref name="openParenPos"/>
    /// looks like a lambda parameter list — i.e. <c>(...)</c> followed (after optional whitespace)
    /// by <c>=&gt;</c>.
    /// </summary>
    private static bool IsLambdaShape(string raw, int openParenPos)
    {
        int closeParen = FindMatchingClose(raw, openParenPos, '(', ')');
        if (closeParen < 0) return false;
        int p = closeParen + 1;
        SkipWhitespace(raw, ref p);
        return p + 1 < raw.Length && raw[p] == '=' && raw[p + 1] == '>';
    }

    /// <summary>
    /// Desugars an <c>unalias</c> command line. Returns Stash source or <see langword="null"/>.
    /// Throws <see cref="RuntimeError"/> for stub commands (Phase D/F).
    /// </summary>
    public static string? TryDesugarUnalias(ShellCommandLine line, IReadOnlyList<string> _expandedArgs)
    {
        string raw = line.Stages[0].RawArgs.Trim();

        if (raw.Length == 0)
            throw new RuntimeError(
                "unalias: missing alias name; use 'unalias --all' to clear all aliases",
                null, StashErrorTypes.CommandError);

        // --all → clear all user aliases
        if (raw.Equals("--all", StringComparison.Ordinal))
            return "alias.clear();";

        // --save <name>  →  alias.remove("name"); alias.__removeSaved("name");
        if (StartsWithFlag(raw, "--save"))
        {
            string remainder = raw["--save".Length..].Trim();
            int j = 0;
            string? saveName = ReadIdentifier(remainder, ref j);
            SkipWhitespace(remainder, ref j);
            if (saveName is null || j < remainder.Length)
                throw new RuntimeError(
                    "unalias --save: usage: unalias --save <name>",
                    null, StashErrorTypes.CommandError);
            string escaped = ShellSugarDesugarer.EscapeForStashString(saveName);
            return $"{{ alias.remove(\"{escaped}\"); alias.__removeSaved(\"{escaped}\"); }}";
        }

        // --force <name> → session-disable a built-in alias for the current session
        if (StartsWithFlag(raw, "--force"))
        {
            string remainder = raw["--force".Length..].Trim();
            int j = 0;
            string? forceName = ReadIdentifier(remainder, ref j);
            SkipWhitespace(remainder, ref j);
            if (forceName is null || j < remainder.Length)
                throw new RuntimeError(
                    "unalias --force: usage: unalias --force <name>",
                    null, StashErrorTypes.CommandError);
            string escaped = ShellSugarDesugarer.EscapeForStashString(forceName);
            return $"alias.__forceDisable(\"{escaped}\");";
        }

        // unalias <name>
        int i = 0;
        string? name = ReadIdentifier(raw, ref i);
        if (name is null) return null;

        SkipWhitespace(raw, ref i);
        if (i < raw.Length) return null; // trailing garbage → not our form

        string escapedName = ShellSugarDesugarer.EscapeForStashString(name);
        return $"alias.remove(\"{escapedName}\");";
    }

    // ── Template-form desugaring ──────────────────────────────────────────────

    private static string DesugarTemplateForm(string name, string raw, int afterEqPos)
    {
        int pos = afterEqPos;
        SkipWhitespace(raw, ref pos);

        if (pos >= raw.Length)
            throw new RuntimeError(
                $"alias: missing body after '=' for alias '{name}'",
                null, StashErrorTypes.CommandError);

        string body;
        if (raw[pos] == '"')
        {
            (body, pos) = ExtractDoubleQuoted(raw, pos);
        }
        else if (raw[pos] == '\'')
        {
            (body, pos) = ExtractSingleQuoted(raw, pos);
        }
        else
        {
            // Bare word: scan until whitespace
            int start = pos;
            while (pos < raw.Length && !char.IsWhiteSpace(raw[pos]))
                pos++;
            body = raw[start..pos];
        }

        // Multi-word unquoted body is a parse error (spec §5.1)
        string trailing = raw[pos..].Trim();
        if (trailing.Length > 0)
        {
            throw new RuntimeError(
                $"alias: body must be quoted; use: alias {name} = \"{EscapeQuotedInMessage(body + " " + trailing)}\"",
                null, StashErrorTypes.ParseError);
        }

        // Emit Stash source: body is escaped for a Stash string literal.
        // In particular '$' is escaped as '\$' so that ${args} placeholders
        // are not evaluated as Stash string interpolations at define time.
        string escapedName = ShellSugarDesugarer.EscapeForStashString(name);
        string escapedBody = EscapeBodyForStash(body);
        return $"alias.define(\"{escapedName}\", \"{escapedBody}\");";
    }

    // ── Lambda-form desugaring ────────────────────────────────────────────────

    /// <summary>
    /// Desugars a lambda function alias of the form
    /// <c>(params) =&gt; expr</c> or <c>(params) =&gt; { stmts }</c>, starting at the
    /// opening parenthesis at <paramref name="parenPos"/>.
    /// </summary>
    private static string DesugarLambdaForm(string name, string raw, int parenPos)
    {
        // raw[parenPos] == '(' and IsLambdaShape has already verified the trailing '=>'.
        int closeParen = FindMatchingClose(raw, parenPos, '(', ')');
        if (closeParen < 0)
            throw new RuntimeError(
                $"alias: unmatched '(' in alias '{name}' definition",
                null, StashErrorTypes.ParseError);

        string paramList = raw[(parenPos + 1)..closeParen];

        int i = closeParen + 1;
        SkipWhitespace(raw, ref i);

        // Skip the '=>' arrow (presence guaranteed by IsLambdaShape).
        if (i + 1 >= raw.Length || raw[i] != '=' || raw[i + 1] != '>')
            throw new RuntimeError(
                $"alias: expected '=>' after parameter list for alias '{name}'",
                null, StashErrorTypes.ParseError);
        i += 2;
        SkipWhitespace(raw, ref i);

        if (i >= raw.Length)
            throw new RuntimeError(
                $"alias: missing body after '=>' for alias '{name}'",
                null, StashErrorTypes.ParseError);

        string escapedName = ShellSugarDesugarer.EscapeForStashString(name);

        if (raw[i] == '{')
        {
            // Block body: find matching closing brace
            int closeBrace = FindMatchingClose(raw, i, '{', '}');
            if (closeBrace < 0)
                throw new RuntimeError(
                    $"alias: unmatched '{{' in alias '{name}' definition",
                    null, StashErrorTypes.ParseError);

            string block = raw[i..(closeBrace + 1)];
            return $"alias.define(\"{escapedName}\", ({paramList}) => {block});";
        }

        // Expression body: everything from '=>' to end of line
        string exprBody = raw[i..].Trim();
        // Strip optional trailing semicolon
        if (exprBody.EndsWith(";", StringComparison.Ordinal))
            exprBody = exprBody[..^1].TrimEnd();

        if (exprBody.Length == 0)
            throw new RuntimeError(
                $"alias: missing body after '=>' for alias '{name}'",
                null, StashErrorTypes.ParseError);

        return $"alias.define(\"{escapedName}\", ({paramList}) => {exprBody});";
    }

    // ── String scanning helpers ───────────────────────────────────────────────

    /// <summary>
    /// Extracts the content of a double-quoted string starting at <paramref name="openPos"/>
    /// (which must be a <c>"</c> character). Handles backslash escape sequences.
    /// Returns the unquoted content and the position immediately after the closing quote.
    /// </summary>
    private static (string Content, int EndPos) ExtractDoubleQuoted(string s, int openPos)
    {
        int i = openPos + 1; // skip opening '"'
        var sb = new StringBuilder();
        while (i < s.Length && s[i] != '"')
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                char next = s[i + 1];
                switch (next)
                {
                    case '"':  sb.Append('"');  i += 2; break;
                    case '\\': sb.Append('\\'); i += 2; break;
                    case 'n':  sb.Append('\n'); i += 2; break;
                    case 't':  sb.Append('\t'); i += 2; break;
                    case 'r':  sb.Append('\r'); i += 2; break;
                    case '$':  sb.Append('$');  i += 2; break; // \$ → literal $
                    default:   sb.Append('\\'); sb.Append(next); i += 2; break;
                }
                continue;
            }
            sb.Append(s[i]);
            i++;
        }
        if (i < s.Length) i++; // skip closing '"'
        return (sb.ToString(), i);
    }

    /// <summary>
    /// Extracts the content of a single-quoted string starting at <paramref name="openPos"/>.
    /// Only <c>\'</c> is treated as an escape sequence inside single quotes.
    /// Returns the unquoted content and the position immediately after the closing quote.
    /// </summary>
    private static (string Content, int EndPos) ExtractSingleQuoted(string s, int openPos)
    {
        int i = openPos + 1; // skip opening '\''
        var sb = new StringBuilder();
        while (i < s.Length && s[i] != '\'')
        {
            if (s[i] == '\\' && i + 1 < s.Length && s[i + 1] == '\'')
            {
                sb.Append('\'');
                i += 2;
                continue;
            }
            sb.Append(s[i]);
            i++;
        }
        if (i < s.Length) i++; // skip closing '\''
        return (sb.ToString(), i);
    }

    /// <summary>
    /// Finds the position of the matching closing delimiter for the delimiter at
    /// <paramref name="openPos"/>. Respects nesting and skips content inside string
    /// literals. Returns -1 if unmatched.
    /// </summary>
    private static int FindMatchingClose(string s, int openPos, char open, char close)
    {
        int depth = 1;
        int i = openPos + 1;
        bool inDouble = false;
        bool inSingle = false;

        while (i < s.Length)
        {
            char c = s[i];

            if (inDouble)
            {
                if (c == '\\' && i + 1 < s.Length) { i += 2; continue; }
                if (c == '"') inDouble = false;
                i++;
                continue;
            }

            if (inSingle)
            {
                if (c == '\\' && i + 1 < s.Length && s[i + 1] == '\'') { i += 2; continue; }
                if (c == '\'') inSingle = false;
                i++;
                continue;
            }

            if (c == '"') { inDouble = true; i++; continue; }
            if (c == '\'') { inSingle = true; i++; continue; }
            if (c == '\\' && i + 1 < s.Length) { i += 2; continue; }

            if (c == open)  { depth++; i++; continue; }
            if (c == close) { depth--; if (depth == 0) return i; }

            i++;
        }

        return -1; // unmatched
    }

    // ── Identifier and whitespace helpers ─────────────────────────────────────

    private static string? ReadIdentifier(string s, ref int pos)
    {
        SkipWhitespace(s, ref pos);
        int start = pos;
        while (pos < s.Length && (char.IsLetterOrDigit(s[pos]) || s[pos] == '_'))
            pos++;
        return pos > start ? s[start..pos] : null;
    }

    private static void SkipWhitespace(string s, ref int pos)
    {
        while (pos < s.Length && char.IsWhiteSpace(s[pos]))
            pos++;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="raw"/> starts with
    /// <paramref name="flag"/> and is either the entire string or followed by whitespace.
    /// </summary>
    private static bool StartsWithFlag(string raw, string flag)
        => raw.StartsWith(flag, StringComparison.Ordinal)
           && (raw.Length == flag.Length || char.IsWhiteSpace(raw[flag.Length]));

    // ── String escaping ───────────────────────────────────────────────────────

    /// <summary>
    /// Escapes a template alias body for embedding inside a Stash double-quoted string literal.
    /// In addition to the standard C escape sequences, <c>$</c> is escaped to <c>\$</c> so
    /// that <c>${args}</c> placeholders are NOT evaluated as Stash string interpolations at
    /// alias-definition time — they are preserved for the alias expansion engine at
    /// invocation time.
    /// </summary>
    internal static string EscapeBodyForStash(string body)
    {
        var sb = new StringBuilder(body.Length + 4);
        foreach (char c in body)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"':  sb.Append("\\\""); break;
                case '$':  sb.Append("\\$");  break;
                case '\n': sb.Append("\\n");  break;
                case '\r': sb.Append("\\r");  break;
                case '\t': sb.Append("\\t");  break;
                default:   sb.Append(c);      break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Escapes a string for embedding inside a double-quoted error message string.
    /// Only escapes <c>"</c> as <c>\"</c>.
    /// </summary>
    private static string EscapeQuotedInMessage(string s)
        => s.Replace("\"", "\\\"");

    // ── Help text ─────────────────────────────────────────────────────────────

    private static string BuildHelpText()
    {
        return string.Join("\n",
            "alias — shell alias management",
            "",
            "Usage:",
            "  alias                                List all registered aliases",
            "  alias <name>                         Show a single alias definition",
            "  alias <name> = \"body\"              Define a template alias (quoted body)",
            "  alias <name> = word                  Define a template alias (single-word body)",
            "  alias <name> = (<params>) => <expr>  Define a function alias (lambda, expression body)",
            "  alias <name> = (<params>) => { ... } Define a function alias (lambda, block body)",
            "  alias --save <name> = <body>         Define and persist to aliases.stash (Phase F)",
            "  alias --help                         Show this help",
            "",
            "  unalias <name>                       Remove alias from registry",
            "  unalias --all                        Remove all non-builtin aliases",
            "  unalias --save <name>                Remove and update aliases.stash (Phase F)",
            "  unalias --force <name>               Disable built-in alias for session (Phase D)",
            "",
            "Template placeholders:",
            "  ${args}                              All args (quoted, space-joined)",
            "  ${args[N]}                           Single argument N (0-indexed)",
            "  ${argv}                              Stash array of all arguments",
            "",
            "Examples:",
            "  alias gst = \"git status\"",
            "  alias g   = \"git ${args}\"",
            "  alias gco = (branch: string = \"main\") => $(git checkout ${branch})",
            "",
            "Use \\name to bypass aliases (force PATH lookup).",
            "Use !name for strict mode (bypass + fail on non-zero exit).");
    }
}
