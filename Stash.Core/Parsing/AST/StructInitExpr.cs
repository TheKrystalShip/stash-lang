namespace Stash.Parsing.AST;

using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;

/// <summary>
/// A struct instantiation expression: <c>Name { field: value, ... }</c>
/// or <c>ns.Name { field: value, ... }</c>
/// </summary>
public class StructInitExpr : Expr
{
    /// <summary>
    /// The struct name token (the final identifier, e.g. "Server" in both "Server { ... }" and "utils.Server { ... }").
    /// </summary>
    public Token Name { get; }

    /// <summary>
    /// Optional expression that resolves to the struct definition.
    /// When non-null, this is used instead of looking up Name in the environment.
    /// For <c>ns.Server { ... }</c>, this would be the DotExpr <c>ns.Server</c>.
    /// </summary>
    public Expr? Target { get; }

    public List<(Token Field, Expr Value)> FieldValues { get; }

    public StructInitExpr(Token name, List<(Token Field, Expr Value)> fieldValues, SourceSpan span)
        : base(span, ExprType.StructInit)
    {
        Name = name;
        Target = null;
        FieldValues = fieldValues;
    }

    public StructInitExpr(Token name, Expr target, List<(Token Field, Expr Value)> fieldValues, SourceSpan span)
        : base(span, ExprType.StructInit)
    {
        Name = name;
        Target = target;
        FieldValues = fieldValues;
    }

    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitStructInitExpr(this);
}
