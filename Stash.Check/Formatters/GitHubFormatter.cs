namespace Stash.Check;

using System.IO;
using System.Text;
using Stash.Analysis;

/// <summary>
/// Formats diagnostics as GitHub Actions workflow commands so they appear as
/// annotations in pull request diffs and action summaries.
/// </summary>
/// <remarks>
/// Format: <c>::{level} file={file},line={line},col={col}::{code}: {message}</c>
/// </remarks>
internal sealed class GitHubFormatter : IOutputFormatter
{
    public string Format => "github";

    public void Write(CheckResult result, Stream output)
    {
        string cwd = Directory.GetCurrentDirectory();
        using var writer = new StreamWriter(output, Encoding.UTF8, leaveOpen: true);

        foreach (var file in result.Files)
        {
            string filePath = file.Uri.IsFile ? file.Uri.LocalPath : file.Uri.ToString();
            string relativePath = Path.GetRelativePath(cwd, filePath).Replace('\\', '/');

            foreach (var err in file.Analysis.StructuredLexErrors)
            {
                writer.WriteLine($"::error file={relativePath},line={err.Span.StartLine},col={err.Span.StartColumn}::STASH001: {err.Message}");
            }

            foreach (var err in file.Analysis.StructuredParseErrors)
            {
                writer.WriteLine($"::error file={relativePath},line={err.Span.StartLine},col={err.Span.StartColumn}::STASH002: {err.Message}");
            }

            foreach (var diag in file.Analysis.SemanticDiagnostics)
            {
                string level = diag.Level switch
                {
                    DiagnosticLevel.Error => "error",
                    DiagnosticLevel.Warning => "warning",
                    _ => "notice"
                };
                string code = diag.Code ?? "SA0000";
                writer.WriteLine($"::{level} file={relativePath},line={diag.Span.StartLine},col={diag.Span.StartColumn}::{code}: {diag.Message}");
            }
        }

        writer.Flush();
    }
}
