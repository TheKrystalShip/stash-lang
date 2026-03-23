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

    /// <summary>
    /// Visits an <see cref="AssignExpr"/> node (variable assignment <c>x = value</c>).
    /// </summary>
    /// <param name="expr">The assignment expression node to visit.</param>
    /// <returns>The result of processing the assignment.</returns>
    T VisitAssignExpr(AssignExpr expr);

    /// <summary>
    /// Visits a <see cref="CallExpr"/> node (function call <c>callee(args)</c>).
    /// </summary>
    /// <param name="expr">The call expression node to visit.</param>
    /// <returns>The result of invoking the callee.</returns>
    T VisitCallExpr(CallExpr expr);

    /// <summary>
    /// Visits an <see cref="ArrayExpr"/> node (array literal <c>[a, b, c]</c>).
    /// </summary>
    /// <param name="expr">The array expression node to visit.</param>
    /// <returns>The result of evaluating the array literal.</returns>
    T VisitArrayExpr(ArrayExpr expr);

    /// <summary>
    /// Visits an <see cref="IndexExpr"/> node (index access <c>arr[i]</c>).
    /// </summary>
    /// <param name="expr">The index expression node to visit.</param>
    /// <returns>The result of the index access.</returns>
    T VisitIndexExpr(IndexExpr expr);

    /// <summary>
    /// Visits an <see cref="IndexAssignExpr"/> node (index assignment <c>arr[i] = val</c>).
    /// </summary>
    /// <param name="expr">The index assignment expression node to visit.</param>
    /// <returns>The result of the index assignment.</returns>
    T VisitIndexAssignExpr(IndexAssignExpr expr);

    /// <summary>
    /// Visits a <see cref="StructInitExpr"/> node (struct instantiation <c>Name { field: value }</c>).
    /// </summary>
    /// <param name="expr">The struct instantiation expression node to visit.</param>
    /// <returns>The result of creating the struct instance.</returns>
    T VisitStructInitExpr(StructInitExpr expr);

    /// <summary>
    /// Visits a <see cref="DotExpr"/> node (dot access <c>obj.field</c>).
    /// </summary>
    /// <param name="expr">The dot expression node to visit.</param>
    /// <returns>The result of accessing the field or member.</returns>
    T VisitDotExpr(DotExpr expr);

    /// <summary>
    /// Visits a <see cref="DotAssignExpr"/> node (dot assignment <c>obj.field = value</c>).
    /// </summary>
    /// <param name="expr">The dot assignment expression node to visit.</param>
    /// <returns>The result of the field assignment.</returns>
    T VisitDotAssignExpr(DotAssignExpr expr);

    /// <summary>
    /// Visits an <see cref="InterpolatedStringExpr"/> node (string interpolation).
    /// </summary>
    /// <param name="expr">The interpolated string expression node to visit.</param>
    /// <returns>The result of evaluating and concatenating the interpolated parts.</returns>
    T VisitInterpolatedStringExpr(InterpolatedStringExpr expr);

    /// <summary>
    /// Visits a <see cref="CommandExpr"/> node (command literal <c>$(...)</c>).
    /// </summary>
    /// <param name="expr">The command expression node to visit.</param>
    /// <returns>The result of executing the command.</returns>
    T VisitCommandExpr(CommandExpr expr);

    /// <summary>
    /// Visits a <see cref="PipeExpr"/> node (pipe chain <c>$(cmd1) | $(cmd2)</c>).
    /// </summary>
    /// <param name="expr">The pipe expression node to visit.</param>
    /// <returns>The result of executing the piped command chain.</returns>
    T VisitPipeExpr(PipeExpr expr);

    /// <summary>
    /// Visits a <see cref="TryExpr"/> node (prefix <c>try expr</c>).
    /// If the inner expression throws a <c>RuntimeError</c>, the result is <c>null</c>.
    /// </summary>
    /// <param name="expr">The try expression node to visit.</param>
    /// <returns>The result of the inner expression, or <c>null</c> if it threw a RuntimeError.</returns>
    T VisitTryExpr(TryExpr expr);

    /// <summary>
    /// Visits a <see cref="NullCoalesceExpr"/> node (<c>left ?? right</c>).
    /// Returns <c>left</c> if it is not null, otherwise evaluates and returns <c>right</c>.
    /// </summary>
    /// <param name="expr">The null-coalescing expression node to visit.</param>
    /// <returns>The left value if non-null; otherwise the evaluated right value.</returns>
    T VisitNullCoalesceExpr(NullCoalesceExpr expr);

    /// <summary>
    /// Visits a <see cref="SwitchExpr"/> node (<c>subject switch { pattern =&gt; result, ... }</c>).
    /// </summary>
    /// <param name="expr">The switch expression node to visit.</param>
    /// <returns>The result of the matched arm's body expression.</returns>
    T VisitSwitchExpr(SwitchExpr expr);

    /// <summary>
    /// Visits an <see cref="UpdateExpr"/> node (prefix or postfix <c>++</c>/<c>--</c> operators).
    /// </summary>
    /// <param name="expr">The update expression node to visit.</param>
    /// <returns>The value before mutation for postfix; the value after mutation for prefix.</returns>
    T VisitUpdateExpr(UpdateExpr expr);

    /// <summary>
    /// Visits a <see cref="LambdaExpr"/> node (arrow function <c>(params) =&gt; body</c>).
    /// </summary>
    /// <param name="expr">The lambda expression node to visit.</param>
    /// <returns>The result of processing the lambda expression.</returns>
    T VisitLambdaExpr(LambdaExpr expr);

    /// <summary>
    /// Visits a <see cref="RedirectExpr"/> node (output redirection <c>$(cmd) &gt; "file"</c>).
    /// </summary>
    /// <param name="expr">The redirect expression node to visit.</param>
    /// <returns>The result of executing the command with redirected output.</returns>
    T VisitRedirectExpr(RedirectExpr expr);

    /// <summary>
    /// Visits a <see cref="RangeExpr"/> node (range expression <c>start..end</c> or <c>start..end..step</c>).
    /// </summary>
    /// <param name="expr">The range expression node to visit.</param>
    /// <returns>The result of evaluating the range.</returns>
    T VisitRangeExpr(RangeExpr expr);
    /// <summary>
    /// Visits a <see cref="DictLiteralExpr"/> node (dictionary literal <c>{ key: value, ... }</c>).
    /// </summary>
    /// <param name="expr">The dictionary literal expression node to visit.</param>
    /// <returns>The result of evaluating the dictionary literal.</returns>
    T VisitDictLiteralExpr(DictLiteralExpr expr);
}
