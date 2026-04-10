namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Lexing;
using Stash.Parsing.AST;

/// <summary>
/// SA0301 — Emits a warning when a variable is declared with an explicit type annotation, but
/// its initializer expression has an incompatible inferred type.
/// </summary>
public sealed class VariableTypeMismatchRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0301;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type> { typeof(VarDeclStmt) };

    public void Analyze(RuleContext context)
    {
        if (context.Statement is not VarDeclStmt stmt)
        {
            return;
        }

        if (stmt.TypeHint == null || stmt.Initializer == null)
        {
            return;
        }

        var expectedType = stmt.TypeHint.Lexeme;
        var actualType = TypeInferenceEngine.InferExpressionType(context.ScopeTree, stmt.Initializer, stmt.Name.Span.StartLine, stmt.Name.Span.StartColumn);
        if (actualType != null && actualType != "null" && actualType != expectedType)
        {
            context.ReportDiagnostic(DiagnosticDescriptors.SA0301.CreateDiagnostic(stmt.Initializer.Span, stmt.Name.Lexeme, expectedType, actualType));
        }
    }
}
