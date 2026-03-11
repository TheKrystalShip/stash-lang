using Stash.Common;
using Stash.Lexing;

namespace Stash.Parsing.AST;

/// <summary>
/// Represents a variable reference (identifier) in Stash source code, such as <c>foo</c>.
/// </summary>
/// <remarks>
/// Stores the full <see cref="Token"/> rather than just the name string so that the source
/// location is available when reporting "undefined variable" errors at runtime.
/// </remarks>
/// <example>
/// The Stash expression <c>myVar</c> produces:
/// <code>new IdentifierExpr(nameToken, nameToken.Span)</code>
/// </example>
public class IdentifierExpr : Expr
{
    /// <summary>
    /// Gets the <see cref="Token"/> for this identifier, which contains the variable name
    /// (<see cref="Token.Lexeme"/>) and its source location (<see cref="Token.Span"/>).
    /// </summary>
    public Token Name { get; }

    /// <summary>
    /// Creates a new identifier expression node.
    /// </summary>
    /// <param name="name">The identifier token from the lexer.</param>
    /// <param name="span">The source span of the identifier.</param>
    public IdentifierExpr(Token name, SourceSpan span) : base(span)
    {
        Name = name;
    }

    /// <inheritdoc />
    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitIdentifierExpr(this);
}
