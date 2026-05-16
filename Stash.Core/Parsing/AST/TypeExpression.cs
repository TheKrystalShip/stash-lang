namespace Stash.Parsing.AST;

using System.Collections.Generic;
using System.Linq;
using Stash.Common;
using Stash.Lexing;

/// <summary>
/// Canonical AST representation of a type as written in Stash source code.
/// Every grammar position that accepts a type name (annotations on
/// <c>let</c>/<c>const</c>/<c>fn</c>/<c>for-in</c>/struct/interface/lambda/retry,
/// the <c>is</c> operator, the <c>catch</c> clause, the <c>extend</c> target,
/// and the head of a struct-init expression) parses into one of these.
/// </summary>
/// <remarks>
/// Under the erasure model (see the Type Hints — Canonical TypeExpression
/// Refactor spec), <see cref="TypeExpression"/> is metadata for tooling and
/// static analysis. The compiler does not emit runtime type checks from it.
/// </remarks>
public abstract record TypeExpression(SourceSpan Span)
{
    /// <summary>
    /// The canonical source-form string for this type, stable across the
    /// compiler/analyzer/LSP. For example: <c>int</c>, <c>int[]</c>,
    /// <c>diff.Edit</c>, <c>diff.Edit[]</c>.
    /// </summary>
    public abstract string ToCanonicalString();

    /// <summary>
    /// The leaf identifier token of this type — the final segment of a
    /// qualified path, the bare name of a simple type, or the leaf of the
    /// element type for an array type. Used by tooling that needs a single
    /// representative <see cref="Token"/> (e.g. semantic highlighting).
    /// </summary>
    public abstract Token LeafToken { get; }
}

/// <summary>A bare-identifier type such as <c>int</c>, <c>string</c>, or <c>Point</c>.</summary>
public sealed record SimpleType(Token Name, SourceSpan Span) : TypeExpression(Span)
{
    /// <inheritdoc />
    public override string ToCanonicalString() => Name.Lexeme;

    /// <inheritdoc />
    public override Token LeafToken => Name;
}

/// <summary>A namespace-qualified type such as <c>diff.Edit</c> or <c>a.b.C</c>.</summary>
/// <param name="Segments">The dot-separated identifier tokens, in order. Always has length &gt;= 2.</param>
public sealed record QualifiedType(IReadOnlyList<Token> Segments, SourceSpan Span)
    : TypeExpression(Span)
{
    /// <inheritdoc />
    public override string ToCanonicalString() => string.Join('.', Segments.Select(s => s.Lexeme));

    /// <inheritdoc />
    public override Token LeafToken => Segments[^1];
}

/// <summary>An array type <c>T[]</c> over any inner <see cref="TypeExpression"/>.</summary>
public sealed record ArrayType(TypeExpression Element, SourceSpan Span) : TypeExpression(Span)
{
    /// <inheritdoc />
    public override string ToCanonicalString() => Element.ToCanonicalString() + "[]";

    /// <inheritdoc />
    public override Token LeafToken => Element.LeafToken;
}
