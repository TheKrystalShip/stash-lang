namespace Stash.Lsp.Analysis;

public static class TextUtilities
{
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
