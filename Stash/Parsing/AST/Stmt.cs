namespace Stash.Parsing.AST;

using Stash.Common;

/// <summary>
/// Base class for all statement AST nodes in the Stash language.
/// </summary>
public abstract class Stmt
{
    public SourceSpan Span { get; }
    protected Stmt(SourceSpan span) => Span = span;
    public abstract T Accept<T>(IStmtVisitor<T> visitor);
}
