namespace Stash.Parsing.AST;

using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;

/// <summary>
/// A function call expression: <c>callee(arg1, arg2, ...)</c>
/// </summary>
public class CallExpr : Expr
{
    public Expr Callee { get; }
    public Token Paren { get; }
    public List<Expr> Arguments { get; }

    public CallExpr(Expr callee, Token paren, List<Expr> arguments, SourceSpan span) : base(span)
    {
        Callee = callee;
        Paren = paren;
        Arguments = arguments;
    }

    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitCallExpr(this);
}
