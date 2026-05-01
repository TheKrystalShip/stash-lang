namespace Stash.Analysis.Rules;

using System;
using System.Collections.Generic;
using Stash.Parsing.AST;

/// <summary>
/// SA0201 — Post-walk rule that emits an information (unnecessary) diagnostic for every declared
/// symbol that is never referenced in the document.
/// </summary>
public sealed class UnusedDeclarationRule : IAnalysisRule
{
    public DiagnosticDescriptor Descriptor => DiagnosticDescriptors.SA0201;

    /// <summary>Empty — this is a post-walk rule invoked once after the full AST walk.</summary>
    public IReadOnlySet<Type> SubscribedNodeTypes { get; } = new HashSet<Type>();

    public void Analyze(RuleContext context)
    {
        var globalSymbols = new HashSet<SymbolInfo>(context.ScopeTree.GlobalScope.Symbols);

        // Collect names that appear as unset targets — these count as a use of the binding.
        var unsetNames = new HashSet<string>();
        foreach (var stmt in context.AllStatements)
        {
            if (stmt is UnsetStmt us)
            {
                foreach (var t in us.Targets)
                    unsetNames.Add(t.Name);
            }
        }

        foreach (var symbol in context.ScopeTree.All)
        {
            // Skip built-ins (injected at line 0)
            if (symbol.Span.StartLine == 0)
            {
                continue;
            }

            // Skip intentionally unused names (convention)
            if (symbol.Name == "_")
            {
                continue;
            }

            bool isTopLevel = globalSymbols.Contains(symbol);

            // Top-level functions, structs, and enums are auto-exported (public API), so only
            // flag them if they're imported. Variables and constants at file scope are still
            // checked — they're typically not imported by name and unused ones are noise.
            if (isTopLevel && symbol.SourceUri == null
                && symbol.Kind is SymbolKind.Function or SymbolKind.Struct or SymbolKind.Enum)
            {
                continue;
            }

            // Only check variables, constants, loop variables, and imported namespaces.
            // Parameters are excluded — they are commonly unused in callbacks and interface implementations.
            if (symbol.Kind is not (SymbolKind.Variable or SymbolKind.Constant
                or SymbolKind.LoopVariable or SymbolKind.Namespace
                or SymbolKind.Function or SymbolKind.Struct or SymbolKind.Enum))
            {
                continue;
            }

            // For non-imported top-level symbols, we already skipped above.
            // For non-top-level Function/Struct/Enum, skip — these are rare and usually intentional.
            if (!isTopLevel && symbol.Kind is SymbolKind.Function or SymbolKind.Struct or SymbolKind.Enum)
            {
                continue;
            }

            // Check whether any reference in the tree resolves to this symbol.
            bool isUsed = false;
            foreach (var r in context.ScopeTree.References)
            {
                if (r.ResolvedSymbol == symbol
                    || (symbol.Kind == SymbolKind.Namespace && r.Name == symbol.Name && r.ResolvedSymbol != null))
                {
                    isUsed = true;
                    break;
                }
            }

            // An unset target counts as a use — let x = ...; unset x; should not warn.
            if (!isUsed && unsetNames.Contains(symbol.Name))
                isUsed = true;

            if (isUsed)
            {
                continue;
            }

            // Imported names (from ImportStmt) are handled separately by SA0802 (UnusedImportRule).
            if (symbol.Detail?.StartsWith("imported from ", StringComparison.Ordinal) == true)
            {
                continue;
            }

            string label = symbol.Kind switch
            {
                SymbolKind.LoopVariable => "Loop variable",
                SymbolKind.Constant => "Constant",
                SymbolKind.Namespace => "Import",
                _ when symbol.SourceUri != null => "Import",
                _ => "Variable"
            };

            context.ReportDiagnostic(DiagnosticDescriptors.SA0201.CreateUnnecessaryDiagnostic(symbol.Span, label, symbol.Name));
        }
    }
}
