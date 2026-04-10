namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Parsing.AST;

/// <summary>SA0103 — Emits an error when <c>return</c> appears outside any function.</summary>
public sealed class ReturnOutsideFunctionRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0103;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type> { typeof(ReturnStmt) };

    public void Analyze(RuleContext context)
    {
        if (context.FunctionDepth == 0)
        {
            context.ReportDiagnostic(DiagnosticDescriptors.SA0103.CreateDiagnostic(context.Statement!.Span));
        }
    }
}
