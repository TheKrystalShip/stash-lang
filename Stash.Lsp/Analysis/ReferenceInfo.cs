namespace Stash.Lsp.Analysis;

using Stash.Common;

public enum ReferenceKind
{
    Read,
    Write,
    Call,
    TypeUse,
}

public class ReferenceInfo
{
    public string Name { get; }
    public SourceSpan Span { get; }
    public SymbolInfo? ResolvedSymbol { get; }
    public ReferenceKind Kind { get; }

    public ReferenceInfo(string name, SourceSpan span, ReferenceKind kind, SymbolInfo? resolvedSymbol = null)
    {
        Name = name;
        Span = span;
        Kind = kind;
        ResolvedSymbol = resolvedSymbol;
    }
}
