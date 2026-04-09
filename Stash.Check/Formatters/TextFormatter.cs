namespace Stash.Check;

using System.IO;
using System.Text;
using Stash.Analysis;

internal sealed class TextFormatter : IOutputFormatter
{
    public string Format => "text";

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
                writer.WriteLine($"{relativePath}:{err.Span.StartLine}:{err.Span.StartColumn}: STASH001 [error] {err.Message}");
            }

            foreach (var err in file.Analysis.StructuredParseErrors)
            {
                writer.WriteLine($"{relativePath}:{err.Span.StartLine}:{err.Span.StartColumn}: STASH002 [error] {err.Message}");
            }

            foreach (var diag in file.Analysis.SemanticDiagnostics)
            {
                string level = diag.Level switch
                {
                    DiagnosticLevel.Error => "error",
                    DiagnosticLevel.Warning => "warning",
                    _ => "info"
                };
                string code = diag.Code ?? "SA0000";
                writer.WriteLine($"{relativePath}:{diag.Span.StartLine}:{diag.Span.StartColumn}: {code} [{level}] {diag.Message}");
            }
        }

        writer.Flush();
    }
}
