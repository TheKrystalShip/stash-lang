namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Parsing.AST;

/// <summary>SA0102 — Emits an error when <c>continue</c> appears outside any loop.</summary>
public sealed class ContinueOutsideLoopRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0102;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type> { typeof(ContinueStmt) };

    public void Analyze(RuleContext context)
    {
        if (context.LoopDepth == 0)
        {
            context.ReportDiagnostic(DiagnosticDescriptors.SA0102.CreateDiagnostic(context.Statement!.Span));
        }
    }
}
