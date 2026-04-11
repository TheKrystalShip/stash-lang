using System;
using System.Collections.Generic;

namespace Stash.Analysis.Formatting;

/// <summary>
/// Post-processing utility that sorts import statements in formatted output.
/// </summary>
internal static class ImportSorter
{
    internal static string SortFormattedImports(string formatted)
    {
        string[] lines = formatted.Split('\n');
        var result = new List<string>(lines.Length);
        int i = 0;

        while (i < lines.Length)
        {
            string trimmed = lines[i].TrimStart();
            if (!trimmed.StartsWith("import ", StringComparison.Ordinal))
            {
                result.Add(lines[i]);
                i++;
                continue;
            }

            var importGroup = new List<string>();
            while (i < lines.Length && lines[i].TrimStart().StartsWith("import ", StringComparison.Ordinal))
            {
                importGroup.Add(lines[i]);
                i++;
            }

            if (importGroup.Count > 1)
            {
                importGroup.Sort((a, b) =>
                    string.Compare(ExtractImportPath(a), ExtractImportPath(b), StringComparison.OrdinalIgnoreCase));
            }

            for (int j = 0; j < importGroup.Count; j++)
                importGroup[j] = SortImportNames(importGroup[j]);

            result.AddRange(importGroup);
        }

        return string.Join("\n", result);
    }

    private static string ExtractImportPath(string importLine)
    {
        int fromIdx = importLine.IndexOf(" from ", StringComparison.Ordinal);
        if (fromIdx < 0) return "";
        return importLine[(fromIdx + 6)..].Trim().Trim('"', ';').Trim();
    }

    private static string SortImportNames(string importLine)
    {
        int braceOpen = importLine.IndexOf('{');
        int braceClose = importLine.IndexOf('}');
        if (braceOpen < 0 || braceClose < 0 || braceClose <= braceOpen + 1) return importLine;

        string inside = importLine[(braceOpen + 1)..braceClose].Trim();
        string[] names = inside.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (names.Length <= 1) return importLine;

        Array.Sort(names, StringComparer.OrdinalIgnoreCase);
        return importLine[..(braceOpen + 1)] + " " + string.Join(", ", names) + " " + importLine[braceClose..];
    }
}
