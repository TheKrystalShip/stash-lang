namespace Stash.Parsing.AST;

using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;

/// <summary>
/// Represents a function call expression: <c>callee(arg1, arg2, ...)</c>.
/// </summary>
/// <remarks>
/// The <see cref="Callee"/> can be any expression that evaluates to a callable value
/// (a function, lambda, built-in, or bound method). The <see cref="Paren"/> token is
/// retained for error reporting — it marks the location of the closing parenthesis,
/// which is used when reporting arity mismatches.
/// </remarks>
public class CallExpr : Expr
{
    /// <summary>
    /// Gets the expression that evaluates to the function being called.
    /// This may be an identifier, a dot expression, or any expression yielding a callable.
    /// </summary>
    public Expr Callee { get; }

    /// <summary>
    /// Gets the closing parenthesis token, used for error location reporting (e.g. arity mismatches).
    /// </summary>
    public Token Paren { get; }

    /// <summary>
    /// Gets the list of argument expressions passed to the function.
    /// </summary>
    public List<Expr> Arguments { get; }

    /// <summary>
    /// Gets whether this call originated from optional chaining (<c>x?.method()</c>).
    /// When <c>true</c> and the callee evaluates to <c>null</c>, the call returns <c>null</c>.
    /// </summary>
    public bool IsOptional { get; }

    /// <summary>
    /// Creates a new function call expression node.
    /// </summary>
    /// <param name="callee">The expression that evaluates to a callable.</param>
    /// <param name="paren">The closing parenthesis token (for error reporting).</param>
    /// <param name="arguments">The list of argument expressions.</param>
    /// <param name="span">The source span covering the entire call expression.</param>
    /// <param name="isOptional">Whether this call is part of an optional chain (<c>?.</c>).</param>
    public CallExpr(Expr callee, Token paren, List<Expr> arguments, SourceSpan span, bool isOptional = false) : base(span, ExprType.Call)
    {
        Callee = callee;
        Paren = paren;
        Arguments = arguments;
        IsOptional = isOptional;
    }

    /// <inheritdoc />
    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitCallExpr(this);
}
