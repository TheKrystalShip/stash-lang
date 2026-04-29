using System;

namespace Stash.Cli.Shell;

/// <summary>Classification result for a REPL line when shell mode is enabled.</summary>
internal enum LineMode
{
    /// <summary>Run through the standard Stash lex/parse/execute pipeline.</summary>
    Stash,

    /// <summary>Run as a bare shell command (PATH-resolved, args expanded, streamed).</summary>
    Shell,

    /// <summary>Phase 5: backslash-forced shell (always PATH, bypasses declared symbols). Returns Stash in Phase 4.</summary>
    ShellForced,

    /// <summary>Phase 5: bang-strict shell (non-zero exit raises CommandError). Returns Stash in Phase 4.</summary>
    ShellStrict,
}

/// <summary>
/// Applies the §4 disambiguation rules to decide whether a REPL line should be
/// routed to the Stash lex/parse pipeline or the shell execution pipeline.
/// </summary>
internal sealed class ShellLineClassifier
{
    private readonly ShellContext _ctx;

    public ShellLineClassifier(ShellContext ctx)
    {
        _ctx = ctx;
    }

    /// <summary>
    /// Classify a single REPL input line.
    /// Always returns in O(1) / O(identifier-length) — no full lexing.
    /// </summary>
    public LineMode Classify(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return LineMode.Stash;

        // Handle multi-line shell input: lines after the first arrive joined by '\n'.
        // Use only the first physical line for classification.
        string firstLine = line;
        int nl = line.IndexOf('\n');
        if (nl >= 0)
            firstLine = line[..nl];

        var pr = PeekTokenizer.Peek(firstLine);

        return pr.Kind switch
        {
            // Empty / whitespace-only
            PeekKind.EndOfLine => LineMode.Stash,

            // $(  $>(  $!(  $!>( — existing Stash command literals
            PeekKind.DollarCommand => LineMode.Stash,

            // Literal starters
            PeekKind.Number => LineMode.Stash,
            PeekKind.StringStart => LineMode.Stash,

            // Opening delimiters
            PeekKind.OpenParen => LineMode.Stash,
            PeekKind.OpenBracket => LineMode.Stash,
            PeekKind.OpenBrace => LineMode.Stash,

            // Operator-leading (includes +, -, =, etc.)
            PeekKind.Operator => LineMode.Stash,

            // §4.2 backslash-force prefix
            PeekKind.Backslash => ClassifyBackslash(firstLine),

            // §4.3 bang-strict prefix
            PeekKind.Bang => ClassifyBang(firstLine),

            // Path-like first token → Shell (§4.1 table)
            PeekKind.PathLike => LineMode.Shell,

            // Bare identifier → apply §4.4 rules
            PeekKind.Identifier => ClassifyIdentifier(pr),

            _ => LineMode.Stash,
        };
    }

    /// <summary>
    /// Returns true when a shell-mode line is incomplete because it ends with a trailing
    /// pipe character (the pipeline continues on the next physical line — §9.1).
    /// </summary>
    public bool IsShellIncomplete(string line)
    {
        if (string.IsNullOrEmpty(line)) return false;

        // Classify: only shell lines can have trailing-pipe continuation.
        // We check based on the actual line content, not classification (avoids recursion).
        // Trim trailing whitespace and check for a bare '|'.
        string trimmed = line.TrimEnd();
        if (trimmed.Length == 0) return false;

        // Quick check: last char is '|'.
        if (trimmed[^1] != '|') return false;

        // Ensure it's not inside a string or ${ } — only top-level '|' counts.
        // For a simple conservative check: if the line ends with '|' after trimming,
        // and the classifier would route this as shell mode, treat it as incomplete.
        var mode = Classify(line);
        return mode is LineMode.Shell or LineMode.ShellForced or LineMode.ShellStrict;
    }

    // ── §4.2 Backslash-force prefix ─────────────────────────────────────────

    /// <summary>
    /// Implements §4.2: <c>\cmd</c> forces shell mode regardless of symbol declarations.
    /// Only valid when the character IMMEDIATELY after <c>\</c> is an identifier start,
    /// <c>/</c>, <c>.</c>, or <c>~</c>.  A backslash followed by whitespace or other
    /// chars falls through to Stash.
    /// </summary>
    private static LineMode ClassifyBackslash(string firstLine)
    {
        int i = 0;
        while (i < firstLine.Length && IsWs(firstLine[i])) i++;
        if (i >= firstLine.Length || firstLine[i] != '\\') return LineMode.Stash;
        i++; // skip '\'

        if (i >= firstLine.Length) return LineMode.Stash; // bare '\' at EOL

        char next = firstLine[i];
        return char.IsAsciiLetter(next) || next == '_' || next == '/' || next == '.' || next == '~'
            ? LineMode.ShellForced
            : LineMode.Stash;
    }

    /// <summary>
    /// Implements §4.3: <c>!cmd</c> runs the command in strict mode (non-zero exit → error).
    /// <c>!\cmd</c> combines strict + force.
    /// <c>!keyword</c> and <c>!declaredGlobal</c> remain Stash (logical-not).
    /// </summary>
    private LineMode ClassifyBang(string firstLine)
    {
        int i = 0;
        while (i < firstLine.Length && IsWs(firstLine[i])) i++;
        if (i >= firstLine.Length || firstLine[i] != '!') return LineMode.Stash;
        i++; // skip '!'

        string remainder = firstLine[i..];
        var inner = PeekTokenizer.Peek(remainder);

        switch (inner.Kind)
        {
            case PeekKind.Backslash:
                // !\<cmd> — strict + forced.  Verify that the char immediately after '\' is
                // an identifier start or path-leading char (mirrors ClassifyBackslash logic).
                {
                    int j = 0;
                    while (j < remainder.Length && IsWs(remainder[j])) j++;
                    if (j < remainder.Length && remainder[j] == '\\')
                    {
                        j++;
                        if (j < remainder.Length)
                        {
                            char next = remainder[j];
                            if (char.IsAsciiLetter(next) || next == '_' || next == '/' || next == '.' || next == '~')
                                return LineMode.ShellStrict; // lexer will also set IsForced
                        }
                    }
                    return LineMode.Stash;
                }

            case PeekKind.Identifier:
                // !<keyword>  → Stash (logical-not of boolean/null literal, etc.)
                if (_ctx.Keywords.Contains(inner.FirstToken)) return LineMode.Stash;
                // !<namespace>  → Stash
                if (_ctx.Namespaces.Contains(inner.FirstToken)) return LineMode.Stash;
                // !<declared global>  → Stash (logical-not of a variable)
                if (_ctx.Vm.HasReplGlobal(inner.FirstToken)) return LineMode.Stash;
                // !<shell builtin>  → ShellStrict
                if (_ctx.ShellBuiltinNames.Contains(inner.FirstToken)) return LineMode.ShellStrict;
                // !<PATH-resolvable>  → ShellStrict
                if (_ctx.PathCache.IsExecutable(inner.FirstToken)) return LineMode.ShellStrict;
                // Unknown identifier → Stash (undefined-identifier error from logical-not)
                return LineMode.Stash;

            default:
                // !=  !(  !42  etc. → Stash
                return LineMode.Stash;
        }
    }

    private static bool IsWs(char c) => c == ' ' || c == '\t' || c == '\r' || c == '\n';

    // ── §4.4 Bare identifier rules ──────────────────────────────────────────

    private LineMode ClassifyIdentifier(PeekResult pr)
    {
        string ident = pr.FirstToken;

        // Rule 1: Stash keyword → Stash mode.
        if (_ctx.Keywords.Contains(ident))
            return LineMode.Stash;

        // Rule 1b: Stdlib namespace → Stash mode.
        if (_ctx.Namespaces.Contains(ident))
            return LineMode.Stash;

        // Rule 2: Peek next token.
        switch (pr.NextKind)
        {
            // End-of-line: a bare identifier with nothing after it. Per spec §4.4 step 3,
            // we still consult the lookup chain — declared global → Stash, shell builtin
            // or PATH executable → Shell, otherwise Stash (undefined-identifier error).
            // The literal "EOL → Stash" reading would strand `ls`, `pwd`, etc. in Stash
            // mode and produce confusing "Undefined variable" errors at the REPL.
            case PeekKind.EndOfLine:
                goto default;

            // '(' → call expression → Stash.
            case PeekKind.OpenParen:
                return LineMode.Stash;

            // '[' → index expression → Stash.
            case PeekKind.OpenBracket:
                return LineMode.Stash;

            // '{' → block/struct → Stash (e.g. 'if x { y }' — 'x' would be Identifier then '{').
            case PeekKind.OpenBrace:
                return LineMode.Stash;

            case PeekKind.Operator:
                // Determine whether the operator signals Stash (assignment family, member, optional)
                // or that the line is a command invocation with arguments.
                if (pr.NextLeadChar.HasValue && IsStashOperatorChar(pr.NextLeadChar.Value))
                    return LineMode.Stash;
                // Otherwise fall through to the PATH check below.
                goto default;

            default:
                // Rule 3: something that looks like arguments follows.
                // Declared Stash symbol wins.
                if (_ctx.Vm.HasReplGlobal(ident))
                {
                    // SA0821: warn once per session when the same name is also on PATH.
                    if (_ctx.PathCache.IsExecutable(ident))
                        ShellDiagnostics.EmitShadowWarning(ident);
                    return LineMode.Stash;
                }

                // Shell built-in names (cd, pwd, exit, quit) → Shell for Phase 4.
                if (_ctx.ShellBuiltinNames.Contains(ident))
                    return LineMode.Shell;

                // PATH lookup.
                if (_ctx.PathCache.IsExecutable(ident))
                    return LineMode.Shell;

                // Not found anywhere → Stash (will produce undefined-identifier error).
                return LineMode.Stash;
        }
    }

    /// <summary>
    /// Returns true when the operator-leading character of the second token
    /// indicates a Stash construct (assignment, member access, optional chaining, etc.).
    /// </summary>
    private static bool IsStashOperatorChar(char c) =>
        // ';' → statement terminator; bare `ident;` is a Stash expression statement.
        // '=' → assignment or equality.
        // '.' → member access.
        // '?' → optional chain/null-coalescing.
        // '*'/'/''%' → arithmetic operators (compound or binary).
        // '&'/'|'/'^'/'<'/'>'/'!' → bitwise/logical/comparison/redirect.
        //
        // NOTE: '+' and '-' are intentionally OMITTED.
        //   '-flag' is a shell argument prefix; we let PATH lookup disambiguate.
        //   '+' is similarly ambiguous. The fallback is still Stash when not on PATH.
        c is ';' or '=' or '.' or '?' or '*' or '/' or '%' or '&' or '|' or '^' or '<' or '>' or '!';
}
