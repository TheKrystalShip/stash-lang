namespace Stash.Parsing.AST;

using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;
using Stash.Runtime;

/// <summary>
/// A single catch clause in a try/catch statement.
/// </summary>
/// <remarks>
/// TypeTokens is empty for an untyped catch-all (<c>catch (e)</c>) or <c>catch (Error e)</c>.
/// TypeTokens contains one or more type name tokens for typed catch (<c>catch (TypeError e)</c>)
/// and union catch (<c>catch (TypeError | ValueError e)</c>).
/// </remarks>
public sealed class CatchClause
{
    /// <summary>Gets the <c>catch</c> keyword token.</summary>
    public Token Keyword { get; }

    /// <summary>
    /// Gets the list of type name tokens for this clause.
    /// Empty for untyped catch-all. One or more for typed/union catch.
    /// </summary>
    public IReadOnlyList<Token> TypeTokens { get; }

    /// <summary>Gets the catch variable token (the bound error identifier).</summary>
    public Token Variable { get; }

    /// <summary>Gets the body of this catch clause.</summary>
    public BlockStmt Body { get; }

    /// <summary>Gets the source span of this catch clause.</summary>
    public SourceSpan Span { get; }

    /// <summary>Initializes a new <see cref="CatchClause"/>.</summary>
    public CatchClause(Token keyword, IReadOnlyList<Token> typeTokens, Token variable, BlockStmt body, SourceSpan span)
    {
        Keyword = keyword;
        TypeTokens = typeTokens;
        Variable = variable;
        Body = body;
        Span = span;
    }

    /// <summary>
    /// Gets whether this clause is a catch-all (matches any error type).
    /// True when TypeTokens is empty or contains only the reserved <c>Error</c> identifier.
    /// </summary>
    public bool IsCatchAll => TypeTokens.Count == 0 || (TypeTokens.Count == 1 && ErrorTypeRegistry.IsBaseType(TypeTokens[0].Lexeme));
}
