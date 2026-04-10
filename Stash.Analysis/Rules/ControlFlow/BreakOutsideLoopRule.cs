namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Parsing.AST;

/// <summary>SA0101 — Emits an error when <c>break</c> appears outside any loop.</summary>
public sealed class BreakOutsideLoopRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0101;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type> { typeof(BreakStmt) };

    public void Analyze(RuleContext context)
    {
        if (context.LoopDepth == 0)
        {
            context.ReportDiagnostic(DiagnosticDescriptors.SA0101.CreateDiagnostic(context.Statement!.Span));
        }
    }
}
