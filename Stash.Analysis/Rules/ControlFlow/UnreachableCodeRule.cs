namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Parsing.AST;

/// <summary>
/// SA0104 — Emits an information diagnostic (rendered as faded/unnecessary code) for every
/// statement that follows a terminating statement within a block.
/// </summary>
/// <remarks>
/// This rule is not dispatched via <see cref="SubscribedNodeTypes"/>. Instead, the
/// <see cref="SemanticValidator"/> calls <see cref="Analyze"/> directly from its
/// <c>CheckUnreachableStatements</c> helper with <see cref="RuleContext.AllStatements"/> set
/// to the block's statement list.
/// </remarks>
public sealed class UnreachableCodeRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0104;

    /// <summary>Empty — this rule is invoked directly, not via the per-node dispatch table.</summary>
    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type>();

    /// <summary>
    /// Scans <see cref="RuleContext.AllStatements"/> (a single block's statements) and emits
    /// SA0104 for every statement that follows a terminating statement.
    /// </summary>
    public void Analyze(RuleContext context)
    {
        bool reachable = true;
        foreach (var stmt in context.AllStatements)
        {
            if (!reachable)
            {
                context.ReportDiagnostic(DiagnosticDescriptors.SA0104.CreateUnnecessaryDiagnostic(stmt.Span));
                continue;
            }

            if (IsTerminatingStatement(stmt))
            {
                reachable = false;
            }
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="stmt"/> unconditionally terminates
    /// control flow, making any following statement unreachable.
    /// </summary>
    internal static bool IsTerminatingStatement(Stmt stmt)
    {
        if (stmt is ReturnStmt || stmt is BreakStmt || stmt is ContinueStmt || stmt is ThrowStmt)
        {
            return true;
        }

        // process.exit(...) call
        if (stmt is ExprStmt exprStmt && exprStmt.Expression is CallExpr call &&
            call.Callee is DotExpr dot &&
            dot.Object is IdentifierExpr obj && obj.Name.Lexeme == "process" &&
            dot.Name.Lexeme == "exit")
        {
            return true;
        }

        return false;
    }
}
