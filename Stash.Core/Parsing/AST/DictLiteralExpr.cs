namespace Stash.Parsing.AST;

using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;

/// <summary>
/// Represents a dictionary literal expression: <c>{ key: value, key2: value2 }</c>.
/// </summary>
/// <remarks>
/// Keys are identifier tokens resolved as string dictionary keys at runtime.
/// An empty dict literal <c>{}</c> produces a node with an empty <see cref="Entries"/> list.
/// At runtime, this evaluates to a <see cref="Stash.Interpreting.Types.StashDictionary"/>.
/// </remarks>
public class DictLiteralExpr : Expr
{
    /// <summary>
    /// Gets the list of key-value entries in the dictionary literal.
    /// Each key is an identifier token whose lexeme becomes the string key.
    /// When <c>Key</c> is <c>null</c>, the entry is a spread entry and <c>Value</c> is a <see cref="SpreadExpr"/>.
    /// </summary>
    public List<(Token? Key, Expr Value)> Entries { get; }

    /// <summary>
    /// Creates a new dictionary literal expression node.
    /// </summary>
    /// <param name="entries">The key-value entries (may be empty for <c>{}</c>). A null Key indicates a spread entry.</param>
    /// <param name="span">The source span covering the entire dict literal.</param>
    public DictLiteralExpr(List<(Token? Key, Expr Value)> entries, SourceSpan span) : base(span)
    {
        Entries = entries;
    }

    /// <inheritdoc />
    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitDictLiteralExpr(this);
}
