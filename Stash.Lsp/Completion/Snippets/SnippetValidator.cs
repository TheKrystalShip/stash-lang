namespace Stash.Lsp.Completion.Snippets;

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Stash.Lexing;
using Stash.Parsing;

/// <summary>
/// Validates raw snippet definitions against the seven rules defined in the brief Decision Log Q1
/// and returns valid <see cref="Snippet"/> instances along with any <see cref="SnippetLoadError"/>s.
/// </summary>
/// <remarks>
/// <para>The seven rules (each failure rejects the snippet and records a <see cref="SnippetLoadError"/>):</para>
/// <list type="number">
///   <item><description><b>Prefix shape.</b> Non-empty; matches <c>[A-Za-z_][A-Za-z0-9_.]*</c>.</description></item>
///   <item><description><b>Body presence.</b> Non-empty after array-join.</description></item>
///   <item><description><b>Lexes.</b> The tabstop-stripped body tokenises without errors.</description></item>
///   <item><description><b>Parses.</b> The stripped body produces a non-empty statement list with no parse errors.</description></item>
///   <item><description><b>Tabstop syntax well-formed.</b> All <c>$…</c> sequences are valid tabstop forms.</description></item>
///   <item><description><b>Scope value.</b> Resolves to a known <see cref="SnippetScope"/> (case-insensitive); absent defaults to <see cref="SnippetScope.Any"/>.</description></item>
///   <item><description><b>Per-source uniqueness.</b> <c>(prefix, scope)</c> is unique within the same source.</description></item>
/// </list>
/// </remarks>
public static class SnippetValidator
{
    // Rule 1: prefix shape — non-empty, identifier-like, allows dot separators (e.g. test.it)
    private static readonly Regex PrefixPattern = new(
        @"^[A-Za-z_][A-Za-z0-9_.]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Rule 5: tabstop syntax — accept $0, $N, ${N}, ${N:default}, ${N|opt1,opt2|}
    // Lone $ followed by " or ( is allowed (Stash interpolation prefix $"…" / $(…))
    private static readonly Regex TabstopPattern = new(
        @"\$(\d+|\{(\d+)(:[^}]*)?\}|\{\d+\|[^|]*\|})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Detects any $ that is NOT a valid tabstop and NOT followed by " or (
    private static readonly Regex InvalidDollarPattern = new(
        @"\$(?![\d{""(])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The result of a validation pass over a batch of raw snippets from a single source.
    /// </summary>
    public sealed record Result(
        IReadOnlyList<Snippet> Valid,
        IReadOnlyList<SnippetLoadError> Errors);

    /// <summary>
    /// Validates <paramref name="rawEntries"/> (display-name → raw snippet pairs)
    /// from <paramref name="source"/> and returns the valid subset and all errors.
    /// </summary>
    public static Result Validate(
        IEnumerable<(string DisplayName, RawSnippet Raw)> rawEntries,
        SnippetSourceKind source)
    {
        var valid = new List<Snippet>();
        var errors = new List<SnippetLoadError>();
        var sourceLocation = source.ToString().ToLowerInvariant();

        // Per-source uniqueness tracking: (prefix, scope) → display-name that claimed it first.
        var seen = new Dictionary<(string prefix, SnippetScope scope), string>();
        // Track which keys have already been flagged as duplicates.
        var flaggedDuplicates = new HashSet<(string prefix, SnippetScope scope)>();

        // Collect all entries first to detect all duplicates (not just first-offenders).
        var allEntries = new List<(string displayName, RawSnippet raw)>();
        foreach (var (displayName, raw) in rawEntries)
            allEntries.Add((displayName, raw));

        // Pre-scan for duplicate (prefix, scope) pairs within this source (Rule 7).
        var prefixScopeCount = new Dictionary<(string prefix, string scopeStr), List<string>>();
        foreach (var (displayName, raw) in allEntries)
        {
            if (string.IsNullOrWhiteSpace(raw.Prefix)) continue; // will fail Rule 1
            var key = (raw.Prefix, raw.Scope ?? "");
            if (!prefixScopeCount.TryGetValue(key, out var names))
            {
                names = new List<string>();
                prefixScopeCount[key] = names;
            }
            names.Add(displayName);
        }

        foreach (var (displayName, raw) in allEntries)
        {
            var idOrName = displayName;

            // ── Rule 1: Prefix shape ─────────────────────────────────────────────────
            if (string.IsNullOrEmpty(raw.Prefix) || !PrefixPattern.IsMatch(raw.Prefix))
            {
                errors.Add(new SnippetLoadError(
                    idOrName,
                    sourceLocation,
                    $"prefix '{raw.Prefix}' is empty or does not match [A-Za-z_][A-Za-z0-9_.]*"));
                continue;
            }

            // ── Rule 6: Scope value ─────────────────────────────────────────────────
            SnippetScope scope;
            if (!TryParseScope(raw.Scope, out scope))
            {
                errors.Add(new SnippetLoadError(
                    $"{source}:{raw.Prefix}:{raw.Scope}",
                    sourceLocation,
                    $"scope '{raw.Scope}' is not a known value (expected: any, top-level, fn-body, loop-body)"));
                continue;
            }

            var id = $"{source}:{raw.Prefix}:{scope}";
            idOrName = id;

            // ── Rule 2: Body presence ───────────────────────────────────────────────
            var body = ResolveBody(raw);
            if (string.IsNullOrEmpty(body))
            {
                errors.Add(new SnippetLoadError(id, sourceLocation, "body is empty or missing"));
                continue;
            }

            // ── Rule 5: Tabstop syntax well-formed ─────────────────────────────────
            // Run on original body before stripping.
            if (!ValidateTabstopSyntax(body, out var tabstopError))
            {
                errors.Add(new SnippetLoadError(id, sourceLocation, tabstopError!));
                continue;
            }

            // ── Rules 3 & 4: Lex and parse (on stripped body) ─────────────────────
            var strippedBody = StripTabstops(body);
            if (!TryLexAndParse(strippedBody, out var lexParseError))
            {
                errors.Add(new SnippetLoadError(id, sourceLocation, lexParseError!));
                continue;
            }

            // ── Rule 7: Per-source uniqueness ───────────────────────────────────────
            var scopeKey = (raw.Prefix, scope);
            var dupeKey = (raw.Prefix, raw.Scope ?? "");
            if (prefixScopeCount.TryGetValue(dupeKey, out var dupeNames) && dupeNames.Count > 1)
            {
                // All duplicates of this key are errors — even if this particular entry would
                // otherwise be valid. Flag the key so we record an error for every occurrence.
                if (!flaggedDuplicates.Contains(scopeKey))
                {
                    flaggedDuplicates.Add(scopeKey);
                    // Emit an error for every duplicate entry in this group.
                    foreach (var dupeName in dupeNames)
                    {
                        errors.Add(new SnippetLoadError(
                            $"{source}:{raw.Prefix}:{scope}",
                            sourceLocation,
                            $"duplicate (prefix='{raw.Prefix}', scope='{scope}') within source '{sourceLocation}'; " +
                            $"all {dupeNames.Count} entries rejected: {string.Join(", ", dupeNames)}"));
                    }
                }
                continue; // Skip adding to valid — already handled above
            }

            valid.Add(new Snippet(
                Id: id,
                Prefix: raw.Prefix,
                DisplayName: displayName,
                Body: body,
                Description: raw.Description,
                Scope: scope,
                Source: source));
        }

        return new Result(valid, errors);
    }

    /// <summary>
    /// Strips LSP tabstop / placeholder tokens from <paramref name="body"/> to produce
    /// a body that can be lexed and parsed as plain Stash code.
    /// </summary>
    /// <remarks>
    /// Substitution rules:
    /// <list type="bullet">
    ///   <item><description><c>${N:default}</c> → <c>default</c> (lifts the default text into the body).</description></item>
    ///   <item><description><c>${N}</c> → <c>__snip_N</c>.</description></item>
    ///   <item><description><c>$N</c> → <c>__snip_N</c>.</description></item>
    ///   <item><description><c>$0</c> → <c>__snip_0</c>.</description></item>
    ///   <item><description><c>${N|opt1,opt2|}</c> → first option, or <c>__snip_N</c> if no options.</description></item>
    /// </list>
    /// Stash interpolation prefixes (<c>$"…"</c>, <c>$(…)</c>) are left untouched.
    /// </remarks>
    internal static string StripTabstops(string body)
    {
        if (!body.Contains('$'))
            return body;

        var sb = new StringBuilder(body.Length);
        int i = 0;
        while (i < body.Length)
        {
            if (body[i] != '$')
            {
                sb.Append(body[i++]);
                continue;
            }

            // Peek at the character after '$'
            if (i + 1 >= body.Length)
            {
                // Trailing '$' — leave as-is (won't affect parse since it's end of string)
                sb.Append(body[i++]);
                continue;
            }

            char next = body[i + 1];

            // Stash interpolation: $" or $( — pass through untouched
            if (next == '"' || next == '(')
            {
                sb.Append(body[i++]);
                continue;
            }

            // $N — bare tabstop (digits only)
            // $N for N > 0 becomes __snip_N so the parser sees a valid identifier.
            // $0 is the final cursor position (no semantic content) and substitutes to
            // `null;` — a valid Stash expression statement that parses cleanly in any
            // block-body position (the empirically dominant location for $0). Empty-
            // string substitution would silently rely on empty blocks being legal;
            // `null;` is a real statement so snippets that put $0 in non-statement
            // positions fail validation loudly instead of producing invalid Stash on
            // expansion.
            if (char.IsDigit(next))
            {
                int start = i + 1;
                int j = start;
                while (j < body.Length && char.IsDigit(body[j])) j++;
                var n = body.Substring(start, j - start);
                sb.Append(n == "0" ? "null;" : $"__snip_{n}");
                i = j;
                continue;
            }

            // ${…} forms
            if (next == '{')
            {
                int braceStart = i + 2;
                // Collect the N
                int j = braceStart;
                while (j < body.Length && char.IsDigit(body[j])) j++;
                var n = body.Substring(braceStart, j - braceStart);

                if (j >= body.Length)
                {
                    // Malformed — leave as-is
                    sb.Append(body[i++]);
                    continue;
                }

                if (body[j] == '}')
                {
                    // ${N} → __snip_N. ${0} → null; (see bare-$N substitution above).
                    sb.Append(n == "0" ? "null;" : $"__snip_{n}");
                    i = j + 1;
                    continue;
                }

                if (body[j] == ':')
                {
                    // ${N:default} → default text
                    int defaultStart = j + 1;
                    // Find the closing '}'
                    int depth = 1;
                    int k = defaultStart;
                    while (k < body.Length && depth > 0)
                    {
                        if (body[k] == '{') depth++;
                        else if (body[k] == '}') depth--;
                        if (depth > 0) k++;
                        else break;
                    }
                    var defaultText = body.Substring(defaultStart, k - defaultStart);
                    sb.Append(defaultText);
                    i = k + 1; // skip closing '}'
                    continue;
                }

                if (body[j] == '|')
                {
                    // ${N|opt1,opt2|} → first option or __snip_N
                    int optStart = j + 1;
                    int pipeClose = body.IndexOf('|', optStart);
                    if (pipeClose >= 0 && pipeClose + 1 < body.Length && body[pipeClose + 1] == '}')
                    {
                        var opts = body.Substring(optStart, pipeClose - optStart).Split(',');
                        sb.Append(opts.Length > 0 && opts[0].Length > 0 ? opts[0] : $"__snip_{n}");
                        i = pipeClose + 2; // skip |}
                    }
                    else
                    {
                        sb.Append($"__snip_{n}");
                        i = j + 1;
                    }
                    continue;
                }

                // Unrecognised ${…} form — leave as-is
                sb.Append(body[i++]);
                continue;
            }

            // Lone $ not followed by digit, {, ", ( — leave as-is
            sb.Append(body[i++]);
        }

        return sb.ToString();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static bool TryParseScope(string? raw, out SnippetScope scope)
    {
        if (raw == null || raw.Equals("any", StringComparison.OrdinalIgnoreCase))
        {
            scope = SnippetScope.Any;
            return true;
        }
        if (raw.Equals("top-level", StringComparison.OrdinalIgnoreCase))
        {
            scope = SnippetScope.TopLevel;
            return true;
        }
        if (raw.Equals("fn-body", StringComparison.OrdinalIgnoreCase))
        {
            scope = SnippetScope.FnBody;
            return true;
        }
        if (raw.Equals("loop-body", StringComparison.OrdinalIgnoreCase))
        {
            scope = SnippetScope.LoopBody;
            return true;
        }
        scope = default;
        return false;
    }

    private static string ResolveBody(RawSnippet raw)
    {
        var elem = raw.Body;
        if (elem.ValueKind == System.Text.Json.JsonValueKind.String)
            return elem.GetString() ?? "";

        if (elem.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            var lines = new List<string>();
            foreach (var item in elem.EnumerateArray())
                lines.Add(item.GetString() ?? "");
            return string.Join("\n", lines);
        }

        return "";
    }

    private static bool ValidateTabstopSyntax(string body, out string? error)
    {
        // Walk the body character-by-character and validate every $ sequence.
        for (int i = 0; i < body.Length; i++)
        {
            if (body[i] != '$') continue;

            if (i + 1 >= body.Length)
            {
                // Trailing $ — not a valid tabstop but only reject if it's truly lone
                // (at end of string it's ambiguous; allow for now)
                continue;
            }

            char next = body[i + 1];

            // Allowed: Stash interpolation $" or $(
            if (next == '"' || next == '(')
            {
                i++; // skip past the $ so the outer loop moves past the next char
                continue;
            }

            // Allowed: $N (bare digit tabstop)
            if (char.IsDigit(next))
            {
                int j = i + 1;
                while (j < body.Length && char.IsDigit(body[j])) j++;
                i = j - 1; // outer loop will i++
                continue;
            }

            // Allowed: ${N}, ${N:default}, ${N|opt1,opt2|}
            if (next == '{')
            {
                int j = i + 2;
                // Skip N
                if (j >= body.Length || !char.IsDigit(body[j]))
                {
                    error = $"malformed tabstop at position {i}: '${{{(j < body.Length ? body[j].ToString() : "EOF")}}}'";
                    return false;
                }
                while (j < body.Length && char.IsDigit(body[j])) j++;

                if (j >= body.Length)
                {
                    error = $"unclosed tabstop brace at position {i}";
                    return false;
                }

                char sep = body[j];
                if (sep == '}')
                {
                    i = j; // skip to closing brace; outer loop will i++
                    continue;
                }
                if (sep == ':')
                {
                    // Find matching closing brace (depth-aware for nested ${})
                    int depth = 1;
                    int k = j + 1;
                    while (k < body.Length && depth > 0)
                    {
                        if (body[k] == '{') depth++;
                        else if (body[k] == '}') depth--;
                        k++;
                    }
                    if (depth != 0)
                    {
                        error = $"unclosed tabstop brace at position {i}";
                        return false;
                    }
                    i = k - 1;
                    continue;
                }
                if (sep == '|')
                {
                    // Find closing |}
                    int pipeClose = body.IndexOf('|', j + 1);
                    if (pipeClose < 0 || pipeClose + 1 >= body.Length || body[pipeClose + 1] != '}')
                    {
                        error = $"malformed choice tabstop at position {i}";
                        return false;
                    }
                    i = pipeClose + 1;
                    continue;
                }

                error = $"malformed tabstop at position {i}: unexpected '{sep}' after tabstop number";
                return false;
            }

            // Lone $ not followed by digit, {, ", ( — reject
            error = $"lone '$' at position {i} is not a valid tabstop (use $N, ${{N}}, ${{N:default}}, or $\"…\" / $(…) for Stash interpolation)";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryLexAndParse(string strippedBody, out string? error)
    {
        // Rule 3: lex
        var lexer = new Lexer(strippedBody, "<snippet>");
        List<Token> tokens;
        try
        {
            tokens = lexer.ScanTokens();
        }
        catch (Exception ex)
        {
            error = $"body failed to lex: {ex.Message}";
            return false;
        }

        if (lexer.Errors.Count > 0)
        {
            error = $"body failed to lex: {lexer.Errors[0]}";
            return false;
        }

        // Filter trivia tokens for the parser (mirrors AnalysisEngine pattern)
        var parserTokens = new List<Token>(tokens.Count);
        foreach (var t in tokens)
        {
            if (t.Type != TokenType.DocComment &&
                t.Type != TokenType.SingleLineComment &&
                t.Type != TokenType.BlockComment &&
                t.Type != TokenType.Shebang)
            {
                parserTokens.Add(t);
            }
        }

        // Rule 4: parse
        var parser = new Parser(parserTokens);
        List<Stash.Parsing.AST.Stmt> stmts;
        try
        {
            stmts = parser.ParseProgram();
        }
        catch (Exception ex)
        {
            error = $"body failed to parse: {ex.Message}";
            return false;
        }

        if (parser.Errors.Count > 0)
        {
            error = $"body failed to parse: {parser.Errors[0]}";
            return false;
        }

        if (stmts.Count == 0)
        {
            error = "body produced no statements after parsing";
            return false;
        }

        error = null;
        return true;
    }
}
