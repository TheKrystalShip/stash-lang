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
}
