namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Parsing.AST;

/// <summary>
/// SA0404 — Emits a warning when a function declared with an explicit return type annotation
/// has at least one code path that does not end with a <c>return</c> statement.
/// </summary>
/// <remarks>
/// Only functions with a <see cref="FnDeclStmt.ReturnType"/> annotation are checked. Functions
/// without a return type annotation are assumed to return <c>null</c> implicitly.
///
/// <code>
/// fn compute(): int {
///     if (cond) { return 1; }
///     // SA0404 — else path falls off the end without returning
/// }
/// </code>
/// </remarks>
public sealed class MissingReturnRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0404;

    /// <summary>Subscribed to FnDeclStmt — analyzed once per function declaration.</summary>
    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type> { typeof(FnDeclStmt) };

    public void Analyze(RuleContext context)
    {
        if (context.Statement is not FnDeclStmt fn)
        {
            return;
        }

        // Only check functions with explicit return type annotations
        if (fn.ReturnType == null)
        {
            return;
        }

        // Skip void-like annotations
        var returnTypeLexeme = fn.ReturnType.Lexeme;
        if (returnTypeLexeme is "void" or "never")
        {
            return;
        }

        // Check if there's a code path that doesn't return
        bool alwaysReturns = UnreachableBranchRule.AlwaysTerminatesSequence(fn.Body.Statements);
        if (!alwaysReturns)
        {
            context.ReportDiagnostic(
                DiagnosticDescriptors.SA0404.CreateDiagnostic(fn.Name.Span, fn.Name.Lexeme));
        }
    }
}
