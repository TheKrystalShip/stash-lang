namespace Stash.Lsp.Completion;

/// <summary>
/// Classifies the completion cursor context into a single <see cref="CompletionMode"/>.
/// </summary>
/// <remarks>
/// Precedence (first match wins):
/// <list type="number">
///   <item><see cref="CompletionMode.ImportString"/> — cursor is inside a string literal on an import/from line.</item>
///   <item><see cref="CompletionMode.Dot"/> — the character immediately left of the cursor is a <c>.</c> after an identifier.</item>
///   <item><see cref="CompletionMode.AfterExtend"/> — cursor is in the type-name position after the <c>extend</c> keyword.</item>
///   <item><see cref="CompletionMode.AfterIs"/> — cursor is in the type-name position after the <c>is</c> keyword.</item>
///   <item><see cref="CompletionMode.Default"/> — everything else.</item>
/// </list>
/// </remarks>
public static class CursorContextClassifier
{
    /// <summary>
    /// Classifies the cursor context and extracts the dot-access prefix when
    /// the result is <see cref="CompletionMode.Dot"/>.
    /// </summary>
    /// <param name="currentLine">
    /// The full text of the line at the cursor, or <see langword="null"/> when the
    /// document text is unavailable. When <see langword="null"/>, the result is always
    /// <see cref="CompletionMode.Default"/> and <paramref name="dotPrefix"/> is <see langword="null"/>.
    /// </param>
    /// <param name="col">The 0-based cursor column within <paramref name="currentLine"/>.</param>
    /// <param name="dotPrefix">
    /// When the returned mode is <see cref="CompletionMode.Dot"/>, the identifier that
    /// precedes the <c>.</c>; otherwise <see langword="null"/>.
    /// </param>
    /// <returns>The classified <see cref="CompletionMode"/>.</returns>
    public static CompletionMode Classify(string? currentLine, int col, out string? dotPrefix)
    {
        dotPrefix = null;

        if (currentLine == null)
        {
            return CompletionMode.Default;
        }

        // 1. ImportString: cursor inside a string literal (import context or plain string to suppress)
        if (IsInsideString(currentLine, col))
        {
            return CompletionMode.ImportString;
        }

        // 2. Dot: character immediately before cursor is '.' after an identifier
        if (col > 0 && col <= currentLine.Length)
        {
            var prefix = GetDotPrefix(currentLine, col);
            if (prefix != null)
            {
                dotPrefix = prefix;
                return CompletionMode.Dot;
            }
        }

        // 3. AfterExtend: cursor in type-name position after 'extend' keyword
        if (IsAfterExtendKeyword(currentLine, col))
        {
            return CompletionMode.AfterExtend;
        }

        // 4. AfterIs: cursor in type-name position after 'is' keyword
        if (IsAfterIsKeyword(currentLine, col))
        {
            return CompletionMode.AfterIs;
        }

        // 5. Default: fall-through
        return CompletionMode.Default;
    }

    /// <summary>
    /// Determines whether the cursor at <paramref name="col"/> on <paramref name="line"/>
    /// is inside an unescaped double-quoted string literal, accounting for interpolation
    /// expressions (<c>$"...{expr}..."</c>) where the cursor inside <c>{}</c> is treated
    /// as code, not string text.
    /// </summary>
    /// <param name="line">The source line text.</param>
    /// <param name="col">The 0-based cursor column.</param>
    /// <returns>
    /// <see langword="true"/> if the cursor is inside string text;
    /// <see langword="false"/> if outside any string or inside an interpolation expression.
    /// </returns>
    internal static bool IsInsideString(string line, int col)
    {
        bool inString = false;
        bool isInterpolated = false;
        int braceDepth = 0;

        for (int i = 0; i < col && i < line.Length; i++)
        {
            char c = line[i];

            if (!inString)
            {
                if (c == '"' && (i == 0 || line[i - 1] != '\\'))
                {
                    inString = true;
                    isInterpolated = i > 0 && line[i - 1] == '$';
                    braceDepth = 0;
                }
            }
            else
            {
                // Inside a string
                if (isInterpolated && braceDepth > 0)
                {
                    // Inside an interpolation expression — track nested braces
                    if (c == '{')
                    {
                        braceDepth++;
                    }
                    else if (c == '}')
                    {
                        braceDepth--;
                    }
                    // If braceDepth hits 0, we're back in string text
                }
                else if (isInterpolated && c == '{')
                {
                    braceDepth = 1;
                }
                else if (c == '"' && (i == 0 || line[i - 1] != '\\'))
                {
                    inString = false;
                    isInterpolated = false;
                }
            }
        }

        // If we're in a string but inside an interpolation expression, treat as code
        return inString && braceDepth == 0;
    }

    /// <summary>
    /// If the character immediately before the cursor is a <c>.</c>, walks backwards to
    /// extract the identifier that precedes it (the dot-access prefix).
    /// </summary>
    /// <param name="line">The source line text.</param>
    /// <param name="col">The 0-based cursor column.</param>
    /// <returns>The identifier before the dot, or <see langword="null"/> if no dot context is found.</returns>
    internal static string? GetDotPrefix(string line, int col)
    {
        // col is 0-based cursor position; the dot is at col-1
        if (col < 2 || col - 1 >= line.Length || line[col - 1] != '.')
        {
            return null;
        }

        // Walk backwards from col-2 to find identifier
        int end = col - 2;
        while (end >= 0 && (char.IsLetterOrDigit(line[end]) || line[end] == '_'))
        {
            end--;
        }

        end++;

        if (end >= col - 1)
        {
            return null; // empty prefix
        }

        return line.Substring(end, col - 1 - end);
    }

    /// <summary>
    /// Detects whether the cursor is immediately after the <c>is</c> keyword.
    /// </summary>
    /// <param name="line">The source line text.</param>
    /// <param name="col">The 0-based cursor column.</param>
    /// <returns>
    /// <see langword="true"/> if the text before the cursor ends with <c>is</c> as a whole word.
    /// </returns>
    internal static bool IsAfterIsKeyword(string line, int col)
    {
        // Walk backwards from cursor to find the start of the current partial word
        int pos = col;
        while (pos > 0 && (char.IsLetterOrDigit(line[pos - 1]) || line[pos - 1] == '_'))
        {
            pos--;
        }

        // Skip whitespace before the partial word
        while (pos > 0 && char.IsWhiteSpace(line[pos - 1]))
        {
            pos--;
        }

        // Check if the two characters before are "is" preceded by a non-identifier char (or start of line)
        if (pos >= 2 && line[pos - 1] == 's' && line[pos - 2] == 'i')
        {
            // Ensure "is" is a whole word (not part of "this", "exists", etc.)
            return pos - 2 == 0 || !char.IsLetterOrDigit(line[pos - 3]) && line[pos - 3] != '_';
        }

        return false;
    }

    /// <summary>
    /// Detects whether the cursor is immediately after the <c>extend</c> keyword.
    /// </summary>
    /// <param name="line">The source line text.</param>
    /// <param name="col">The 0-based cursor column.</param>
    /// <returns>
    /// <see langword="true"/> if the text before the cursor ends with <c>extend</c> as a whole word.
    /// </returns>
    internal static bool IsAfterExtendKeyword(string line, int col)
    {
        // Walk backwards from cursor to find the start of the current partial word
        int pos = col;
        while (pos > 0 && (char.IsLetterOrDigit(line[pos - 1]) || line[pos - 1] == '_'))
        {
            pos--;
        }

        // Skip whitespace before the partial word
        while (pos > 0 && char.IsWhiteSpace(line[pos - 1]))
        {
            pos--;
        }

        // Check if the six characters before are "extend" preceded by a non-identifier char (or start of line)
        if (pos >= 6 &&
            line[pos - 1] == 'd' &&
            line[pos - 2] == 'n' &&
            line[pos - 3] == 'e' &&
            line[pos - 4] == 't' &&
            line[pos - 5] == 'x' &&
            line[pos - 6] == 'e')
        {
            // Ensure "extend" is a whole word (not part of a longer identifier)
            return pos - 6 == 0 || !char.IsLetterOrDigit(line[pos - 7]) && line[pos - 7] != '_';
        }

        return false;
    }
}
