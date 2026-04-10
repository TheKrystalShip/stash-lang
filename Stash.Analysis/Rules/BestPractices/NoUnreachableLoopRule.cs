namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Parsing.AST;

/// <summary>
/// SA1108 — Emits a warning when a loop body always terminates on the first iteration via an
/// unconditional <c>return</c>, <c>throw</c>, or <c>break</c> as the last statement,
/// making the loop execute at most once.
/// </summary>
public sealed class NoUnreachableLoopRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA1108;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type>
    {
        typeof(WhileStmt),
        typeof(DoWhileStmt),
        typeof(ForStmt),
        typeof(ForInStmt),
    };

    public void Analyze(RuleContext context)
    {
        BlockStmt? body = context.Statement switch
        {
            WhileStmt ws => ws.Body,
            DoWhileStmt dws => dws.Body,
            ForStmt fs => fs.Body,
            ForInStmt fis => fis.Body,
            _ => null,
        };

        if (body == null || body.Statements.Count == 0)
        {
            return;
        }

        var lastStmt = body.Statements[^1];
        if (IsUnconditionalTerminator(lastStmt))
        {
            context.ReportDiagnostic(
                DiagnosticDescriptors.SA1108.CreateDiagnostic(context.Statement!.Span));
        }
    }

    private static bool IsUnconditionalTerminator(Stmt stmt) => stmt switch
    {
        ReturnStmt => true,
        ThrowStmt => true,
        // break is NOT included — it's the expected exit mechanism for loops,
        // especially the common while(true) { ... break; } idiom.
        _ => false,
    };
}
