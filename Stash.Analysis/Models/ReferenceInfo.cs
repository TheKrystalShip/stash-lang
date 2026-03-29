namespace Stash.Analysis;

using Stash.Common;

/// <summary>
/// Classifies how an identifier is used at a particular site in the source,
/// enabling fine-grained filtering in find-references and highlight handlers.
/// </summary>
public enum ReferenceKind
{
    /// <summary>The identifier is read (e.g. used in an expression).</summary>
    Read,
    /// <summary>The identifier is written (assignment target or update expression).</summary>
    Write,
    /// <summary>The identifier is invoked as a function or method.</summary>
    Call,
    /// <summary>The identifier is used as a type name (e.g. in a struct initializer).</summary>
    TypeUse,
}

/// <summary>
/// Records a single use of an identifier in the source, linking it back to its resolved
/// <see cref="SymbolInfo"/> declaration (or <see langword="null"/> for unresolved names).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ReferenceInfo"/> instances are created by <see cref="SymbolCollector"/> during
/// AST traversal and stored in <see cref="ScopeTree.References"/>. They are consumed by:
/// </para>
/// <list type="bullet">
///   <item><description><see cref="ScopeTree.FindReferences"/> — to enumerate all uses of a symbol for the LSP find-references request.</description></item>
///   <item><description><see cref="ScopeTree.GetUnresolvedReferences"/> — to surface undefined-identifier warnings via <see cref="SemanticValidator"/>.</description></item>
///   <item><description>Document-highlight handlers — to highlight all read/write sites of the symbol under the cursor.</description></item>
/// </list>
/// </remarks>
public class ReferenceInfo
{
    /// <summary>Gets the raw identifier text as it appears in source.</summary>
    public string Name { get; }

    /// <summary>Gets the source span of the identifier token, used to produce LSP location ranges.</summary>
    public SourceSpan Span { get; }

    /// <summary>
    /// Gets or sets the <see cref="SymbolInfo"/> that this reference resolves to, or
    /// <see langword="null"/> if no declaration was found in scope at the usage site.
    /// </summary>
    public SymbolInfo? ResolvedSymbol { get; internal set; }

    /// <summary>Gets the usage kind (read, write, call, or type-use) at this reference site.</summary>
    public ReferenceKind Kind { get; }

    /// <summary>
    /// Initializes a new <see cref="ReferenceInfo"/> with the given name, span, kind, and
    /// optional resolved symbol.
    /// </summary>
    /// <param name="name">The identifier text.</param>
    /// <param name="span">The source span of the identifier token.</param>
    /// <param name="kind">The usage kind of this reference.</param>
    /// <param name="resolvedSymbol">The declaration this reference resolves to, or <see langword="null"/>.</param>
    public ReferenceInfo(string name, SourceSpan span, ReferenceKind kind, SymbolInfo? resolvedSymbol = null)
    {
        Name = name;
        Span = span;
        Kind = kind;
        ResolvedSymbol = resolvedSymbol;
    }
}
