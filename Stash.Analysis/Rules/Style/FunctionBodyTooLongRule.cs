namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Parsing.AST;

/// <summary>
/// SA0902 — Reports an information diagnostic when a function body exceeds the line length threshold.
/// Configurable via <c>max_function_lines</c> in <c>.stashcheck</c> (default: 60).
/// </summary>
public sealed class FunctionBodyTooLongRule : IAnalysisRule, IConfigurableRule
{
    /// <summary>Default line count threshold.</summary>
    public const int DefaultThreshold = 60;

    /// <summary>Configurable threshold; defaults to <see cref="DefaultThreshold"/>.</summary>
    public int Threshold { get; private set; } = DefaultThreshold;

    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0902;

    public void Configure(IReadOnlyDictionary<string, string> options)
    {
        if (options.TryGetValue("max_function_lines", out string? val) && int.TryParse(val, out int v) && v > 0)
            Threshold = v;
    }

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type> { typeof(FnDeclStmt) };

    public void Analyze(RuleContext context)
    {
        if (context.Statement is not FnDeclStmt fn) return;

        int lineCount = fn.Body.Span.EndLine - fn.Body.Span.StartLine + 1;
        if (lineCount > Threshold)
        {
            context.ReportDiagnostic(
                DiagnosticDescriptors.SA0902.CreateDiagnostic(fn.Name.Span, fn.Name.Lexeme, lineCount, Threshold));
        }
    }
}
