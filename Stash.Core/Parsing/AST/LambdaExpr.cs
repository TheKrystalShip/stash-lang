namespace Stash.Parsing.AST;

using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;

/// <summary>
/// Represents a lambda (arrow function) expression: <c>(params) =&gt; expr</c> or <c>(params) =&gt; { body }</c>.
/// </summary>
/// <remarks>
/// Lambdas capture the enclosing scope (closure semantics). A lambda has either an
/// <see cref="ExpressionBody"/> (concise form that returns the expression's value) or a
/// <see cref="BlockBody"/> (block form with explicit <c>return</c> statements). Exactly
/// one of these is non-null. Parameters may have optional type hints and default values.
/// <para>
/// Examples:
/// <list type="bullet">
///   <item><description><c>(x) =&gt; x * 2</c> — expression-body lambda</description></item>
///   <item><description><c>(x, y) =&gt; { return x + y; }</c> — block-body lambda</description></item>
///   <item><description><c>(x = 10) =&gt; x</c> — lambda with default parameter</description></item>
/// </list>
/// </para>
/// </remarks>
public class LambdaExpr : Expr
{
    /// <summary>
    /// Gets the list of parameter name tokens.
    /// </summary>
    public List<Token> Parameters { get; }

    /// <summary>
    /// Gets the list of optional type hint tokens for each parameter.
    /// Each entry is <c>null</c> if no type hint was provided for that parameter.
    /// </summary>
    public List<Token?> ParameterTypes { get; }

    /// <summary>
    /// Gets the list of optional default value expressions for each parameter.
    /// Each entry is <c>null</c> if no default was provided for that parameter.
    /// </summary>
    public List<Expr?> DefaultValues { get; }

    /// <summary>
    /// Gets the expression body for concise lambdas (<c>(x) =&gt; expr</c>).
    /// Null when the lambda uses a block body.
    /// </summary>
    public Expr? ExpressionBody { get; }

    /// <summary>
    /// Gets the block body for block-form lambdas (<c>(x) =&gt; { ... }</c>).
    /// Null when the lambda uses an expression body.
    /// </summary>
    public BlockStmt? BlockBody { get; }

    /// <summary>
    /// Gets whether this lambda was declared with the <c>async</c> keyword.
    /// </summary>
    public bool IsAsync { get; }

    /// <summary>
    /// Gets the <c>async</c> keyword token, or <c>null</c> if not async.
    /// </summary>
    public Token? AsyncKeyword { get; }

    /// <summary>
    /// Creates a new lambda expression node.
    /// </summary>
    /// <param name="parameters">The parameter name tokens.</param>
    /// <param name="parameterTypes">Optional type hint tokens (one per parameter, null if none).</param>
    /// <param name="defaultValues">Optional default value expressions (one per parameter, null if none).</param>
    /// <param name="expressionBody">The expression body (mutually exclusive with <paramref name="blockBody"/>).</param>
    /// <param name="blockBody">The block body (mutually exclusive with <paramref name="expressionBody"/>).</param>
    /// <param name="span">The source span covering the entire lambda expression.</param>
    /// <param name="isAsync">Whether this lambda was declared with the <c>async</c> keyword.</param>
    /// <param name="asyncKeyword">The <c>async</c> keyword token, or <c>null</c>.</param>
    public LambdaExpr(List<Token> parameters, List<Token?> parameterTypes, List<Expr?> defaultValues,
                      Expr? expressionBody, BlockStmt? blockBody, SourceSpan span, bool isAsync = false, Token? asyncKeyword = null) : base(span)
    {
        Parameters = parameters;
        ParameterTypes = parameterTypes;
        DefaultValues = defaultValues;
        ExpressionBody = expressionBody;
        BlockBody = blockBody;
        IsAsync = isAsync;
        AsyncKeyword = asyncKeyword;
    }

    /// <inheritdoc />
    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitLambdaExpr(this);
}
