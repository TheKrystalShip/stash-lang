namespace Stash.Lsp.Analysis;

using System.Collections.Generic;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Stash.Analysis;

/// <summary>
/// Converts an <see cref="AnalysisResult"/> into LSP <see cref="Diagnostic"/> objects
/// for publishing to the client.
/// </summary>
public static class DiagnosticBuilder
{
    /// <summary>
    /// Builds a list of LSP diagnostics from lex errors, parse errors, and semantic diagnostics
    /// in the given analysis result.
    /// </summary>
    public static List<Diagnostic> Build(AnalysisResult result)
    {
        var diagnostics = new List<Diagnostic>();

        foreach (var error in result.StructuredLexErrors)
        {
            diagnostics.Add(new Diagnostic
            {
                Range = error.Span.ToLspRange(),
                Severity = DiagnosticSeverity.Error,
                Source = "stash",
                Message = error.Message
            });
        }

        foreach (var error in result.StructuredParseErrors)
        {
            diagnostics.Add(new Diagnostic
            {
                Range = error.Span.ToLspRange(),
                Severity = DiagnosticSeverity.Error,
                Source = "stash",
                Message = error.Message
            });
        }

        foreach (var semantic in result.SemanticDiagnostics)
        {
            diagnostics.Add(new Diagnostic
            {
                Range = semantic.Span.ToLspRange(),
                Severity = semantic.Level switch
                {
                    DiagnosticLevel.Error => DiagnosticSeverity.Error,
                    DiagnosticLevel.Warning => DiagnosticSeverity.Warning,
                    DiagnosticLevel.Information => DiagnosticSeverity.Information,
                    _ => DiagnosticSeverity.Warning
                },
                Source = "stash",
                Message = semantic.Message,
                Tags = semantic.IsUnnecessary
                    ? new Container<DiagnosticTag>(DiagnosticTag.Unnecessary)
                    : null
            });
        }

        return diagnostics;
    }
}
