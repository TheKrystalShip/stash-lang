namespace Stash.Parsing.AST;

using Stash.Common;

/// <summary>
/// Abstract base class for all expression nodes in the Stash AST.
/// </summary>
/// <remarks>
/// Every AST node carries a <see cref="SourceSpan"/> so that error messages, warnings,
/// and diagnostics can reference the exact source location — including for compound
/// expressions whose span covers multiple tokens.
/// The <see cref="Accept{T}"/> method implements the Visitor pattern, dispatching to the
/// appropriate method on <see cref="IExprVisitor{T}"/>. This allows the
/// <see cref="Stash.Interpreting.Interpreter"/>, pretty-printers, or any future analysis
/// pass to operate on the tree without modifying the node classes.
/// </remarks>
public abstract class Expr
{
    /// <summary>
    /// Gets the source location (file, line, column range) of this expression in the original source code.
    /// </summary>
    public SourceSpan Span { get; }

    /// <summary>
    /// Gets the concrete node type of this expression for switch-based dispatch.
    /// </summary>
    public ExprType NodeType { get; }

    /// <summary>
    /// Initializes the base <see cref="Expr"/> with the given source location and node type.
    /// </summary>
    /// <param name="span">The source span covering this expression.</param>
    /// <param name="nodeType">The concrete node type of this expression.</param>
    protected Expr(SourceSpan span, ExprType nodeType)
    {
        Span = span;
        NodeType = nodeType;
    }

    /// <summary>
    /// Dispatches to the correct visit method on <paramref name="visitor"/> for this node type.
    /// </summary>
    /// <typeparam name="T">The return type of the visitor.</typeparam>
    /// <param name="visitor">The visitor that will process this node.</param>
    /// <returns>The value produced by the visitor for this node.</returns>
    public abstract T Accept<T>(IExprVisitor<T> visitor);
}
