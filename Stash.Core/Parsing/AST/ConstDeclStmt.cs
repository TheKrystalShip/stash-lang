namespace Stash.Parsing.AST;

using Stash.Common;
using Stash.Lexing;

/// <summary>
/// A constant declaration: <c>const X = expr;</c>
/// </summary>
public class ConstDeclStmt : Stmt
{
    public Token Name { get; }
    public Token? TypeHint { get; }
    public Expr Initializer { get; }

    public ConstDeclStmt(Token name, Token? typeHint, Expr initializer, SourceSpan span) : base(span)
    {
        Name = name;
        TypeHint = typeHint;
        Initializer = initializer;
    }

    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitConstDeclStmt(this);
}
