namespace Stash.Check;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Stash.Analysis;

/// <summary>
/// Applies <see cref="CodeFix"/> edits to source files and can produce unified diff output.
/// </summary>
internal static class FixApplier
{
    /// <summary>
    /// Collects all applicable fixes from a <see cref="CheckResult"/>, filtered by applicability.
    /// </summary>
    /// <param name="result">The check result containing diagnostics with fixes.</param>
    /// <param name="allowUnsafe">When <see langword="true"/>, includes <see cref="FixApplicability.Unsafe"/> fixes.</param>
    /// <returns>A map from file URI to the list of fixes applicable to that file.</returns>
    public static Dictionary<Uri, List<(CodeFix Fix, SemanticDiagnostic Diagnostic)>> CollectFixes(
        CheckResult result, bool allowUnsafe)
    {
        var byFile = new Dictionary<Uri, List<(CodeFix, SemanticDiagnostic)>>();

        foreach (var file in result.Files)
        {
            var list = new List<(CodeFix, SemanticDiagnostic)>();

            foreach (var diag in file.Analysis.SemanticDiagnostics)
            {
                foreach (var fix in diag.Fixes)
                {
                    if (fix.Applicability == FixApplicability.Safe ||
                        (allowUnsafe && fix.Applicability == FixApplicability.Unsafe))
                    {
                        list.Add((fix, diag));
                    }
                }
            }

            if (list.Count > 0)
            {
                byFile[file.Uri] = list;
            }
        }

        return byFile;
    }

    /// <summary>
    /// Applies fixes in-place to the files in <paramref name="fixesByFile"/>.
    /// </summary>
    /// <param name="fixesByFile">Map from URI to fixes to apply.</param>
    /// <returns>The number of files modified.</returns>
    public static int ApplyFixes(Dictionary<Uri, List<(CodeFix Fix, SemanticDiagnostic Diagnostic)>> fixesByFile)
    {
        int fileCount = 0;

        foreach (var (uri, fixes) in fixesByFile)
        {
            string path = uri.LocalPath;
            if (!File.Exists(path))
            {
                continue;
            }

            string original = File.ReadAllText(path);
            string modified = ApplyFixesToSource(original, fixes.Select(f => f.Fix));
            if (modified != original)
            {
                File.WriteAllText(path, modified, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                fileCount++;
            }
        }

        return fileCount;
    }

    /// <summary>
    /// Writes a unified diff of all fixes to <paramref name="writer"/> without modifying files.
    /// </summary>
    public static void WriteDiff(
        CheckResult result,
        Dictionary<Uri, List<(CodeFix Fix, SemanticDiagnostic Diagnostic)>> fixesByFile,
        TextWriter writer)
    {
        foreach (var (uri, fixes) in fixesByFile)
        {
            string path = uri.LocalPath;
            if (!File.Exists(path))
            {
                continue;
            }

            string original = File.ReadAllText(path);
            string modified = ApplyFixesToSource(original, fixes.Select(f => f.Fix));

            if (modified == original)
            {
                continue;
            }

            string relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), path)
                .Replace('\\', '/');

            WriteUnifiedDiff(writer, original, modified, $"a/{relativePath}", $"b/{relativePath}");
        }
    }

    /// <summary>
    /// Applies a collection of <see cref="CodeFix"/> edits to a source string.
    /// Edits are sorted bottom-to-top and deduplicated when they overlap.
    /// </summary>
    internal static string ApplyFixesToSource(string source, IEnumerable<CodeFix> fixes)
    {
        // Collect all TextEdit objects from all fixes and sort bottom-to-top so that
        // earlier offsets remain valid as we apply edits from the end.
        var allEdits = fixes
            .SelectMany(f => f.Edits)
            .OrderByDescending(e => e.Span.StartLine)
            .ThenByDescending(e => e.Span.StartColumn)
            .ToList();

        var lines = SplitLines(source);
        var applied = new List<(int StartLine, int StartCol, int EndLine, int EndCol)>();

        foreach (var edit in allEdits)
        {
            int sl = edit.Span.StartLine;
            int sc = edit.Span.StartColumn;
            int el = edit.Span.EndLine;
            int ec = edit.Span.EndColumn;

            // Conflict check: skip if this edit overlaps an already-applied edit.
            bool conflicts = false;
            foreach (var (asl, asc, ael, aec) in applied)
            {
                if (!(el < asl || sl > ael || (el == asl && ec < asc) || (sl == ael && sc > aec)))
                {
                    conflicts = true;
                    break;
                }
            }

            if (conflicts)
            {
                continue;
            }

            lines = ApplySingleEdit(lines, sl, sc, el, ec, edit.NewText);
            applied.Add((sl, sc, el, ec));
        }

        return JoinLines(lines);
    }

    private static List<string> ApplySingleEdit(List<string> lines, int startLine, int startCol, int endLine, int endCol, string newText)
    {
        // Convert 1-based SourceSpan to 0-based list indices and character offsets.
        int startIdx = startLine - 1;
        int endIdx = endLine - 1;

        if (startIdx < 0 || startIdx >= lines.Count)
        {
            return lines;
        }

        endIdx = Math.Min(endIdx, lines.Count - 1);

        string startLineText = lines[startIdx];
        string endLineText = lines[endIdx];

        // SourceSpan columns are 1-based.
        int startCharIdx = Math.Min(startCol - 1, startLineText.Length);
        int endCharIdx = Math.Min(endCol, endLineText.Length);  // endCol is 1-based inclusive, so endCol chars

        string before = startLineText[..startCharIdx];
        string after = endLineText[endCharIdx..];
        string combined = before + newText + after;

        // Replace the range of lines with the combined result.
        var result = new List<string>(lines);
        result.RemoveRange(startIdx, endIdx - startIdx + 1);

        // Split the combined result on newlines to support multi-line replacements.
        var newLines = SplitLines(combined);
        result.InsertRange(startIdx, newLines);

        // Remove lines that became empty due to the edit (e.g. when an import is blanked out)
        // when the original edit was a full-line removal (NewText == "").
        if (newText == "" && startCharIdx == 0)
        {
            // Remove empty lines inserted at startIdx.
            while (startIdx < result.Count && result[startIdx].Trim().Length == 0)
            {
                result.RemoveAt(startIdx);
            }
        }

        return result;
    }

    /// <summary>
    /// Splits source text into logical lines, preserving platform-native newlines as empty
    /// string entries (each element is a line WITHOUT its trailing newline).
    /// </summary>
    private static List<string> SplitLines(string source)
    {
        var lines = new List<string>();
        int i = 0;
        int start = 0;
        while (i < source.Length)
        {
            if (source[i] == '\n')
            {
                lines.Add(source[start..i]);
                start = i + 1;
            }
            else if (source[i] == '\r' && i + 1 < source.Length && source[i + 1] == '\n')
            {
                lines.Add(source[start..i]);
                start = i + 2;
                i++;
            }
            i++;
        }
        // Remaining text after last newline (may be empty for files ending with \n)
        lines.Add(source[start..]);
        return lines;
    }

    private static string JoinLines(List<string> lines)
    {
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Writes a minimal unified diff between <paramref name="before"/> and <paramref name="after"/>
    /// to <paramref name="writer"/>.
    /// </summary>
    private static void WriteUnifiedDiff(TextWriter writer, string before, string after, string fromLabel, string toLabel)
    {
        var beforeLines = SplitLines(before);
        var afterLines = SplitLines(after);

        writer.WriteLine($"--- {fromLabel}");
        writer.WriteLine($"+++ {toLabel}");

        // Simple hunk-based diff: find changed regions.
        int i = 0, j = 0;
        while (i < beforeLines.Count || j < afterLines.Count)
        {
            if (i < beforeLines.Count && j < afterLines.Count && beforeLines[i] == afterLines[j])
            {
                i++;
                j++;
                continue;
            }

            // Start of a changed hunk.
            int hunkStartBefore = i;
            int hunkStartAfter = j;

            // Advance until either we run out of content or find 3 matching lines.
            int matchCount = 0;
            int bi = i, ai = j;
            while ((bi < beforeLines.Count || ai < afterLines.Count) && matchCount < 3)
            {
                if (bi < beforeLines.Count && ai < afterLines.Count && beforeLines[bi] == afterLines[ai])
                {
                    matchCount++;
                    bi++;
                    ai++;
                }
                else
                {
                    matchCount = 0;
                    if (bi < beforeLines.Count) bi++;
                    if (ai < afterLines.Count) ai++;
                }
            }

            int hunkEndBefore = bi;
            int hunkEndAfter = ai;

            // Write hunk header: @@ -start,count +start,count @@
            int beforeCount = hunkEndBefore - hunkStartBefore;
            int afterCount = hunkEndAfter - hunkStartAfter;
            writer.WriteLine($"@@ -{hunkStartBefore + 1},{beforeCount} +{hunkStartAfter + 1},{afterCount} @@");

            // Write hunk lines.
            int ci = hunkStartBefore, di = hunkStartAfter;
            while (ci < hunkEndBefore || di < hunkEndAfter)
            {
                if (ci < hunkEndBefore && di < hunkEndAfter && beforeLines[ci] == afterLines[di])
                {
                    writer.WriteLine($" {beforeLines[ci]}");
                    ci++;
                    di++;
                }
                else if (ci < hunkEndBefore)
                {
                    writer.WriteLine($"-{beforeLines[ci]}");
                    ci++;
                }
                else
                {
                    writer.WriteLine($"+{afterLines[di]}");
                    di++;
                }
            }

            i = hunkEndBefore;
            j = hunkEndAfter;
        }
    }
}
