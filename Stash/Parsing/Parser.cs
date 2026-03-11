namespace Stash.Parsing;

using System;
using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;
using Stash.Parsing.AST;

/// <summary>
/// A recursive-descent parser that transforms a flat list of <see cref="Token"/>s (produced by
/// the <see cref="Lexer"/>) into an expression AST rooted at <see cref="Expr"/>.
/// </summary>
/// <remarks>
/// <para>
/// Each precedence level is encoded as its own method, with lower-precedence methods calling
/// higher-precedence ones. This naturally produces correct operator precedence without an
/// explicit precedence table. The full chain (lowest → highest) is:
/// </para>
/// <list type="number">
///   <item><description><see cref="Expression"/> — entry point, delegates to <see cref="Ternary"/></description></item>
///   <item><description><see cref="Ternary"/> — <c>? :</c> (right-associative)</description></item>
///   <item><description><see cref="Or"/> — <c>||</c></description></item>
///   <item><description><see cref="And"/> — <c>&amp;&amp;</c></description></item>
///   <item><description><see cref="Equality"/> — <c>== !=</c></description></item>
///   <item><description><see cref="Comparison"/> — <c>&lt; &gt; &lt;= &gt;=</c></description></item>
///   <item><description><see cref="Term"/> — <c>+ -</c></description></item>
///   <item><description><see cref="Factor"/> — <c>* / %</c></description></item>
///   <item><description><see cref="Unary"/> — prefix <c>! -</c></description></item>
///   <item><description><see cref="Primary"/> — literals, identifiers, grouping <c>( )</c></description></item>
/// </list>
/// <para>
/// Left-associativity for binary operators is achieved via <c>while</c> loops (e.g.
/// <c>1 + 2 + 3</c> → <c>(1 + 2) + 3</c>). The ternary operator is right-associative
/// because its branches call <see cref="Expression"/> (the lowest precedence), allowing
/// <c>a ? b : c ? d : e</c> → <c>a ? b : (c ? d : e)</c>.
/// </para>
/// <para>
/// Error recovery uses a private <see cref="ParseError"/> exception for control flow.
/// When the parser encounters an unexpected token it records a human-readable message in
/// <see cref="Errors"/> and throws <see cref="ParseError"/>, which is caught in
/// <see cref="Parse"/> to prevent cascading errors. A <c>null</c> literal is returned as
/// a safe fallback AST node.
/// </para>
/// </remarks>
public class Parser
{
    /// <summary>
    /// The complete list of tokens produced by the <see cref="Lexer"/>, including the
    /// trailing <see cref="TokenType.Eof"/> sentinel.
    /// </summary>
    private readonly List<Token> _tokens;

    /// <summary>
    /// Index of the next token to be consumed. Advances monotonically via <see cref="Advance"/>.
    /// </summary>
    private int _current;

    /// <summary>
    /// Gets the list of human-readable error messages accumulated during parsing.
    /// Each entry includes the file name, line, column, and a description of the problem.
    /// </summary>
    public List<string> Errors { get; } = new();

    /// <summary>
    /// Creates a new <see cref="Parser"/> for the given token stream.
    /// </summary>
    /// <param name="tokens">
    /// The list of tokens to parse. Must end with a <see cref="TokenType.Eof"/> token.
    /// </param>
    public Parser(List<Token> tokens)
    {
        _tokens = tokens;
        _current = 0;
    }

    /// <summary>
    /// Parses the token stream into a single expression AST.
    /// </summary>
    /// <returns>
    /// The root <see cref="Expr"/> of the parsed AST. On parse failure, returns a
    /// <see cref="LiteralExpr"/> with a <c>null</c> value as a safe fallback.
    /// </returns>
    /// <remarks>
    /// Any syntax errors encountered are recorded in <see cref="Errors"/>.
    /// The caller should check <see cref="Errors"/> after calling this method to determine
    /// whether parsing succeeded.
    /// </remarks>
    public Expr Parse()
    {
        try
        {
            return Expression();
        }
        catch (ParseError)
        {
            return new LiteralExpr(null, Peek().Span);
        }
    }

    // ── Precedence levels (lowest → highest) ──────────────────────

    /// <summary>
    /// Parses an expression at the lowest precedence level.
    /// Currently delegates directly to <see cref="Ternary"/>.
    /// </summary>
    /// <returns>The parsed expression.</returns>
    private Expr Expression()
    {
        return Ternary();
    }

    /// <summary>
    /// Parses a ternary conditional expression: <c>condition ? thenBranch : elseBranch</c>.
    /// </summary>
    /// <returns>
    /// A <see cref="TernaryExpr"/> if a <c>?</c> token follows the condition,
    /// otherwise the condition expression itself.
    /// </returns>
    /// <remarks>
    /// Right-associativity is achieved by calling <see cref="Expression"/> (the lowest
    /// precedence) for both the then-branch and else-branch, so nested ternaries like
    /// <c>a ? b : c ? d : e</c> parse as <c>a ? b : (c ? d : e)</c>.
    /// </remarks>
    private Expr Ternary()
    {
        Expr expr = Or();

        if (Match(TokenType.QuestionMark))
        {
            Expr thenBranch = Expression();
            Consume(TokenType.Colon, "Expected ':' after then-branch of ternary expression.");
            Expr elseBranch = Expression();
            SourceSpan span = MakeSpan(expr.Span, elseBranch.Span);
            return new TernaryExpr(expr, thenBranch, elseBranch, span);
        }

        return expr;
    }

    /// <summary>
    /// Parses a logical OR expression: <c>left || right</c>.
    /// </summary>
    /// <returns>
    /// A left-associative chain of <see cref="BinaryExpr"/> nodes for <c>||</c>,
    /// or the result of <see cref="And"/> if no <c>||</c> operator is present.
    /// </returns>
    private Expr Or()
    {
        Expr expr = And();

        while (Match(TokenType.PipePipe))
        {
            Token op = Previous();
            Expr right = And();
            expr = new BinaryExpr(expr, op, right, MakeSpan(expr.Span, right.Span));
        }

        return expr;
    }

    /// <summary>
    /// Parses a logical AND expression: <c>left &amp;&amp; right</c>.
    /// </summary>
    /// <returns>
    /// A left-associative chain of <see cref="BinaryExpr"/> nodes for <c>&amp;&amp;</c>,
    /// or the result of <see cref="Equality"/> if no <c>&amp;&amp;</c> operator is present.
    /// </returns>
    private Expr And()
    {
        Expr expr = Equality();

        while (Match(TokenType.AmpersandAmpersand))
        {
            Token op = Previous();
            Expr right = Equality();
            expr = new BinaryExpr(expr, op, right, MakeSpan(expr.Span, right.Span));
        }

        return expr;
    }

    /// <summary>
    /// Parses an equality expression: <c>left == right</c> or <c>left != right</c>.
    /// </summary>
    /// <returns>
    /// A left-associative chain of <see cref="BinaryExpr"/> nodes for <c>==</c> / <c>!=</c>,
    /// or the result of <see cref="Comparison"/> if no equality operator is present.
    /// </returns>
    private Expr Equality()
    {
        Expr expr = Comparison();

        while (Match(TokenType.EqualEqual, TokenType.BangEqual))
        {
            Token op = Previous();
            Expr right = Comparison();
            expr = new BinaryExpr(expr, op, right, MakeSpan(expr.Span, right.Span));
        }

        return expr;
    }

    /// <summary>
    /// Parses a comparison expression: <c>&lt;</c>, <c>&gt;</c>, <c>&lt;=</c>, or <c>&gt;=</c>.
    /// </summary>
    /// <returns>
    /// A left-associative chain of <see cref="BinaryExpr"/> nodes for comparison operators,
    /// or the result of <see cref="Term"/> if no comparison operator is present.
    /// </returns>
    private Expr Comparison()
    {
        Expr expr = Term();

        while (Match(TokenType.Less, TokenType.Greater, TokenType.LessEqual, TokenType.GreaterEqual))
        {
            Token op = Previous();
            Expr right = Term();
            expr = new BinaryExpr(expr, op, right, MakeSpan(expr.Span, right.Span));
        }

        return expr;
    }

    /// <summary>
    /// Parses an additive expression: <c>left + right</c> or <c>left - right</c>.
    /// </summary>
    /// <returns>
    /// A left-associative chain of <see cref="BinaryExpr"/> nodes for <c>+</c> / <c>-</c>,
    /// or the result of <see cref="Factor"/> if no additive operator is present.
    /// </returns>
    private Expr Term()
    {
        Expr expr = Factor();

        while (Match(TokenType.Plus, TokenType.Minus))
        {
            Token op = Previous();
            Expr right = Factor();
            expr = new BinaryExpr(expr, op, right, MakeSpan(expr.Span, right.Span));
        }

        return expr;
    }

    /// <summary>
    /// Parses a multiplicative expression: <c>*</c>, <c>/</c>, or <c>%</c>.
    /// </summary>
    /// <returns>
    /// A left-associative chain of <see cref="BinaryExpr"/> nodes for <c>* / %</c>,
    /// or the result of <see cref="Unary"/> if no multiplicative operator is present.
    /// </returns>
    private Expr Factor()
    {
        Expr expr = Unary();

        while (Match(TokenType.Star, TokenType.Slash, TokenType.Percent))
        {
            Token op = Previous();
            Expr right = Unary();
            expr = new BinaryExpr(expr, op, right, MakeSpan(expr.Span, right.Span));
        }

        return expr;
    }

    /// <summary>
    /// Parses a prefix unary expression: <c>!expr</c> or <c>-expr</c>.
    /// </summary>
    /// <returns>
    /// A <see cref="UnaryExpr"/> if a prefix operator is found; otherwise falls through
    /// to <see cref="Primary"/>.
    /// </returns>
    /// <remarks>
    /// Unary is right-recursive — calling itself allows chaining like <c>!!x</c> or <c>--x</c>.
    /// </remarks>
    private Expr Unary()
    {
        if (Match(TokenType.Bang, TokenType.Minus))
        {
            Token op = Previous();
            Expr right = Unary();
            return new UnaryExpr(op, right, MakeSpan(op.Span, right.Span));
        }

        return Primary();
    }

    /// <summary>
    /// Parses a primary (highest-precedence) expression: literals, identifiers, or
    /// parenthesized groupings.
    /// </summary>
    /// <returns>
    /// A <see cref="LiteralExpr"/>, <see cref="IdentifierExpr"/>, or <see cref="GroupingExpr"/>
    /// depending on the current token.
    /// </returns>
    /// <exception cref="ParseError">
    /// Thrown when the current token does not start a valid expression, triggering
    /// error recovery in <see cref="Parse"/>.
    /// </exception>
    private Expr Primary()
    {
        if (Match(TokenType.IntegerLiteral, TokenType.FloatLiteral, TokenType.StringLiteral))
        {
            Token token = Previous();
            return new LiteralExpr(token.Literal, token.Span);
        }

        if (Match(TokenType.True))
        {
            return new LiteralExpr(true, Previous().Span);
        }

        if (Match(TokenType.False))
        {
            return new LiteralExpr(false, Previous().Span);
        }

        if (Match(TokenType.Null))
        {
            return new LiteralExpr(null, Previous().Span);
        }

        if (Match(TokenType.Identifier))
        {
            Token name = Previous();
            return new IdentifierExpr(name, name.Span);
        }

        if (Match(TokenType.LeftParen))
        {
            Token open = Previous();
            Expr expr = Expression();
            Token close = Consume(TokenType.RightParen, "Expected ')' after expression.");
            return new GroupingExpr(expr, MakeSpan(open.Span, close.Span));
        }

        throw Error(Peek(), "Expected expression.");
    }

    // ── Helper methods ────────────────────────────────────────────

    /// <summary>
    /// Checks whether the current token matches any of the given types. If a match is found,
    /// the token is consumed (advanced past).
    /// </summary>
    /// <param name="types">One or more <see cref="TokenType"/> values to check against.</param>
    /// <returns><c>true</c> if the current token matched and was consumed; <c>false</c> otherwise.</returns>
    private bool Match(params TokenType[] types)
    {
        foreach (TokenType type in types)
        {
            if (Check(type))
            {
                Advance();
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Tests whether the current token is of the given type without consuming it.
    /// </summary>
    /// <param name="type">The token type to check for.</param>
    /// <returns><c>true</c> if the current token has the specified type and we are not at EOF.</returns>
    private bool Check(TokenType type)
    {
        if (IsAtEnd)
        {
            return false;
        }

        return Peek().Type == type;
    }

    /// <summary>
    /// Consumes the current token and returns the previously current token.
    /// </summary>
    /// <returns>The token that was current before advancing (i.e. <see cref="Previous"/>).</returns>
    private Token Advance()
    {
        if (!IsAtEnd)
        {
            _current++;
        }

        return Previous();
    }

    /// <summary>
    /// Returns the current token without consuming it.
    /// </summary>
    /// <returns>The token at position <see cref="_current"/>.</returns>
    private Token Peek()
    {
        return _tokens[_current];
    }

    /// <summary>
    /// Returns the most recently consumed token (one position before <see cref="_current"/>).
    /// </summary>
    /// <returns>The token at position <c>_current - 1</c>.</returns>
    private Token Previous()
    {
        return _tokens[_current - 1];
    }

    /// <summary>
    /// Gets a value indicating whether the parser has reached the end-of-file sentinel token.
    /// </summary>
    private bool IsAtEnd => Peek().Type == TokenType.Eof;

    /// <summary>
    /// Consumes the current token if it matches <paramref name="type"/>; otherwise reports
    /// an error with <paramref name="message"/> and throws <see cref="ParseError"/>.
    /// </summary>
    /// <param name="type">The expected <see cref="TokenType"/>.</param>
    /// <param name="message">The error message to report if the token does not match.</param>
    /// <returns>The consumed token (via <see cref="Advance"/>).</returns>
    /// <exception cref="ParseError">Thrown when the current token does not match <paramref name="type"/>.</exception>
    private Token Consume(TokenType type, string message)
    {
        if (Check(type))
        {
            return Advance();
        }

        throw Error(Peek(), message);
    }

    /// <summary>
    /// Records a parse error with file location and returns a <see cref="ParseError"/>
    /// exception for control-flow unwinding.
    /// </summary>
    /// <param name="token">The token where the error was detected.</param>
    /// <param name="message">A description of what went wrong.</param>
    /// <returns>A <see cref="ParseError"/> instance to be thrown by the caller.</returns>
    private ParseError Error(Token token, string message)
    {
        string location = token.Type == TokenType.Eof
            ? "at end"
            : $"at '{token.Lexeme}'";

        string error = $"[{token.Span.File} {token.Span.StartLine}:{token.Span.StartColumn}] Error {location}: {message}";
        Errors.Add(error);
        return new ParseError();
    }

    /// <summary>
    /// Creates a <see cref="SourceSpan"/> that stretches from the start position of
    /// <paramref name="start"/> to the end position of <paramref name="end"/>.
    /// </summary>
    /// <param name="start">The span whose start position is used.</param>
    /// <param name="end">The span whose end position is used.</param>
    /// <returns>A new <see cref="SourceSpan"/> covering the combined range.</returns>
    /// <remarks>
    /// Used to compute the source span for compound AST nodes (e.g. a <see cref="BinaryExpr"/>
    /// spanning from its left operand to its right operand).
    /// </remarks>
    private static SourceSpan MakeSpan(SourceSpan start, SourceSpan end)
    {
        return new SourceSpan(start.File, start.StartLine, start.StartColumn, end.EndLine, end.EndColumn);
    }

    /// <summary>
    /// A private sentinel exception used purely for control flow during error recovery.
    /// </summary>
    /// <remarks>
    /// When the parser encounters an unexpected token, it throws <see cref="ParseError"/>
    /// to unwind the recursive-descent call stack back to <see cref="Parse"/>, which
    /// catches it and returns a <c>null</c> literal as a fallback AST node. This approach
    /// avoids cascading errors from a single syntax mistake.
    /// </remarks>
    private class ParseError : Exception { }
}
