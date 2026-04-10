namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Parsing.AST;

/// <summary>
/// SA0802 — Post-walk rule that emits an unnecessary diagnostic (with a safe removal fix) for
/// every <c>import</c> statement where all imported names are never read.
/// </summary>
public sealed class UnusedImportRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0802;

    /// <summary>Empty — this is a post-walk rule invoked once after the full AST walk.</summary>
    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type>();

    public void Analyze(RuleContext context)
    {
        foreach (var stmt in context.AllStatements)
        {
            if (stmt is not ImportStmt importStmt)
            {
                continue;
            }

            // Only flag imports where ALL names are unused (entire import can be removed).
            bool allUnused = true;
            foreach (var nameToken in importStmt.Names)
            {
                bool hasRead = false;
                foreach (var r in context.ScopeTree.References)
                {
                    if (r.Name == nameToken.Lexeme
                        && r.Kind == ReferenceKind.Read
                        && r.ResolvedSymbol != null
                        && r.ResolvedSymbol.Span.StartLine == nameToken.Span.StartLine
                        && r.ResolvedSymbol.Span.StartColumn == nameToken.Span.StartColumn)
                    {
                        hasRead = true;
                        break;
                    }
                }

                if (hasRead)
                {
                    allUnused = false;
                    break;
                }
            }

            if (!allUnused)
            {
                continue;
            }

            // Emit one SA0802 per unused import statement.
            string importedNames = string.Join(", ", importStmt.Names.ConvertAll(n => n.Lexeme));
            var fix = new CodeFix(
                "Remove unused import",
                FixApplicability.Safe,
                [new SourceEdit(importStmt.Span, "")]
            );
            context.ReportDiagnostic(DiagnosticDescriptors.SA0802.CreateUnnecessaryDiagnosticWithFix(
                importStmt.Span, fix, importedNames));
        }
    }
}
