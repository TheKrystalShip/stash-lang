namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Parsing.AST;

/// <summary>
/// SA0302 — Emits a warning when a constant is declared with an explicit type annotation, but
/// its initializer expression has an incompatible inferred type.
/// </summary>
public sealed class ConstantTypeMismatchRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0302;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type> { typeof(ConstDeclStmt) };

    public void Analyze(RuleContext context)
    {
        if (context.Statement is not ConstDeclStmt stmt)
        {
            return;
        }

        if (stmt.TypeHint == null)
        {
            return;
        }

        var expectedType = stmt.TypeHint.Lexeme;
        var actualType = TypeInferenceEngine.InferExpressionType(context.ScopeTree, stmt.Initializer, stmt.Name.Span.StartLine, stmt.Name.Span.StartColumn);
        if (actualType != null && actualType != "null" && actualType != expectedType)
        {
            context.ReportDiagnostic(DiagnosticDescriptors.SA0302.CreateDiagnostic(stmt.Initializer.Span, stmt.Name.Lexeme, expectedType, actualType));
        }
    }
}
