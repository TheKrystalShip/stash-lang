namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Analysis.FlowAnalysis;
using Stash.Parsing.AST;

/// <summary>
/// SA0106 — Emits an information diagnostic when statements are unreachable because all preceding
/// branches (both the then-branch and else-branch of an if/else) unconditionally return or throw.
/// </summary>
/// <remarks>
/// This rule detects the case that the existing <see cref="UnreachableCodeRule"/> (SA0104) misses:
/// <code>
/// if (cond) { return 1; } else { return 2; }
/// print("dead"); // SA0106
/// </code>
/// The rule is post-walk and processes every statement block in the program, looking for
/// if/else statements after which all subsequent statements are unreachable.
/// </remarks>
public sealed class UnreachableBranchRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0106;

    /// <summary>Empty — this is a post-walk rule invoked once after the full AST walk.</summary>
    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type>();

    public void Analyze(RuleContext context)
    {
        AnalyzeBlock(context.AllStatements, context);
    }

    private static void AnalyzeBlock(IReadOnlyList<Stmt> stmts, RuleContext context)
    {
        for (int i = 0; i < stmts.Count; i++)
        {
            var stmt = stmts[i];

            // Check if this if/else terminates all branches, making 'i+1..n' unreachable
            if (stmt is IfStmt ifStmt && ifStmt.ElseBranch != null)
            {
                if (AlwaysTerminates(ifStmt.ThenBranch) && AlwaysTerminates(ifStmt.ElseBranch))
                {
                    // All statements after this if/else are unreachable
                    for (int j = i + 1; j < stmts.Count; j++)
                    {
                        context.ReportDiagnostic(
                            DiagnosticDescriptors.SA0106.CreateUnnecessaryDiagnostic(stmts[j].Span));
                    }
                    return; // Don't recurse into already-dead code
                }
            }

            // Recurse into nested blocks
            RecurseIntoStatement(stmt, context);
        }
    }

    private static void RecurseIntoStatement(Stmt stmt, RuleContext context)
    {
        switch (stmt)
        {
            case FnDeclStmt fn:
                AnalyzeBlock(fn.Body.Statements, context);
                break;
            case IfStmt ifStmt:
                RecurseIntoStatement(ifStmt.ThenBranch, context);
                if (ifStmt.ElseBranch != null)
                    RecurseIntoStatement(ifStmt.ElseBranch, context);
                break;
            case BlockStmt block:
                AnalyzeBlock(block.Statements, context);
                break;
            case WhileStmt @while:
                AnalyzeBlock(@while.Body.Statements, context);
                break;
            case DoWhileStmt doWhile:
                AnalyzeBlock(doWhile.Body.Statements, context);
                break;
            case ForStmt @for:
                AnalyzeBlock(@for.Body.Statements, context);
                break;
            case ForInStmt forIn:
                AnalyzeBlock(forIn.Body.Statements, context);
                break;
            case TryCatchStmt tryCatch:
                AnalyzeBlock(tryCatch.TryBody.Statements, context);
                foreach (var clause in tryCatch.CatchClauses)
                    AnalyzeBlock(clause.Body.Statements, context);
                if (tryCatch.FinallyBody != null)
                    AnalyzeBlock(tryCatch.FinallyBody.Statements, context);
                break;
            case StructDeclStmt structDecl:
                foreach (var method in structDecl.Methods)
                    AnalyzeBlock(method.Body.Statements, context);
                break;
            case ExtendStmt extend:
                foreach (var method in extend.Methods)
                    AnalyzeBlock(method.Body.Statements, context);
                break;
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when every execution path through <paramref name="stmt"/>
    /// is guaranteed to terminate (return, throw, or process.exit).
    /// </summary>
    internal static bool AlwaysTerminates(Stmt stmt)
    {
        if (UnreachableCodeRule.IsTerminatingStatement(stmt))
        {
            return true;
        }

        if (stmt is BlockStmt block)
        {
            return AlwaysTerminatesSequence(block.Statements);
        }

        if (stmt is IfStmt ifStmt && ifStmt.ElseBranch != null)
        {
            return AlwaysTerminates(ifStmt.ThenBranch) && AlwaysTerminates(ifStmt.ElseBranch);
        }

        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> when at least one statement in the sequence is a
    /// guaranteed terminator, or an if/else that always terminates.
    /// </summary>
    internal static bool AlwaysTerminatesSequence(IReadOnlyList<Stmt> stmts)
    {
        foreach (var stmt in stmts)
        {
            if (UnreachableCodeRule.IsTerminatingStatement(stmt))
            {
                return true;
            }

            if (stmt is IfStmt ifStmt && ifStmt.ElseBranch != null &&
                AlwaysTerminates(ifStmt.ThenBranch) && AlwaysTerminates(ifStmt.ElseBranch))
            {
                return true;
            }
        }
        return false;
    }
}
