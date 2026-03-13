namespace Stash.Lsp.Analysis;

using System.Collections.Generic;
using Stash.Common;

public enum ScopeKind
{
    Global,
    Function,
    Block,
    Loop
}

public class Scope
{
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
    }
}
