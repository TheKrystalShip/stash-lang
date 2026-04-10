namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Common;
using Stash.Parsing.AST;

/// <summary>
/// SA0203 — Emits an error when a constant is assigned a new value after its declaration,
/// with an optional unsafe autofix that changes <c>const</c> to <c>let</c>.
/// </summary>
public sealed class ConstantReassignmentRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0203;

    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type> { typeof(AssignExpr) };

    public void Analyze(RuleContext context)
    {
        if (context.Expression is not AssignExpr expr)
        {
            return;
        }

        var line = expr.Name.Span.StartLine;
        var col = expr.Name.Span.StartColumn;
        var definition = context.ScopeTree.FindDefinition(expr.Name.Lexeme, line, col);
        if (definition == null || definition.Kind != SymbolKind.Constant)
        {
            return;
        }

        var fix = BuildConstToLetFix(definition);
        if (fix != null)
        {
            context.ReportDiagnostic(DiagnosticDescriptors.SA0203.CreateDiagnosticWithFix(expr.Name.Span, fix, expr.Name.Lexeme));
        }
        else
        {
            context.ReportDiagnostic(DiagnosticDescriptors.SA0203.CreateDiagnostic(expr.Name.Span, expr.Name.Lexeme));
        }
    }

    private static CodeFix? BuildConstToLetFix(SymbolInfo definition)
    {
        if (definition.FullSpan is not { } fullSpan)
        {
            return null;
        }

        // "const" keyword occupies 5 characters starting at the full-span's start column.
        var keywordSpan = new SourceSpan(
            fullSpan.File,
            fullSpan.StartLine,
            fullSpan.StartColumn,
            fullSpan.StartLine,
            fullSpan.StartColumn + 4);  // 1-based inclusive: "const" = cols [N, N+4]

        return new CodeFix(
            "Change 'const' to 'let' (may change semantics)",
            FixApplicability.Unsafe,
            [new SourceEdit(keywordSpan, "let")]);
    }
}
