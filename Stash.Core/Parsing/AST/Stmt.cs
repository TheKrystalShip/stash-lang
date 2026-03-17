namespace Stash.Parsing.AST;

using Stash.Common;

/// <summary>
/// Abstract base class for all statement nodes in the Stash AST.
/// </summary>
/// <remarks>
/// Statements represent actions that are executed for their side effects rather than
/// producing a value. Examples include variable declarations (<c>let x = 1;</c>),
/// control flow (<c>if</c>, <c>while</c>, <c>for</c>), and function definitions (<c>fn</c>).
/// Every AST node carries a <see cref="SourceSpan"/> so that error messages, warnings,
/// and diagnostics can reference the exact source location.
/// The <see cref="Accept{T}"/> method implements the Visitor pattern, dispatching to the
/// appropriate method on <see cref="IStmtVisitor{T}"/>. This allows the interpreter,
/// LSP analyzers, or any future analysis pass to operate on the tree without modifying
/// the node classes.
/// </remarks>
public abstract class Stmt
{
    /// <summary>
    /// Gets the source location (file, line, column range) of this statement in the original source code.
    /// </summary>
    public SourceSpan Span { get; }

    /// <summary>
    /// Initializes the base <see cref="Stmt"/> with the given source location.
    /// </summary>
    /// <param name="span">The source span covering this statement.</param>
    protected Stmt(SourceSpan span) => Span = span;

    /// <summary>
    /// Dispatches to the correct visit method on <paramref name="visitor"/> for this node type.
    /// </summary>
    /// <typeparam name="T">The return type of the visitor.</typeparam>
    /// <param name="visitor">The visitor that will process this node.</param>
    /// <returns>The value produced by the visitor for this node.</returns>
    public abstract T Accept<T>(IStmtVisitor<T> visitor);
}
