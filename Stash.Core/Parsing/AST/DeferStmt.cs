namespace Stash.Parsing.AST;

using Stash.Common;
using Stash.Lexing;

/// <summary>
/// Represents a <c>defer</c> statement that registers cleanup code to execute at function exit.
/// </summary>
public class DeferStmt : Stmt
{
    /// <summary>The <c>defer</c> keyword token.</summary>
    public Token DeferKeyword { get; }

    /// <summary>The deferred body — either an <see cref="ExprStmt"/> (single-statement) or a <see cref="BlockStmt"/> (block form).</summary>
    public Stmt Body { get; }

    /// <summary>Whether the defer uses <c>await</c> (<c>defer await expr</c>).</summary>
    public bool HasAwait { get; }

    public DeferStmt(Token deferKeyword, Stmt body, bool hasAwait, SourceSpan span)
        : base(span, StmtType.Defer)
    {
        DeferKeyword = deferKeyword;
        Body = body;
        HasAwait = hasAwait;
    }

    /// <inheritdoc />
    public override T Accept<T>(IStmtVisitor<T> visitor) => visitor.VisitDeferStmt(this);
}
