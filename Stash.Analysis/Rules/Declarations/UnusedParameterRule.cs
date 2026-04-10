namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;

/// <summary>
/// SA0206 — Post-walk rule that emits an information (unnecessary) diagnostic for every function
/// parameter that is never referenced in the function body.
/// </summary>
/// <remarks>
/// Parameters prefixed with <c>_</c> are conventionally marked as intentionally unused and are skipped.
/// </remarks>
public sealed class UnusedParameterRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0206;

    /// <summary>Empty — this is a post-walk rule invoked once after the full AST walk.</summary>
    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type>();

    public void Analyze(RuleContext context)
    {
        foreach (var symbol in context.ScopeTree.All)
        {
            if (symbol.Kind != SymbolKind.Parameter) continue;
            if (symbol.Span.StartLine == 0) continue;
            if (symbol.Name.StartsWith('_')) continue;

            bool isUsed = false;
            foreach (var r in context.ScopeTree.References)
            {
                if (r.ResolvedSymbol == symbol)
                {
                    isUsed = true;
                    break;
                }
            }

            if (!isUsed)
            {
                context.ReportDiagnostic(DiagnosticDescriptors.SA0206.CreateUnnecessaryDiagnostic(symbol.Span, symbol.Name));
            }
        }
    }
}
