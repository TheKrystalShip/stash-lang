namespace Stash.Parsing.AST;

using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;

/// <summary>
/// Discriminates the three forms of a dict-literal key.
/// </summary>
public enum DictKeyKind
{
    /// <summary>A plain identifier key (<c>name: v</c>) or a string-literal key (<c>"a-b": v</c>).</summary>
    Constant,
    /// <summary>A computed key expression (<c>[expr]: v</c>).</summary>
    Computed,
    /// <summary>A spread entry — no key; <see cref="DictEntry.Value"/> is a <see cref="SpreadExpr"/>.</summary>
    Spread,
}

/// <summary>
/// Represents one entry in a dict literal.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description><see cref="DictKeyKind.Constant"/> — <see cref="KeyToken"/> is the identifier or string-literal token;
/// use <see cref="KeyString"/> to get the resolved string value (works for both token kinds).</description></item>
/// <item><description><see cref="DictKeyKind.Computed"/> — <see cref="KeyExpr"/> holds the key expression;
/// <see cref="KeyToken"/> is <c>null</c>.</description></item>
/// <item><description><see cref="DictKeyKind.Spread"/> — <see cref="KeyToken"/> and <see cref="KeyExpr"/> are both <c>null</c>;
/// <see cref="Value"/> is a <see cref="SpreadExpr"/>.</description></item>
/// </list>
/// </remarks>
public sealed class DictEntry
{
    /// <summary>The kind of key for this entry.</summary>
    public DictKeyKind Kind { get; }

    /// <summary>
    /// The key token for <see cref="DictKeyKind.Constant"/> entries (identifier or string literal).
    /// <c>null</c> for computed and spread entries.
    /// </summary>
    public Token? KeyToken { get; }

    /// <summary>
    /// The key expression for <see cref="DictKeyKind.Computed"/> entries.
    /// <c>null</c> for constant and spread entries.
    /// </summary>
    public Expr? KeyExpr { get; }

    /// <summary>The value expression for this entry.</summary>
    public Expr Value { get; }

    /// <summary>
    /// Returns the string key value for a <see cref="DictKeyKind.Constant"/> entry:
    /// the unescaped literal value for <see cref="TokenType.StringLiteral"/> tokens,
    /// or the lexeme for identifier tokens.
    /// </summary>
    /// <exception cref="System.InvalidOperationException">Thrown when called on a non-constant entry.</exception>
    public string KeyString =>
        Kind == DictKeyKind.Constant
            ? (KeyToken!.Type == TokenType.StringLiteral
                ? (string)KeyToken.Literal!
                : KeyToken.Lexeme)
            : throw new System.InvalidOperationException("KeyString is only valid for Constant entries.");

    private DictEntry(DictKeyKind kind, Token? keyToken, Expr? keyExpr, Expr value)
    {
        Kind = kind;
        KeyToken = keyToken;
        KeyExpr = keyExpr;
        Value = value;
    }

    /// <summary>Creates a constant-key entry (identifier or string-literal key).</summary>
    public static DictEntry Constant(Token keyToken, Expr value) =>
        new(DictKeyKind.Constant, keyToken, null, value);

    /// <summary>Creates a computed-key entry (<c>[expr]: v</c>).</summary>
    public static DictEntry Computed(Expr keyExpr, Expr value) =>
        new(DictKeyKind.Computed, null, keyExpr, value);

    /// <summary>Creates a spread entry (<c>...expr</c>).</summary>
    public static DictEntry Spread(Expr spreadExpr) =>
        new(DictKeyKind.Spread, null, null, spreadExpr);
}

/// <summary>
/// Represents a dictionary literal expression: <c>{ key: value, key2: value2 }</c>.
/// </summary>
/// <remarks>
/// Three key forms are supported (per Appendix A grammar):
/// <list type="bullet">
/// <item><description>Identifier key: <c>{ name: 1 }</c></description></item>
/// <item><description>String-literal key: <c>{ "a-b": 1, "with space": 2 }</c></description></item>
/// <item><description>Computed key: <c>{ [expr]: 1 }</c></description></item>
/// </list>
/// An empty dict literal <c>{}</c> produces a node with an empty <see cref="Entries"/> list.
/// At runtime, this evaluates to a <see cref="Stash.Runtime.Types.StashDictionary"/>.
/// </remarks>
public class DictLiteralExpr : Expr
{
    /// <summary>
    /// Gets the list of entries in the dictionary literal.
    /// </summary>
    public List<DictEntry> Entries { get; }

    /// <summary>
    /// Creates a new dictionary literal expression node.
    /// </summary>
    /// <param name="entries">The entries (may be empty for <c>{}</c>).</param>
    /// <param name="span">The source span covering the entire dict literal.</param>
    public DictLiteralExpr(List<DictEntry> entries, SourceSpan span) : base(span, ExprType.DictLiteral)
    {
        Entries = entries;
    }

    /// <inheritdoc />
    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitDictLiteralExpr(this);
}
