namespace Stash.Parsing.AST;

using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;
using Stash.Runtime;

/// <summary>
/// A single catch clause in a try/catch statement.
/// </summary>
/// <remarks>
/// <see cref="CatchTypes"/> is empty for an untyped catch-all (<c>catch (e)</c>) or <c>catch (Error e)</c>.
/// It contains one or more <see cref="TypeExpression"/>s for typed catch (<c>catch (TypeError e)</c>)
/// and union catch (<c>catch (TypeError | ValueError e)</c>). Each entry may be a simple, qualified,
/// or array type expression.
/// </remarks>
public sealed class CatchClause
{
    /// <summary>Gets the <c>catch</c> keyword token.</summary>
    public Token Keyword { get; }

    /// <summary>
    /// Gets the list of type expressions matched by this clause.
    /// Empty for untyped catch-all. One or more for typed/union catch.
    /// </summary>
    public IReadOnlyList<TypeExpression> CatchTypes { get; }

    /// <summary>Gets the catch variable token (the bound error identifier).</summary>
    public Token Variable { get; }

    /// <summary>Gets the body of this catch clause.</summary>
    public BlockStmt Body { get; }

    /// <summary>Gets the source span of this catch clause.</summary>
    public SourceSpan Span { get; }

    /// <summary>Initializes a new <see cref="CatchClause"/>.</summary>
    public CatchClause(Token keyword, IReadOnlyList<TypeExpression> catchTypes, Token variable, BlockStmt body, SourceSpan span)
    {
        Keyword = keyword;
        CatchTypes = catchTypes;
        Variable = variable;
        Body = body;
        Span = span;
    }

    /// <summary>
    /// Gets whether this clause is a catch-all (matches any error type).
    /// True when <see cref="CatchTypes"/> is empty or contains only the reserved <c>Error</c> identifier
    /// (or one of its registered base-type aliases).
    /// </summary>
    public bool IsCatchAll => CatchTypes.Count == 0
        || (CatchTypes.Count == 1 && ErrorTypeRegistry.IsBaseType(CatchTypes[0].ToCanonicalString()));
}
