namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Parsing.AST;

/// <summary>
/// SA0901 — Emits an information diagnostic when an if-statement's then-branch ends with a
/// terminating statement (return, break, continue, throw) and an else branch is present.
/// The else is unnecessary because control flow already left via the terminator.
/// </summary>
public sealed class NoUnnecessaryElseRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0901;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type> { typeof(IfStmt) };

    public void Analyze(RuleContext context)
    {
        if (context.Statement is not IfStmt ifStmt)
        {
            return;
        }

        if (ifStmt.ElseBranch == null)
        {
            return;
        }

        var terminator = GetTerminator(ifStmt.ThenBranch);
        if (terminator == null)
        {
            return;
        }

        context.ReportDiagnostic(DiagnosticDescriptors.SA0901.CreateDiagnostic(ifStmt.ElseBranch.Span, terminator));
    }

    /// <summary>
    /// Returns the keyword name of the terminating statement if the branch unconditionally terminates,
    /// or <c>null</c> if it does not.
    /// </summary>
    private static string? GetTerminator(Stmt branch)
    {
        var last = branch switch
        {
            BlockStmt block when block.Statements.Count > 0 => block.Statements[^1],
            BlockStmt => null,
            _ => branch,
        };

        return last switch
        {
            ReturnStmt => "return",
            BreakStmt => "break",
            ContinueStmt => "continue",
            ThrowStmt => "throw",
            _ => null,
        };
    }
}
