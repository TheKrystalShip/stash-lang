namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Parsing.AST;

/// <summary>
/// SA0304 — Emits a warning when a struct field is assigned a value whose inferred type is
/// incompatible with the field's declared type.
/// </summary>
public sealed class FieldTypeMismatchRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0304;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type> { typeof(DotAssignExpr) };

    public void Analyze(RuleContext context)
    {
        if (context.Expression is not DotAssignExpr expr)
        {
            return;
        }

        var line = expr.Name.Span.StartLine;
        var col = expr.Name.Span.StartColumn;
        var receiverType = TypeInferenceEngine.InferExpressionType(context.ScopeTree, expr.Object, line, col);
        if (receiverType == null)
        {
            return;
        }

        var field = context.ScopeTree.FindField(receiverType, expr.Name.Lexeme);
        if (field?.TypeHint == null)
        {
            return;
        }

        var valueType = TypeInferenceEngine.InferExpressionType(context.ScopeTree, expr.Value, line, col);
        if (valueType != null && valueType != "null" && valueType != field.TypeHint)
        {
            context.ReportDiagnostic(DiagnosticDescriptors.SA0304.CreateDiagnostic(expr.Name.Span, valueType, expr.Name.Lexeme, field.TypeHint));
        }
    }
}
