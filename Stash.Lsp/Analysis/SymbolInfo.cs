namespace Stash.Lsp.Analysis;

using System;
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
    LoopVariable,
    Namespace
}

public class SymbolInfo
{
    public string Name { get; }
    public SymbolKind Kind { get; }
    public SourceSpan Span { get; }
    public SourceSpan? FullSpan { get; }
    public string? Detail { get; }
    public string? ParentName { get; }
    public string? TypeHint { get; }
    public Uri? SourceUri { get; }
    public string[]? ParameterNames { get; }

    public SymbolInfo(string name, SymbolKind kind, SourceSpan span, SourceSpan? fullSpan = null, string? detail = null, string? parentName = null, string? typeHint = null, Uri? sourceUri = null, string[]? parameterNames = null)
    {
        Name = name;
        Kind = kind;
        Span = span;
        FullSpan = fullSpan;
        Detail = detail;
        ParentName = parentName;
        TypeHint = typeHint;
        SourceUri = sourceUri;
        ParameterNames = parameterNames;
    }
}
