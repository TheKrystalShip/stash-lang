namespace Stash.Lsp.Analysis;

using System;
using System.Collections.Generic;
using System.Linq;
using Stash.Common;

public class ScopeTree
{
    public Scope GlobalScope { get; }
    public IReadOnlyList<ReferenceInfo> References { get; }

    public ScopeTree(Scope globalScope, IEnumerable<ReferenceInfo>? references = null)
    {
        GlobalScope = globalScope;
        References = references?.ToList() ?? new List<ReferenceInfo>();
    }

    /// <summary>
    /// Finds the innermost scope that contains the given position.
    /// </summary>
    public Scope FindScopeAt(int line, int column)
    {
        return FindScopeAt(GlobalScope, line, column);
    }

    private Scope FindScopeAt(Scope scope, int line, int column)
    {
        // Check children first (more specific scopes)
        foreach (var child in scope.Children)
        {
            if (ContainsPosition(child.Span, line, column))
            {
                return FindScopeAt(child, line, column);
            }
        }

        return scope;
    }

    /// <summary>
    /// Returns all symbols visible from a given position, walking up the scope chain.
    /// Only includes symbols declared before (or at) the given position.
    /// </summary>
    public IEnumerable<SymbolInfo> GetVisibleSymbols(int line, int column)
    {
        var scope = FindScopeAt(line, column);
        var seen = new HashSet<string>();
        var result = new List<SymbolInfo>();

        while (scope != null)
        {
            foreach (var sym in scope.Symbols)
            {
                // Only include symbols declared before or at the query position
                if (IsBeforeOrAt(sym.Span, line, column) && seen.Add(sym.Name))
                {
                    result.Add(sym);
                }
            }

            scope = scope.Parent;
        }

        return result;
    }

    /// <summary>
    /// Finds the definition of a symbol visible at the given position.
    /// Walks from the innermost scope outward, returning the first match.
    /// </summary>
    public SymbolInfo? FindDefinition(string name, int line, int column)
    {
        var scope = FindScopeAt(line, column);

        while (scope != null)
        {
            // Search this scope's symbols in reverse order to find the closest declaration
            for (int i = scope.Symbols.Count - 1; i >= 0; i--)
            {
                var sym = scope.Symbols[i];
                if (sym.Name == name && IsBeforeOrAt(sym.Span, line, column))
                {
                    return sym;
                }
            }

            scope = scope.Parent;
        }

        return null;
    }

    /// <summary>
    /// Gets all symbols in the global scope (top-level declarations).
    /// </summary>
    public IEnumerable<SymbolInfo> GetTopLevel()
    {
        return GlobalScope.Symbols.Where(s =>
            s.Kind is SymbolKind.Function or SymbolKind.Struct
                or SymbolKind.Enum or SymbolKind.Variable or SymbolKind.Constant);
    }

    /// <summary>
    /// Gets all symbols across all scopes (flat list).
    /// </summary>
    public IReadOnlyList<SymbolInfo> All
    {
        get
        {
            var result = new List<SymbolInfo>();
            CollectAll(GlobalScope, result);
            return result;
        }
    }

    private void CollectAll(Scope scope, List<SymbolInfo> result)
    {
        result.AddRange(scope.Symbols);
        foreach (var child in scope.Children)
        {
            CollectAll(child, result);
        }
    }

    /// <summary>
    /// Gets the global scope plus its direct children suitable for hierarchical document symbols.
    /// Returns top-level symbols with their children (e.g., struct fields, enum members, function params).
    /// </summary>
    public IEnumerable<(SymbolInfo Symbol, IReadOnlyList<SymbolInfo> Children)> GetHierarchicalSymbols()
    {
        foreach (var sym in GlobalScope.Symbols)
        {
            if (sym.Kind is SymbolKind.Function or SymbolKind.Struct or SymbolKind.Enum
                or SymbolKind.Variable or SymbolKind.Constant)
            {
                // Find the child scope that corresponds to this symbol (if any)
                var childSymbols = new List<SymbolInfo>();

                if (sym.Kind is SymbolKind.Struct)
                {
                    // Struct fields and methods are in the global scope
                    childSymbols.AddRange(GlobalScope.Symbols.Where(s =>
                        (s.Kind == SymbolKind.Field || s.Kind == SymbolKind.Method) && s.ParentName == sym.Name));
                }
                else if (sym.Kind is SymbolKind.Enum)
                {
                    childSymbols.AddRange(GlobalScope.Symbols.Where(s =>
                        s.Kind == SymbolKind.EnumMember && s.ParentName == sym.Name));
                }
                else if (sym.Kind is SymbolKind.Function && sym.FullSpan != null)
                {
                    // Find the function's scope and get its parameters
                    foreach (var childScope in GlobalScope.Children)
                    {
                        if (childScope.Kind == ScopeKind.Function &&
                            ContainsSpan(sym.FullSpan, childScope.Span))
                        {
                            childSymbols.AddRange(childScope.Symbols.Where(s =>
                                s.Kind == SymbolKind.Parameter && s.ParentName == sym.Name));
                            break;
                        }
                    }
                }

                yield return (sym, childSymbols);
            }
        }
    }

    /// <summary>
    /// Finds all references to a symbol identified by name at the given position.
    /// First resolves the declaration, then finds all references that resolve to the same declaration.
    /// Also includes the declaration itself.
    /// </summary>
    public IReadOnlyList<ReferenceInfo> FindReferences(string name, int line, int column)
    {
        var definition = FindDefinition(name, line, column);
        if (definition == null)
        {
            return Array.Empty<ReferenceInfo>();
        }

        var result = new List<ReferenceInfo>();

        // Include the declaration itself as a reference
        result.Add(new ReferenceInfo(definition.Name, definition.Span, ReferenceKind.Write, definition));

        // Find all references that resolve to this specific declaration
        foreach (var reference in References)
        {
            if (reference.ResolvedSymbol == definition)
            {
                result.Add(reference);
            }
        }

        return result;
    }

    /// <summary>
    /// Gets all unresolved references (identifiers used but not declared).
    /// Excludes known built-in names.
    /// </summary>
    public IReadOnlyList<ReferenceInfo> GetUnresolvedReferences(HashSet<string>? knownNames = null)
    {
        var result = new List<ReferenceInfo>();
        foreach (var reference in References)
        {
            if (reference.ResolvedSymbol == null)
            {
                if (knownNames != null && knownNames.Contains(reference.Name))
                {
                    continue;
                }

                result.Add(reference);
            }
        }
        return result;
    }

    private static bool ContainsPosition(SourceSpan span, int line, int column)
    {
        if (line < span.StartLine || line > span.EndLine)
        {
            return false;
        }

        if (line == span.StartLine && column < span.StartColumn)
        {
            return false;
        }

        if (line == span.EndLine && column > span.EndColumn)
        {
            return false;
        }

        return true;
    }

    private static bool ContainsSpan(SourceSpan outer, SourceSpan inner)
    {
        if (inner.StartLine < outer.StartLine || inner.EndLine > outer.EndLine)
        {
            return false;
        }

        if (inner.StartLine == outer.StartLine && inner.StartColumn < outer.StartColumn)
        {
            return false;
        }

        if (inner.EndLine == outer.EndLine && inner.EndColumn > outer.EndColumn)
        {
            return false;
        }

        return true;
    }

    private static bool IsBeforeOrAt(SourceSpan declaration, int line, int column)
    {
        if (declaration.StartLine < line)
        {
            return true;
        }

        if (declaration.StartLine == line && declaration.StartColumn <= column)
        {
            return true;
        }

        return false;
    }
}
