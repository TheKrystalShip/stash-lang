namespace Stash.Parsing.AST;

using Stash.Common;
using Stash.Lexing;

/// <summary>
/// A variable declaration: <c>let x = expr;</c> or <c>let x;</c> (initializer is null → value is null).
/// </summary>
/// <remarks>
/// When the <see cref="Initializer"/> is <c>null</c>, the variable is initialized to <c>null</c>.
/// An optional <see cref="TypeHint"/> may be present for documentation purposes (type hints are not enforced at runtime).
/// </remarks>
public class VarDeclStmt : Stmt
{
    /// <summary>Gets the identifier token of the variable being declared.</summary>
    public Token Name { get; }
    /// <summary>Gets the optional type hint. <c>null</c> if no type annotation was provided.</summary>
    public TypeHint? TypeHint { get; }
    /// <summary>Gets the optional initializer expression. <c>null</c> for uninitialized declarations (<c>let x;</c>).</summary>
    public Expr? Initializer { get; }

    /// <summary>Initializes a new instance of <see cref="VarDeclStmt"/>.</summary>
    /// <param name="name">The identifier token of the variable being declared.</param>
    /// <param name="typeHint">The optional type hint, or <c>null</c>.</param>
    /// <param name="initializer">The optional initializer expression, or <c>null</c>.</param>
    /// <param name="span">The source location of this statement.</param>
    public VarDeclStmt(Token name, TypeHint? typeHint, Expr? initializer, SourceSpan span) : base(span, StmtType.VarDecl)
    {
        Name = name;
        TypeHint = typeHint;
        Initializer = initializer;
    }

    /// <inheritdoc />
    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitVarDeclStmt(this);
}
