namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Parsing.AST;

/// <summary>
/// SA1201 — Warns when a spread operator is used to accumulate values into the same
/// variable inside a loop, creating O(n²) copies.
/// </summary>
public sealed class NoAccumulatingSpreadRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA1201;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type> { typeof(AssignExpr) };

    public void Analyze(RuleContext context)
    {
        if (context.LoopDepth == 0) return;
        if (context.Expression is not AssignExpr assign) return;

        string targetName = assign.Name.Lexeme;

        // Check array literal: result = [...result, item]
        if (assign.Value is ArrayExpr array)
        {
            foreach (var element in array.Elements)
            {
                if (element is SpreadExpr spread &&
                    spread.Expression is IdentifierExpr id &&
                    id.Name.Lexeme == targetName)
                {
                    context.ReportDiagnostic(
                        DiagnosticDescriptors.SA1201.CreateDiagnostic(spread.Span, targetName));
                    return;
                }
            }
        }

        // Check dict literal: merged = {...merged, ...item}
        if (assign.Value is DictLiteralExpr dict)
        {
            foreach (var entry in dict.Entries)
            {
                if (entry.Key == null && entry.Value is SpreadExpr spread &&
                    spread.Expression is IdentifierExpr id &&
                    id.Name.Lexeme == targetName)
                {
                    context.ReportDiagnostic(
                        DiagnosticDescriptors.SA1201.CreateDiagnostic(spread.Span, targetName));
                    return;
                }
            }
        }
    }
}
