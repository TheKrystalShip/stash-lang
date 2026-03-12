namespace Stash.Parsing.AST;

using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;

/// <summary>
/// A struct instantiation expression: <c>Name { field: value, ... }</c>
/// </summary>
public class StructInitExpr : Expr
{
    public Token Name { get; }
    public List<(Token Field, Expr Value)> FieldValues { get; }

    public StructInitExpr(Token name, List<(Token Field, Expr Value)> fieldValues, SourceSpan span) : base(span)
    {
        Name = name;
        FieldValues = fieldValues;
    }

    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitStructInitExpr(this);
}
