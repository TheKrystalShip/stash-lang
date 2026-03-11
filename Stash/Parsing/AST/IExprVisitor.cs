namespace Stash.Parsing.AST;

/// <summary>
/// Defines the visitor interface for traversing the Stash expression AST.
/// </summary>
/// <typeparam name="T">The return type produced by each visit method.</typeparam>
/// <remarks>
/// This is the visitor half of the Visitor pattern used throughout the Stash AST.
/// By separating operations (interpretation, pretty-printing, static analysis) from
/// node definitions, new passes can be added without modifying any <see cref="Expr"/> subclass.
/// Each concrete <see cref="Expr"/> node dispatches to the corresponding method via
/// <see cref="Expr.Accept{T}"/>.
/// </remarks>
public interface IExprVisitor<T>
{
    /// <summary>
    /// Visits a <see cref="LiteralExpr"/> node (numbers, strings, booleans, or <c>null</c>).
    /// </summary>
    /// <param name="expr">The literal expression node to visit.</param>
    /// <returns>The result of processing the literal value.</returns>
    T VisitLiteralExpr(LiteralExpr expr);

    /// <summary>
    /// Visits an <see cref="IdentifierExpr"/> node (a variable reference such as <c>foo</c>).
    /// </summary>
    /// <param name="expr">The identifier expression node to visit.</param>
    /// <returns>The result of resolving the identifier.</returns>
    T VisitIdentifierExpr(IdentifierExpr expr);

    /// <summary>
    /// Visits a <see cref="UnaryExpr"/> node (prefix operators <c>!</c> and <c>-</c>).
    /// </summary>
    /// <param name="expr">The unary expression node to visit.</param>
    /// <returns>The result of applying the unary operator.</returns>
    T VisitUnaryExpr(UnaryExpr expr);

    /// <summary>
    /// Visits a <see cref="BinaryExpr"/> node (infix operators like <c>+</c>, <c>==</c>, <c>&amp;&amp;</c>, etc.).
    /// </summary>
    /// <param name="expr">The binary expression node to visit.</param>
    /// <returns>The result of applying the binary operator.</returns>
    T VisitBinaryExpr(BinaryExpr expr);

    /// <summary>
    /// Visits a <see cref="GroupingExpr"/> node (a parenthesized expression).
    /// </summary>
    /// <param name="expr">The grouping expression node to visit.</param>
    /// <returns>The result of evaluating the inner expression.</returns>
    T VisitGroupingExpr(GroupingExpr expr);

    /// <summary>
    /// Visits a <see cref="TernaryExpr"/> node (the <c>condition ? then : else</c> operator).
    /// </summary>
    /// <param name="expr">The ternary expression node to visit.</param>
    /// <returns>The result of evaluating the selected branch.</returns>
    T VisitTernaryExpr(TernaryExpr expr);
}
