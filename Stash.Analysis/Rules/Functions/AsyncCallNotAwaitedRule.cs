namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Parsing.AST;

/// <summary>
/// SA0406 — Warns when an async function call result is not awaited at statement level.
/// </summary>
public sealed class AsyncCallNotAwaitedRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0406;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type> { typeof(ExprStmt) };

    public void Analyze(RuleContext context)
    {
        if (context.Statement is not ExprStmt exprStmt) return;

        // If it's awaited, it's fine
        if (exprStmt.Expression is AwaitExpr) return;

        // Must be a bare call expression (not awaited)
        if (exprStmt.Expression is not CallExpr call) return;

        // Only handle direct identifier calls (simple name resolution)
        if (call.Callee is not IdentifierExpr ident) return;

        string funcName = ident.Name.Lexeme;
        int line = call.Span.StartLine;
        int col = call.Span.StartColumn;

        var symbol = context.ScopeTree.FindDefinition(funcName, line, col);
        if (symbol == null || !symbol.IsAsync) return;

        context.ReportDiagnostic(
            DiagnosticDescriptors.SA0406.CreateDiagnostic(call.Span, funcName));
    }
}
