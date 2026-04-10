namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Parsing.AST;

/// <summary>
/// SA1102 — Emits a warning when a variable is assigned to itself: <c>x = x</c>.
/// </summary>
public sealed class NoSelfAssignRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA1102;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type> { typeof(AssignExpr) };

    public void Analyze(RuleContext context)
    {
        if (context.Expression is not AssignExpr assign)
        {
            return;
        }

        if (assign.Value is IdentifierExpr rhs && rhs.Name.Lexeme == assign.Name.Lexeme)
        {
            context.ReportDiagnostic(DiagnosticDescriptors.SA1102.CreateDiagnostic(assign.Span, assign.Name.Lexeme));
        }
    }
}
