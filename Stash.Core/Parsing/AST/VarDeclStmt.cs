namespace Stash.Parsing.AST;

using Stash.Common;
using Stash.Lexing;

/// <summary>
/// A variable declaration: <c>let x = expr;</c> or <c>let x;</c> (initializer is null → value is null).
/// </summary>
public class VarDeclStmt : Stmt
{
    public Token Name { get; }
    public Token? TypeHint { get; }
    public Expr? Initializer { get; }

    public VarDeclStmt(Token name, Token? typeHint, Expr? initializer, SourceSpan span) : base(span)
    {
        Name = name;
        TypeHint = typeHint;
        Initializer = initializer;
    }

    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitVarDeclStmt(this);
}
