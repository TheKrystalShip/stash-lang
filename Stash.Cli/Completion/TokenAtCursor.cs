using System;

namespace Stash.Cli.Completion;

/// <summary>
/// Probes a buffer up to a cursor position to determine the token at the cursor
/// and the surrounding lexical context (quote, substitution, comment).
/// </summary>
internal static class TokenAtCursor
{
    /// <summary>
    /// Walks <paramref name="buffer"/>[0..<paramref name="cursor"/>] tracking minimal lexer state
    /// to determine the token at the cursor and its context.
    /// </summary>
    /// <param name="buffer">The full line buffer.</param>
    /// <param name="cursor">The cursor position (exclusive upper bound of typed text).</param>
    /// <param name="classifiedMode">
    /// The line mode as classified by the shell-line classifier. Affects word-boundary rules.
    /// </param>
    public static CursorContext Probe(string buffer, int cursor, CompletionMode classifiedMode)
    {
        if (buffer.Length == 0)
            return new CursorContext(classifiedMode, 0, 0, string.Empty, false, '\0', false, Array.Empty<string>());

        cursor = Math.Clamp(cursor, 0, buffer.Length);

        if (cursor == 0)
            return new CursorContext(classifiedMode, 0, 0, string.Empty, false, '\0', false, Array.Empty<string>());

        // Single-pass forward scan tracking lexer state up to cursor.
        bool inSQ = false;          // inside '...'
        bool inDQ = false;          // inside "..."
        int dollarDepth = 0;        // nesting depth of ${ ... }
        int dollarInnerStart = -1;  // buffer index of first char inside the innermost ${
        int quoteStart = -1;        // buffer index of the opening quote character
        bool inLineComment = false;
        bool inBlockComment = false;
        bool inEscape = false;      // next char is escaped (shell-mode \ handling)

        // Shell-mode token tracking: updated at each unquoted boundary character.
        int shellTokenStart = 0;

        int i = 0;
        while (i < cursor)
        {
            char c = buffer[i];

            // ── Active line comment ──
            if (inLineComment)
            {
                i++;
                continue;
            }

            // ── Active block comment ──
            if (inBlockComment)
            {
                if (c == '*' && i + 1 < cursor && buffer[i + 1] == '/')
                {
                    inBlockComment = false;
                    i += 2;
                }
                else
                {
                    i++;
                }
                continue;
            }

            // ── Shell-mode escape: next character is literal ──
            if (inEscape)
            {
                inEscape = false;
                i++;
                continue;
            }

            // ── Outside any string ──
            if (!inSQ && !inDQ)
            {
                // Line comment //
                if (c == '/' && i + 1 < cursor && buffer[i + 1] == '/')
                {
                    inLineComment = true;
                    i += 2;
                    continue;
                }

                // Block comment /*
                if (c == '/' && i + 1 < cursor && buffer[i + 1] == '*')
                {
                    inBlockComment = true;
                    i += 2;
                    continue;
                }

                // Dollar-brace ${
                if (c == '$' && i + 1 < cursor && buffer[i + 1] == '{')
                {
                    dollarDepth++;
                    if (dollarDepth == 1)
                        dollarInnerStart = i + 2;
                    i += 2;
                    continue;
                }

                // Closing } of ${ ... }
                if (c == '}' && dollarDepth > 0)
                {
                    dollarDepth--;
                    if (dollarDepth == 0)
                        dollarInnerStart = -1;
                    i++;
                    continue;
                }

                // Shell-mode backslash escape (outside quotes)
                if (classifiedMode == CompletionMode.Shell && dollarDepth == 0 && c == '\\')
                {
                    inEscape = true;
                    i++;
                    continue;
                }

                // Single quote
                if (c == '\'')
                {
                    inSQ = true;
                    quoteStart = i;
                    i++;
                    continue;
                }

                // Double quote
                if (c == '"')
                {
                    inDQ = true;
                    quoteStart = i;
                    i++;
                    continue;
                }

                // Shell-mode word boundaries (outside ${...} and quotes)
                if (classifiedMode == CompletionMode.Shell && dollarDepth == 0)
                {
                    if (char.IsWhiteSpace(c) || c == '|' || c == ';' ||
                        c == '<' || c == '>' || c == '&' || c == '(' || c == ')')
                    {
                        shellTokenStart = i + 1;
                    }
                }
            }
            else if (inSQ)
            {
                // Single-quoted: only ' closes it
                if (c == '\'')
                {
                    inSQ = false;
                    quoteStart = -1;
                }
            }
            else // inDQ
            {
                // Shell-mode double-quote: \ escapes the next character
                if (classifiedMode == CompletionMode.Shell && c == '\\')
                {
                    inEscape = true;
                    i++;
                    continue;
                }
                // Closing double quote
                if (c == '"')
                {
                    inDQ = false;
                    quoteStart = -1;
                }
            }

            i++;
        }

        // ── Determine context from final scan state ──

        // Cursor is inside a line or block comment → no completion
        if (inLineComment || inBlockComment)
            return CursorContext.Empty;

        // Cursor is inside an unbalanced ${ ... } → Substitution mode
        if (dollarDepth > 0 && dollarInnerStart >= 0)
        {
            string subToken = buffer[dollarInnerStart..cursor];
            return new CursorContext(
                Mode: CompletionMode.Substitution,
                ReplaceStart: dollarInnerStart,
                ReplaceEnd: cursor,
                TokenText: subToken,
                InQuote: false,
                QuoteChar: '\0',
                InSubstitution: true,
                PriorArgs: Array.Empty<string>());
        }

        // Cursor is inside an unterminated quote
        if (inSQ || inDQ)
        {
            char qc = inSQ ? '\'' : '"';
            int ts = quoteStart + 1;
            string quotedToken = buffer[ts..cursor];
            return new CursorContext(
                Mode: classifiedMode,
                ReplaceStart: ts,
                ReplaceEnd: cursor,
                TokenText: quotedToken,
                InQuote: true,
                QuoteChar: qc,
                InSubstitution: false,
                PriorArgs: Array.Empty<string>());
        }

        // ── Normal token detection ──

        if (classifiedMode == CompletionMode.Shell)
        {
            // Token start was tracked during the forward scan.
            string shellToken = buffer[shellTokenStart..cursor];
            return new CursorContext(
                Mode: classifiedMode,
                ReplaceStart: shellTokenStart,
                ReplaceEnd: cursor,
                TokenText: shellToken,
                InQuote: false,
                QuoteChar: '\0',
                InSubstitution: false,
                // TODO Phase 2: populate via ArgExpander
                PriorArgs: Array.Empty<string>());
        }
        else
        {
            // Stash mode: walk backward over [A-Za-z_0-9.] characters.
            int ts = cursor;
            while (ts > 0 && IsStashWordChar(buffer[ts - 1]))
                ts--;

            string stashToken = buffer[ts..cursor];
            return new CursorContext(
                Mode: classifiedMode,
                ReplaceStart: ts,
                ReplaceEnd: cursor,
                TokenText: stashToken,
                InQuote: false,
                QuoteChar: '\0',
                InSubstitution: false,
                PriorArgs: Array.Empty<string>());
        }
    }

    private static bool IsStashWordChar(char c)
        => char.IsLetterOrDigit(c) || c == '_' || c == '.';
}
