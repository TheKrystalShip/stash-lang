namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Parsing.AST;

/// <summary>
/// SA0105 — Emits an information diagnostic when a loop, conditional, or handler body is
/// an empty block, indicating that the block has no effect.
/// </summary>
public sealed class EmptyBlockRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0105;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type>
    {
        typeof(WhileStmt),
        typeof(DoWhileStmt),
        typeof(ForStmt),
        typeof(ForInStmt),
        typeof(IfStmt),
        typeof(TryCatchStmt),
    };

    public void Analyze(RuleContext context)
    {
        switch (context.Statement)
        {
            case WhileStmt ws when ws.Body is BlockStmt wb && wb.Statements.Count == 0:
                context.ReportDiagnostic(DiagnosticDescriptors.SA0105.CreateDiagnostic(wb.Span, "while"));
                break;

            case DoWhileStmt dws when dws.Body is BlockStmt dwb && dwb.Statements.Count == 0:
                context.ReportDiagnostic(DiagnosticDescriptors.SA0105.CreateDiagnostic(dwb.Span, "do-while"));
                break;

            case ForStmt fs when fs.Body is BlockStmt fb && fb.Statements.Count == 0:
                context.ReportDiagnostic(DiagnosticDescriptors.SA0105.CreateDiagnostic(fb.Span, "for"));
                break;

            case ForInStmt fis when fis.Body is BlockStmt fib && fib.Statements.Count == 0:
                context.ReportDiagnostic(DiagnosticDescriptors.SA0105.CreateDiagnostic(fib.Span, "for-in"));
                break;

            case IfStmt ifs:
                if (ifs.ThenBranch is BlockStmt thenBlock && thenBlock.Statements.Count == 0)
                {
                    context.ReportDiagnostic(DiagnosticDescriptors.SA0105.CreateDiagnostic(thenBlock.Span, "if"));
                }
                if (ifs.ElseBranch is BlockStmt elseBlock && elseBlock.Statements.Count == 0)
                {
                    context.ReportDiagnostic(DiagnosticDescriptors.SA0105.CreateDiagnostic(elseBlock.Span, "else"));
                }
                break;

            case TryCatchStmt tcs:
                if (tcs.TryBody is BlockStmt tryBlock && tryBlock.Statements.Count == 0)
                {
                    context.ReportDiagnostic(DiagnosticDescriptors.SA0105.CreateDiagnostic(tryBlock.Span, "try"));
                }
                foreach (var clause in tcs.CatchClauses)
                {
                    if (clause.Body.Statements.Count == 0)
                        context.ReportDiagnostic(DiagnosticDescriptors.SA0105.CreateDiagnostic(clause.Body.Span, "catch"));
                }
                break;
        }
    }
}
