namespace Stash.Check;

using System.Collections.Generic;
using Stash.Analysis;

internal sealed class CheckResult
{
    public IReadOnlyList<FileResult> Files { get; }
    public int TotalErrors { get; }
    public int TotalWarnings { get; }
    public int TotalInformation { get; }

    public CheckResult(IReadOnlyList<FileResult> files)
    {
        Files = files;
        int errors = 0, warnings = 0, information = 0;

        foreach (var file in files)
        {
            foreach (var diag in file.Analysis.SemanticDiagnostics)
            {
                switch (diag.Level)
                {
                    case DiagnosticLevel.Error: errors++; break;
                    case DiagnosticLevel.Warning: warnings++; break;
                    case DiagnosticLevel.Information: information++; break;
                }
            }
            errors += file.Analysis.StructuredLexErrors.Count;
            errors += file.Analysis.StructuredParseErrors.Count;
        }

        TotalErrors = errors;
        TotalWarnings = warnings;
        TotalInformation = information;
    }
}

internal sealed class FileResult
{
    public System.Uri Uri { get; }
    public AnalysisResult Analysis { get; }

    public FileResult(System.Uri uri, AnalysisResult analysis)
    {
        Uri = uri;
        Analysis = analysis;
    }
}
