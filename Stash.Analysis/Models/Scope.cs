namespace Stash.Analysis;

using System;
using System.Collections.Generic;
using Stash.Common;

public class Scope
{
    private readonly Dictionary<string, List<SymbolInfo>> _symbolsByName = new();

    public ScopeKind Kind { get; }
    public Scope? Parent { get; }
    public SourceSpan Span { get; }
    public List<SymbolInfo> Symbols { get; } = new();
    public List<Scope> Children { get; } = new();

    public Scope(ScopeKind kind, Scope? parent, SourceSpan span)
    {
        Kind = kind;
        Parent = parent;
        Span = span;
        parent?.Children.Add(this);
    }

    public void AddSymbol(SymbolInfo symbol)
    {
        Symbols.Add(symbol);
        if (!_symbolsByName.TryGetValue(symbol.Name, out var list))
        {
            list = new List<SymbolInfo>();
            _symbolsByName[symbol.Name] = list;
        }
        list.Add(symbol);
    }

    public void ReplaceSymbol(int index, SymbolInfo symbol)
    {
        var old = Symbols[index];
        Symbols[index] = symbol;

        if (_symbolsByName.TryGetValue(old.Name, out var oldList))
        {
            var idx = oldList.IndexOf(old);
            if (idx >= 0)
            {
                oldList.RemoveAt(idx);
                if (oldList.Count == 0)
                {
                    _symbolsByName.Remove(old.Name);
                }
            }
        }

        if (!_symbolsByName.TryGetValue(symbol.Name, out var newList))
        {
            newList = new List<SymbolInfo>();
            _symbolsByName[symbol.Name] = newList;
        }
        newList.Add(symbol);
    }

    public IReadOnlyList<SymbolInfo> GetSymbolsByName(string name)
    {
        return _symbolsByName.TryGetValue(name, out var list) ? list : Array.Empty<SymbolInfo>();
    }

    public Dictionary<string, string>? TypeNarrowings { get; private set; }

    public void AddTypeNarrowing(string name, string typeHint)
    {
        TypeNarrowings ??= new();
        TypeNarrowings[name] = typeHint;
    }

    public bool RemoveSymbol(string name)
    {
        if (_symbolsByName.TryGetValue(name, out var list) && list.Count > 0)
        {
            var sym = list[^1];
            list.RemoveAt(list.Count - 1);
            if (list.Count == 0) _symbolsByName.Remove(name);
            Symbols.Remove(sym);
            return true;
        }
        return false;
    }
}
