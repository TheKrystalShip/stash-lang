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
    Method,
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
    public string? TypeHint { get; set; }
    public Uri? SourceUri { get; }
    public string[]? ParameterNames { get; }
    public int? RequiredParameterCount { get; }
    public string?[]? ParameterTypes { get; }
    public bool IsExplicitTypeHint { get; }
    public string? Documentation { get; set; }

    public SymbolInfo(string name, SymbolKind kind, SourceSpan span, SourceSpan? fullSpan = null, string? detail = null, string? parentName = null, string? typeHint = null, Uri? sourceUri = null, string[]? parameterNames = null, int? requiredParameterCount = null, string?[]? parameterTypes = null, bool isExplicitTypeHint = false)
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
        RequiredParameterCount = requiredParameterCount;
        ParameterTypes = parameterTypes;
        IsExplicitTypeHint = isExplicitTypeHint;
    }
}
