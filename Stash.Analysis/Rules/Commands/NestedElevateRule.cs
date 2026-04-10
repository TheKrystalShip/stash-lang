namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Parsing.AST;

/// <summary>
/// SA0701 — Emits a warning when an <c>elevate</c> block appears nested inside another
/// <c>elevate</c> block, which has no additional effect.
/// </summary>
public sealed class NestedElevateRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0701;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type> { typeof(ElevateStmt) };

    public void Analyze(RuleContext context)
    {
        if (context.ElevateDepth > 0)
        {
            context.ReportDiagnostic(DiagnosticDescriptors.SA0701.CreateDiagnostic(context.Statement!.Span));
        }
    }
}
