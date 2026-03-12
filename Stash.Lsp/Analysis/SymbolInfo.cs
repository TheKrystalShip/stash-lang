namespace Stash.Lsp.Analysis;

using Stash.Common;

public enum SymbolKind
{
    Variable,
    Constant,
    Function,
    Parameter,
    Struct,
    Enum,
    EnumMember,
    Field,
    LoopVariable
}

public class SymbolInfo
{
    public string Name { get; }
    public SymbolKind Kind { get; }
    public SourceSpan Span { get; }
    public SourceSpan? FullSpan { get; }
    public string? Detail { get; }

    public SymbolInfo(string name, SymbolKind kind, SourceSpan span, SourceSpan? fullSpan = null, string? detail = null)
    {
        Name = name;
        Kind = kind;
        Span = span;
        FullSpan = fullSpan;
        Detail = detail;
    }
}
