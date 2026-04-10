namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Common;

/// <summary>
/// SA0205 — Post-walk rule that suggests changing <c>let</c> to <c>const</c> for variables
/// that are never reassigned after their initial declaration.
/// </summary>
public sealed class LetCouldBeConstRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0205;

    /// <summary>Empty — this is a post-walk rule invoked once after the full AST walk.</summary>
    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type>();

    public void Analyze(RuleContext context)
    {
        foreach (var symbol in context.ScopeTree.All)
        {
            if (symbol.Kind != SymbolKind.Variable) continue;
            if (symbol.Span.StartLine == 0) continue;
            if (symbol.Name == "_") continue;
            if (symbol.Detail == "caught error") continue;

            bool hasWrite = false;
            foreach (var r in context.ScopeTree.References)
            {
                if (r.ResolvedSymbol == symbol && r.Kind == ReferenceKind.Write)
                {
                    hasWrite = true;
                    break;
                }
            }

            if (!hasWrite)
            {
                var fix = BuildLetToConstFix(symbol);
                if (fix != null)
                {
                    context.ReportDiagnostic(DiagnosticDescriptors.SA0205.CreateDiagnosticWithFix(symbol.Span, fix, symbol.Name));
                }
                else
                {
                    context.ReportDiagnostic(DiagnosticDescriptors.SA0205.CreateDiagnostic(symbol.Span, symbol.Name));
                }
            }
        }
    }

    private static CodeFix? BuildLetToConstFix(SymbolInfo symbol)
    {
        if (symbol.FullSpan is not { } fullSpan)
        {
            return null;
        }

        // "let" keyword occupies 3 characters starting at the full-span's start column.
        var keywordSpan = new SourceSpan(
            fullSpan.File,
            fullSpan.StartLine,
            fullSpan.StartColumn,
            fullSpan.StartLine,
            fullSpan.StartColumn + 2);  // 1-based inclusive: "let" = cols [N, N+2]

        return new CodeFix(
            "Change 'let' to 'const'",
            FixApplicability.Safe,
            [new SourceEdit(keywordSpan, "const")]);
    }
}
