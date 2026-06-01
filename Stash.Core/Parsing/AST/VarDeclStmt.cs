namespace Stash.Parsing.AST;

using Stash.Common;
using Stash.Lexing;

/// <summary>
/// A variable declaration: <c>let x = expr;</c>, <c>let x;</c>, or <c>readonly let x = expr;</c>.
/// </summary>
/// <remarks>
/// When the <see cref="Initializer"/> is <c>null</c>, the variable is initialized to <c>null</c>.
/// An optional <see cref="TypeHint"/> may be present for documentation purposes (type hints are not enforced at runtime).
/// When <see cref="ReadonlyKeyword"/> is non-<c>null</c>, the <c>readonly</c> modifier was present and the value
/// is to be deep-frozen after the initializer is evaluated (and on every subsequent rebind).
/// </remarks>
public class VarDeclStmt : Stmt
{
    /// <summary>Gets the identifier token of the variable being declared.</summary>
    public Token Name { get; }
    /// <summary>Gets the optional type hint. <c>null</c> if no type annotation was provided.</summary>
    public TypeExpression? TypeHint { get; }
    /// <summary>Gets the optional initializer expression. <c>null</c> for uninitialized declarations (<c>let x;</c>).</summary>
    public Expr? Initializer { get; }
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

    /// <summary>Initializes a new instance of <see cref="VarDeclStmt"/>.</summary>
    /// <param name="name">The identifier token of the variable being declared.</param>
    /// <param name="typeHint">The optional type hint, or <c>null</c>.</param>
    /// <param name="initializer">The optional initializer expression, or <c>null</c>.</param>
    /// <param name="span">The source location of this statement.</param>
    public VarDeclStmt(Token name, TypeExpression? typeHint, Expr? initializer, SourceSpan span) : base(span, StmtType.VarDecl)
    {
        Name = name;
        TypeHint = typeHint;
        Initializer = initializer;
    }

    /// <inheritdoc />
    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitVarDeclStmt(this);
}
