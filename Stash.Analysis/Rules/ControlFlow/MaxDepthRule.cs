namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Parsing.AST;

/// <summary>
/// SA1002 — Emits an information diagnostic when a function's maximum nesting depth exceeds
/// the configured threshold (default: 5).
/// </summary>
public sealed class MaxDepthRule : IAnalysisRule, IConfigurableRule
{
    /// <summary>Default nesting depth threshold.</summary>
    public const int DefaultThreshold = 5;

    /// <summary>Configurable threshold; defaults to <see cref="DefaultThreshold"/>.</summary>
    public int Threshold { get; private set; } = DefaultThreshold;

    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA1002;

    public void Configure(IReadOnlyDictionary<string, string> options)
    {
        if (options.TryGetValue("maxDepth", out string? val) && int.TryParse(val, out int v) && v > 0)
            Threshold = v;
    }

    /// <summary>Subscribed to FnDeclStmt — analyzed once per function declaration.</summary>
    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type> { typeof(FnDeclStmt) };

    public void Analyze(RuleContext context)
    {
        if (context.Statement is not FnDeclStmt fn)
        {
            return;
        }

        int maxDepth = ComputeMaxDepth(fn.Body.Statements, 0);

        if (maxDepth > Threshold)
        {
            context.ReportDiagnostic(
                DiagnosticDescriptors.SA1002.CreateDiagnostic(fn.Name.Span, maxDepth, Threshold, fn.Name.Lexeme));
        }
    }

    private static int ComputeMaxDepth(IReadOnlyList<Stmt> stmts, int currentDepth)
    {
        int max = currentDepth;
        foreach (var stmt in stmts)
        {
            int d = ComputeMaxDepthStmt(stmt, currentDepth);
            if (d > max)
            {
                max = d;
            }
        }

        return max;
    }

    private static int ComputeMaxDepthStmt(Stmt stmt, int currentDepth)
    {
        switch (stmt)
        {
            case IfStmt ifStmt:
            {
                int inner = currentDepth + 1;
                int max = ifStmt.ThenBranch is BlockStmt thenBlock
                    ? ComputeMaxDepth(thenBlock.Statements, inner)
                    : ComputeMaxDepthStmt(ifStmt.ThenBranch, inner);
                if (ifStmt.ElseBranch != null)
                {
                    int elsDepth = ifStmt.ElseBranch is BlockStmt elseBlock
                        ? ComputeMaxDepth(elseBlock.Statements, inner)
                        : ComputeMaxDepthStmt(ifStmt.ElseBranch, inner);
                    if (elsDepth > max) max = elsDepth;
                }

                return max;
            }

            case WhileStmt whileStmt:
                return ComputeMaxDepth(whileStmt.Body.Statements, currentDepth + 1);

            case DoWhileStmt doWhileStmt:
                return ComputeMaxDepth(doWhileStmt.Body.Statements, currentDepth + 1);

            case ForStmt forStmt:
                return ComputeMaxDepth(forStmt.Body.Statements, currentDepth + 1);

            case ForInStmt forInStmt:
                return ComputeMaxDepth(forInStmt.Body.Statements, currentDepth + 1);

            case TryCatchStmt tryCatch:
            {
                int inner = currentDepth + 1;
                int max = ComputeMaxDepth(tryCatch.TryBody.Statements, inner);
                foreach (var clause in tryCatch.CatchClauses)
                {
                    int catchDepth = ComputeMaxDepth(clause.Body.Statements, inner);
                    if (catchDepth > max) max = catchDepth;
                }

                return max;
            }

            case BlockStmt block:
                // Nested block (not a function body) — count it
                return ComputeMaxDepth(block.Statements, currentDepth + 1);

            default:
                return currentDepth;
        }
    }
}
