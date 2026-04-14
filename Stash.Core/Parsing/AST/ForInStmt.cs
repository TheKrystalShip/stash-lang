namespace Stash.Parsing.AST;

using Stash.Common;
using Stash.Lexing;

/// <summary>
/// A for-in loop: <c>for (let x in collection) { ... }</c>
/// </summary>
/// <remarks>
/// Iterates over arrays, strings, dictionaries, and ranges. See <see cref="ForStmt"/> for the C-style <c>for(;;)</c> loop.
/// When iterating with an index, the syntax is
/// <c>for (let i, x in collection) { ... }</c>. An optional type hint may be provided on the loop variable.
/// </remarks>
public class ForInStmt : Stmt
{
    /// <summary>Gets the optional index variable token. Non-null when the loop uses indexed iteration: <c>for (let i, x in arr)</c>.</summary>
    public Token? IndexName { get; }
    /// <summary>Gets the loop variable token that receives each element.</summary>
    public Token VariableName { get; }
    /// <summary>Gets the optional type hint for the loop variable. <c>null</c> if no type annotation was provided.</summary>
    public TypeHint? TypeHint { get; }
    /// <summary>Gets the expression that produces the iterable collection.</summary>
    public Expr Iterable { get; }
    /// <summary>Gets the block of statements executed for each element.</summary>
    public BlockStmt Body { get; }

    /// <summary>Initializes a new instance of <see cref="ForInStmt"/>.</summary>
    /// <param name="indexName">The optional index variable token, or <c>null</c>.</param>
    /// <param name="variableName">The loop variable token that receives each element.</param>
    /// <param name="typeHint">The optional type hint for the loop variable, or <c>null</c>.</param>
    /// <param name="iterable">The expression that produces the iterable collection.</param>
    /// <param name="body">The block of statements executed for each element.</param>
    /// <param name="span">The source location of this statement.</param>
    public ForInStmt(Token? indexName, Token variableName, TypeHint? typeHint, Expr iterable, BlockStmt body, SourceSpan span) : base(span, StmtType.ForIn)
    {
        IndexName = indexName;
        VariableName = variableName;
        TypeHint = typeHint;
        Iterable = iterable;
        Body = body;
    }

    /// <inheritdoc />
    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitForInStmt(this);
}
