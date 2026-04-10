namespace Stash.Check;

using System.Collections.Generic;
using System.IO;
using System.Text;
using Stash.Analysis;

/// <summary>
/// Formats diagnostics grouped by file with a header for each file.
/// </summary>
/// <remarks>
/// Output format:
/// <code>
/// ── src/foo.stash ──
///   15:3  info   SA0201  Unused variable 'temp'
///   28:1  error  SA0103  'return' used outside of function
///
/// ── src/bar.stash ──
///   5:10  warn   SA0302  Constant type mismatch
/// </code>
/// </remarks>
internal sealed class GroupedFormatter : IOutputFormatter
{
    public string Format => "grouped";

    public void Write(CheckResult result, Stream output)
    {
        string cwd = Directory.GetCurrentDirectory();
        using var writer = new StreamWriter(output, Encoding.UTF8, leaveOpen: true);
        bool firstFile = true;

        foreach (var file in result.Files)
        {
            string filePath = file.Uri.IsFile ? file.Uri.LocalPath : file.Uri.ToString();
            string relativePath = Path.GetRelativePath(cwd, filePath).Replace('\\', '/');

            // Collect all entries for this file
            var entries = new List<(int line, int col, string level, string code, string message)>();

            foreach (var err in file.Analysis.StructuredLexErrors)
            {
                entries.Add((err.Span.StartLine, err.Span.StartColumn, "error", "STASH001", err.Message));
            }

            foreach (var err in file.Analysis.StructuredParseErrors)
            {
                entries.Add((err.Span.StartLine, err.Span.StartColumn, "error", "STASH002", err.Message));
            }

            foreach (var diag in file.Analysis.SemanticDiagnostics)
            {
                string level = diag.Level switch
                {
                    DiagnosticLevel.Error => "error",
                    DiagnosticLevel.Warning => "warn",
                    _ => "info"
                };
                entries.Add((diag.Span.StartLine, diag.Span.StartColumn, level, diag.Code ?? "SA0000", diag.Message));
            }

            if (entries.Count == 0)
            {
                continue;
            }

            if (!firstFile)
            {
                writer.WriteLine();
            }
            firstFile = false;

            writer.WriteLine($"── {relativePath} ──");
            foreach (var (line, col, level, code, message) in entries)
            {
                writer.WriteLine($"  {line,4}:{col,-4}  {level,-5}  {code,-8}  {message}");
            }
        }

        writer.Flush();
    }
}
