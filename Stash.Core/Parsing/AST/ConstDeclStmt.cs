namespace Stash.Parsing.AST;

using Stash.Common;
using Stash.Lexing;

/// <summary>
/// A constant declaration: <c>const X = expr;</c> or <c>readonly const X = expr;</c>.
/// </summary>
/// <remarks>
/// Unlike <see cref="VarDeclStmt"/>, a constant always requires an initializer and cannot be reassigned
/// after declaration. Attempts to reassign a constant produce a runtime error.
/// When <see cref="ReadonlyKeyword"/> is non-<c>null</c>, the <c>readonly</c> modifier was present and
/// the initializer value is to be deep-frozen at declaration time.
/// </remarks>
public class ConstDeclStmt : Stmt
{
    /// <summary>Gets the identifier token of the constant being declared.</summary>
    public Token Name { get; }
    /// <summary>Gets the optional type hint. <c>null</c> if no type annotation was provided.</summary>
    public TypeExpression? TypeHint { get; }
    /// <summary>Gets the initializer expression (always required for constants).</summary>
    public Expr Initializer { get; }
    /// <summary>
    /// Gets the <c>readonly</c> modifier token when this declaration carries the modifier, or <c>null</c> if absent.
    /// Use <see cref="IsReadonly"/> as a convenient boolean test.
    /// </summary>
    public Token? ReadonlyKeyword { get; init; }
    /// <summary>
    /// Returns <see langword="true"/> when this declaration carries the <c>readonly</c> modifier.
    /// Derived from <see cref="ReadonlyKeyword"/>.
    /// </summary>
    public bool IsReadonly => ReadonlyKeyword is not null;

    /// <summary>Initializes a new instance of <see cref="ConstDeclStmt"/>.</summary>
    /// <param name="name">The identifier token of the constant being declared.</param>
    /// <param name="typeHint">The optional type hint, or <c>null</c>.</param>
    /// <param name="initializer">The initializer expression (required).</param>
    /// <param name="span">The source location of this statement.</param>
    public ConstDeclStmt(Token name, TypeExpression? typeHint, Expr initializer, SourceSpan span) : base(span, StmtType.ConstDecl)
    {
        Name = name;
        TypeHint = typeHint;
        Initializer = initializer;
    }

    /// <inheritdoc />
    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitConstDeclStmt(this);
}
