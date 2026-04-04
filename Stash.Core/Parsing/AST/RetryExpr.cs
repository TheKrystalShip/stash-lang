namespace Stash.Parsing.AST;

using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;

/// <summary>
/// Represents the <c>onRetry</c> clause of a <see cref="RetryExpr"/>.
/// Can be either an inline block with parameters or a function reference.
/// </summary>
/// <param name="OnRetryKeyword">The <c>onRetry</c> keyword token.</param>
/// <param name="IsReference"><see langword="true"/> when a named function reference is used; <see langword="false"/> for an inline block.</param>
/// <param name="ParamAttempt">The parameter token for the attempt number (inline block only).</param>
/// <param name="ParamAttemptTypeHint">The optional type hint token for the attempt parameter (inline block only).</param>
/// <param name="ParamError">The parameter token for the error value (inline block only).</param>
/// <param name="ParamErrorTypeHint">The optional type hint token for the error parameter (inline block only).</param>
/// <param name="Body">The inline block body (<see cref="IsReference"/> is <see langword="false"/>).</param>
/// <param name="Reference">The function reference expression (<see cref="IsReference"/> is <see langword="true"/>).</param>
/// <param name="Span">The source span covering the entire onRetry clause.</param>
public record OnRetryNode(
    Token OnRetryKeyword,
    bool IsReference,
    Token? ParamAttempt,
    Token? ParamAttemptTypeHint,
    Token? ParamError,
    Token? ParamErrorTypeHint,
    BlockStmt? Body,
    Expr? Reference,
    SourceSpan Span);

/// <summary>
/// Represents a retry expression: <c>retry (maxAttempts, options...) onRetry (...) { ... } until predicate { body }</c>.
/// </summary>
/// <remarks>
/// The retry expression evaluates its body up to <see cref="MaxAttempts"/> times. If the body
/// completes without throwing, its result value is returned. If all attempts are exhausted,
/// the last failure is propagated.
/// <para>
/// Options can be specified as inline named fields (<see cref="NamedOptions"/>) or as a
/// pre-built <c>RetryOptions</c> struct instance (<see cref="OptionsExpr"/>). At most one
/// of these is non-null.
/// </para>
/// </remarks>
public class RetryExpr : Expr
{
    /// <summary>Gets the <c>retry</c> keyword token.</summary>
    public Token RetryKeyword { get; }

    /// <summary>Gets the expression that evaluates to the maximum number of attempts.</summary>
    public Expr MaxAttempts { get; }

    /// <summary>
    /// Gets the inline named options (e.g. <c>delay: 1s, backoff: Backoff.Exponential</c>).
    /// Null when a <see cref="OptionsExpr"/> struct instance is used instead.
    /// </summary>
    public List<(Token Name, Expr Value)>? NamedOptions { get; }

    /// <summary>
    /// Gets the expression evaluating to a <c>RetryOptions</c> struct instance.
    /// Null when inline <see cref="NamedOptions"/> are used instead.
    /// </summary>
    public Expr? OptionsExpr { get; }

    /// <summary>Gets the optional <c>until</c> keyword token.</summary>
    public Token? UntilKeyword { get; }

    /// <summary>Gets the optional <c>until</c> predicate expression (lambda or function reference).</summary>
    public Expr? UntilClause { get; }

    /// <summary>Gets the optional <c>onRetry</c> hook clause.</summary>
    public OnRetryNode? OnRetryClause { get; }

    /// <summary>Gets the retry body block.</summary>
    public BlockStmt Body { get; }

    /// <summary>Initializes a new instance of <see cref="RetryExpr"/>.</summary>
    public RetryExpr(
        Token retryKeyword,
        Expr maxAttempts,
        List<(Token Name, Expr Value)>? namedOptions,
        Expr? optionsExpr,
        Token? untilKeyword,
        Expr? untilClause,
        OnRetryNode? onRetryClause,
        BlockStmt body,
        SourceSpan span) : base(span, ExprType.Retry)
    {
        RetryKeyword = retryKeyword;
        MaxAttempts = maxAttempts;
        NamedOptions = namedOptions;
        OptionsExpr = optionsExpr;
        UntilKeyword = untilKeyword;
        UntilClause = untilClause;
        OnRetryClause = onRetryClause;
        Body = body;
    }

    /// <inheritdoc />
    public override T Accept<T>(IExprVisitor<T> visitor) => visitor.VisitRetryExpr(this);
}
