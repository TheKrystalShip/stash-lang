namespace Stash.Parsing.AST;

using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;

/// <summary>
/// A lambda (arrow function) expression: <c>(params) =&gt; expr</c> or <c>(params) =&gt; { body }</c>
/// </summary>
public class LambdaExpr : Expr
{
    public List<Token> Parameters { get; }
    public List<Token?> ParameterTypes { get; }
    public Expr? ExpressionBody { get; }
    public BlockStmt? BlockBody { get; }

    public LambdaExpr(List<Token> parameters, List<Token?> parameterTypes,
                      Expr? expressionBody, BlockStmt? blockBody, SourceSpan span) : base(span)
    {
        Parameters = parameters;
        ParameterTypes = parameterTypes;
        ExpressionBody = expressionBody;
        BlockBody = blockBody;
    }

    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitLambdaExpr(this);
}
