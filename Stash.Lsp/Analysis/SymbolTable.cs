namespace Stash.Lsp.Analysis;

using System.Collections.Generic;
using System.Linq;
using Stash.Common;

public class SymbolTable
{
    private readonly List<SymbolInfo> _symbols = new();

    public void Add(SymbolInfo symbol) => _symbols.Add(symbol);

    public IReadOnlyList<SymbolInfo> All => _symbols;

    public IEnumerable<SymbolInfo> GetTopLevel() =>
        _symbols.Where(s => s.Kind is SymbolKind.Function or SymbolKind.Struct
            or SymbolKind.Enum or SymbolKind.Variable or SymbolKind.Constant);

    public SymbolInfo? FindDefinition(string name, SourceSpan atPosition)
    {
        // Return the closest declaration that appears before or at the usage position
        // Prefer narrower scope (later in list = more nested)
        SymbolInfo? best = null;
        foreach (var sym in _symbols)
        {
            if (sym.Name == name && IsBeforeOrAt(sym.Span, atPosition))
            {
                best = sym;
            }
        }
        return best;
    }

    public IEnumerable<SymbolInfo> FindByName(string name) =>
        _symbols.Where(s => s.Name == name);

    public SymbolInfo? FindAtPosition(int line, int column)
    {
        foreach (var sym in _symbols)
        {
            if (sym.Span.StartLine == line && sym.Span.StartColumn <= column
                && sym.Span.EndColumn >= column)
            {
                return sym;
            }
            if (sym.Span.StartLine <= line && sym.Span.EndLine >= line)
            {
                if (sym.Span.StartLine == line && sym.Span.StartColumn > column)
                {
                    continue;
                }

                if (sym.Span.EndLine == line && sym.Span.EndColumn < column)
                {
                    continue;
                }
                // Multi-line span check
                if (sym.Span.StartLine < line && sym.Span.EndLine > line)
                {
                    return sym;
                }
            }
        }
        return null;
    }

    private static bool IsBeforeOrAt(SourceSpan declaration, SourceSpan usage)
    {
        if (declaration.StartLine < usage.StartLine)
        {
            return true;
        }

        if (declaration.StartLine == usage.StartLine && declaration.StartColumn <= usage.StartColumn)
        {
            return true;
        }

        return false;
    }
}
