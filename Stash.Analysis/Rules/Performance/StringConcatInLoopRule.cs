namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Lexing;
using Stash.Parsing.AST;

/// <summary>
/// SA1202 — Warns when a string variable is concatenated with itself inside a loop body,
/// producing O(n²) string allocations.
/// </summary>
/// <remarks>
/// Both <c>s = s + expr</c> and <c>s += expr</c> are detected. Compound assignments are
/// desugared by the parser into <c>AssignExpr { Name=s, Value=BinaryExpr { Left=IdentifierExpr(s), Op=+, Right=expr } }</c>,
/// so the same pattern covers both forms.
/// </remarks>
public sealed class StringConcatInLoopRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA1202;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type> { typeof(AssignExpr) };

    public void Analyze(RuleContext context)
    {
        if (context.LoopDepth == 0) return;
        if (context.Expression is not AssignExpr assign) return;

        string targetName = assign.Name.Lexeme;

        // Must be: s = s + expr
        if (assign.Value is not BinaryExpr bin) return;
        if (bin.Operator.Type != TokenType.Plus) return;
        if (bin.Left is not IdentifierExpr leftId || leftId.Name.Lexeme != targetName) return;

        // If a known non-string type is inferred, skip to reduce false positives
        var symbol = context.ScopeTree.FindDefinition(targetName, assign.Span.StartLine, assign.Span.StartColumn);
        if (symbol != null && symbol.TypeHint != null && symbol.TypeHint != "string")
            return;

        context.ReportDiagnostic(
            DiagnosticDescriptors.SA1202.CreateDiagnostic(assign.Span));
    }
}
