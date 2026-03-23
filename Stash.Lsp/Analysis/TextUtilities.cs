namespace Stash.Lsp.Analysis;

/// <summary>
/// Stateless text-analysis helpers for extracting identifier tokens and dot-access prefixes
/// from raw source lines, used by LSP handlers to resolve cursor context.
/// </summary>
/// <remarks>
/// All methods operate on plain <see cref="string"/> input and are pure functions with no side effects.
/// Consumed primarily by <see cref="AnalysisEngine.GetContextAt"/> and <see cref="AnalysisResult.ResolveNamespaceMember"/>.
/// </remarks>
public static class TextUtilities
{
    /// <summary>
    /// Extracts the identifier word that spans the given 0-based cursor position.
    /// </summary>
    /// <remarks>
    /// A word character is any letter, digit, or underscore (<c>[A-Za-z0-9_]</c>).
    /// If the character at <paramref name="character"/> is not a word character, returns <c>null</c>.
    /// Scans left and right to find the full token boundary.
    /// </remarks>
    /// <param name="text">The full document text (lines joined by <c>'\n'</c>).</param>
    /// <param name="line">The 0-based line number.</param>
    /// <param name="character">The 0-based character offset within the line.</param>
    /// <returns>The word at the cursor, or <c>null</c> if the position is out of bounds or not on a word character.</returns>
    public static string? FindWordAtPosition(string text, int line, int character)
    {
        var lines = text.Split('\n');
        if (line < 0 || line >= lines.Length)
        {
            return null;
        }

        var lineText = lines[line];
        if (character < 0 || character >= lineText.Length)
        {
            return null;
        }

        var c = lineText[character];
        if (!char.IsLetterOrDigit(c) && c != '_')
        {
            return null;
        }

        int start = character;
        while (start > 0 && (char.IsLetterOrDigit(lineText[start - 1]) || lineText[start - 1] == '_'))
        {
            start--;
        }

        int end = character;
        while (end < lineText.Length - 1 && (char.IsLetterOrDigit(lineText[end + 1]) || lineText[end + 1] == '_'))
        {
            end++;
        }

        return lineText[start..(end + 1)];
    }

    /// <summary>
    /// Finds the identifier before a dot at the cursor position.
    /// Given "utils.log", with cursor on "log", returns "utils".
    /// </summary>
    /// <param name="line">The text line.</param>
    /// <param name="col">0-based cursor column (position of the first character of the word after the dot).</param>
    /// <returns>The prefix identifier, or null if there's no dot-access pattern.</returns>
    public static string? FindDotPrefix(string line, int col)
    {
        // Walk backwards from cursor to find the start of the current word
        int wordStart = col;
        while (wordStart > 0 && (char.IsLetterOrDigit(line[wordStart - 1]) || line[wordStart - 1] == '_'))
        {
            wordStart--;
        }

        // Check if there's a dot before the word
        if (wordStart <= 0 || line[wordStart - 1] != '.')
        {
            return null;
        }

        // Find the prefix before the dot
        int dotPos = wordStart - 1;
        int prefixStart = dotPos;
        while (prefixStart > 0 && (char.IsLetterOrDigit(line[prefixStart - 1]) || line[prefixStart - 1] == '_'))
        {
            prefixStart--;
        }

        if (prefixStart >= dotPos)
        {
            return null;
        }

        return line.Substring(prefixStart, dotPos - prefixStart);
    }
}
