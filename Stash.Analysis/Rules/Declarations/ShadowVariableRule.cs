namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;

/// <summary>
/// SA0207 — Post-walk rule that emits a warning when a variable, constant, loop variable, or
/// parameter has the same name as an outer variable in an enclosing scope.
/// </summary>
public sealed class ShadowVariableRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0207;

    /// <summary>Empty — this is a post-walk rule invoked once after the full AST walk.</summary>
    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type>();

    public void Analyze(RuleContext context)
    {
        CheckShadowsInScope(context.ScopeTree.GlobalScope, context);
    }

    private static void CheckShadowsInScope(Scope scope, RuleContext context)
    {
        foreach (var symbol in scope.Symbols)
        {
            if (symbol.Span.StartLine == 0) continue;
            if (symbol.Kind is not (SymbolKind.Variable or SymbolKind.Constant or SymbolKind.LoopVariable or SymbolKind.Parameter)) continue;

            var ancestor = scope.Parent;
            while (ancestor != null)
            {
                foreach (var outerSym in ancestor.GetSymbolsByName(symbol.Name))
                {
                    if (outerSym.Span.StartLine == 0) continue;
                    if (outerSym.Kind is SymbolKind.Variable or SymbolKind.Constant or SymbolKind.LoopVariable or SymbolKind.Parameter)
                    {
                        context.ReportDiagnostic(DiagnosticDescriptors.SA0207.CreateDiagnostic(symbol.Span, symbol.Name));
                        goto nextSymbol;
                    }
                }
                ancestor = ancestor.Parent;
            }
            nextSymbol:;
        }

        foreach (var child in scope.Children)
        {
            CheckShadowsInScope(child, context);
        }
    }
}
