namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Parsing.AST;

/// <summary>
/// SA0305 — Emits a warning when a variable assignment's right-hand side has an inferred type
/// that is incompatible with the variable's declared type annotation.
/// </summary>
public sealed class AssignmentTypeMismatchRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0305;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type> { typeof(AssignExpr) };

    public void Analyze(RuleContext context)
    {
        if (context.Expression is not AssignExpr expr)
        {
            return;
        }

        var line = expr.Name.Span.StartLine;
        var col = expr.Name.Span.StartColumn;
        var definition = context.ScopeTree.FindDefinition(expr.Name.Lexeme, line, col);
        if (definition == null || !definition.IsExplicitTypeHint || definition.TypeHint == null)
        {
            return;
        }

        var valueType = TypeInferenceEngine.InferExpressionType(context.ScopeTree, expr.Value, line, col);
        if (valueType != null && valueType != "null" && valueType != definition.TypeHint)
        {
            context.ReportDiagnostic(DiagnosticDescriptors.SA0305.CreateDiagnostic(expr.Name.Span, valueType, expr.Name.Lexeme, definition.TypeHint));
        }
    }
}
