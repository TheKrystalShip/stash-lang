namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Parsing.AST;

/// <summary>
/// SA0211 — Warns when a named function declaration appears inside a loop body.
/// The function object will be recreated on every iteration, which is almost always unintentional.
/// </summary>
public sealed class FunctionInLoopRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0211;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type> { typeof(FnDeclStmt) };

    public void Analyze(RuleContext context)
    {
        if (context.LoopDepth == 0) return;
        if (context.FunctionDepth > 0) return; // Inside a nested function — don't fire
        if (context.Statement is not FnDeclStmt fn) return;

        context.ReportDiagnostic(
            DiagnosticDescriptors.SA0211.CreateDiagnostic(fn.Name.Span, fn.Name.Lexeme));
    }
}
