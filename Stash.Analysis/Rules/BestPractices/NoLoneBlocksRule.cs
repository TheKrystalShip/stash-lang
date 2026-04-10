namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Lexing;
using Stash.Parsing.AST;

/// <summary>
/// SA1105 — Emits an information diagnostic when a <c>BlockStmt</c> appears as a standalone
/// statement in a statement list (not as the body of a loop, if, or function).
/// </summary>
public sealed class NoLoneBlocksRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA1105;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type> { typeof(BlockStmt) };

    public void Analyze(RuleContext context)
    {
        if (context.Statement is not BlockStmt block)
        {
            return;
        }

        // A lone block appears directly in AllStatements. Control-structure bodies
        // (while-body, for-body, if-body) are visited via Accept() calls from their
        // parent visitor but are NOT present in _allStatements, so Contains returns false.
        if (context.AllStatements.Contains(block))
        {
            context.ReportDiagnostic(DiagnosticDescriptors.SA1105.CreateDiagnostic(block.Span));
        }
    }
}
