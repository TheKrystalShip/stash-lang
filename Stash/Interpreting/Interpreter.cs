namespace Stash.Interpreting;

using System;
using Stash.Lexing;
using Stash.Parsing.AST;

/// <summary>
/// Tree-walk interpreter that evaluates a Stash AST by visiting each expression node
/// and computing a runtime value.
/// </summary>
/// <remarks>
/// <para>
/// The interpreter implements <see cref="IExprVisitor{T}"/> with <c>object?</c> as the return
/// type because Stash is dynamically typed. Runtime values are represented as CLR objects:
/// <see cref="long"/> for integers, <see cref="double"/> for floating-point numbers,
/// <see cref="string"/> for text, <see cref="bool"/> for booleans, and <c>null</c> for the
/// absence of a value.
/// </para>
/// <para>
/// This is a tree-walk interpreter — it traverses the AST directly via the visitor pattern
/// rather than compiling to bytecode. This approach is simple to implement and debug, which
/// is ideal for Phase 1 of the language. A bytecode VM is planned for a future version.
/// </para>
/// <para>
/// <b>Truthiness rules</b> (per the Stash specification): <c>false</c>, <c>null</c>,
/// <c>0</c> (long), <c>0.0</c> (double), and <c>""</c> (empty string) are falsy.
/// Everything else is truthy.
/// </para>
/// <para>
/// <b>Equality semantics</b>: No type coercion is performed for equality checks.
/// <c>5 == "5"</c> is <c>false</c>, and <c>0 == false</c> is <c>false</c>. This is a
/// deliberate design choice to prevent subtle bugs common in languages with loose equality.
/// </para>
/// <para>
/// <b>Short-circuit evaluation</b>: The <c>&amp;&amp;</c> and <c>||</c> operators return
/// actual operand values, not coerced booleans. For example, <c>null || "default"</c> returns
/// <c>"default"</c>, and <c>"value" &amp;&amp; false</c> returns <c>false</c>. This enables
/// idiomatic patterns like <c>name || "anonymous"</c>.
/// </para>
/// <para>
/// <b>Numeric type promotion</b>: When a <see cref="long"/> and <see cref="double"/> appear
/// together in arithmetic, the <c>long</c> is promoted to <c>double</c> and the result is
/// <c>double</c>. When both operands are <c>long</c>, the result stays <c>long</c>
/// (integer arithmetic, including truncating division).
/// </para>
/// <para>
/// <b>String concatenation via <c>+</c></b>: When either operand of <c>+</c> is a string,
/// the other is converted using <see cref="Stringify"/> and the result is a concatenated
/// string. This matches the Stash spec's type coercion rules for the <c>+</c> operator.
/// </para>
/// </remarks>
public class Interpreter : IExprVisitor<object?>
{
    /// <summary>
    /// Evaluates a parsed expression AST and returns the resulting runtime value.
    /// </summary>
    /// <param name="expression">The root <see cref="Expr"/> node to evaluate.</param>
    /// <returns>
    /// The computed value: a <see cref="long"/>, <see cref="double"/>, <see cref="string"/>,
    /// <see cref="bool"/>, or <c>null</c>.
    /// </returns>
    /// <exception cref="RuntimeError">
    /// Thrown if evaluation fails (e.g., type mismatch, division by zero, undefined variable).
    /// </exception>
    public object? Interpret(Expr expression)
    {
        return expression.Accept(this);
    }

    /// <summary>
    /// Visits a literal expression node and returns its compile-time value directly.
    /// </summary>
    /// <param name="expr">A <see cref="LiteralExpr"/> containing a numeric, string, boolean, or null value.</param>
    /// <returns>The literal value stored in the AST node.</returns>
    public object? VisitLiteralExpr(LiteralExpr expr)
    {
        return expr.Value;
    }

    /// <summary>
    /// Visits an identifier expression node. Currently always throws because there is no
    /// variable environment in Phase 1.
    /// </summary>
    /// <param name="expr">An <see cref="IdentifierExpr"/> referencing a variable by name.</param>
    /// <returns>Never returns — always throws.</returns>
    /// <exception cref="RuntimeError">
    /// Always thrown with the undefined variable's name and source location. Variable lookup
    /// will be implemented in Phase 2 when <c>let</c>/<c>const</c> declarations and an
    /// <c>Environment</c> chain are added.
    /// </exception>
    public object? VisitIdentifierExpr(IdentifierExpr expr)
    {
        throw new RuntimeError($"Undefined variable '{expr.Name.Lexeme}'.", expr.Span);
    }

    /// <summary>
    /// Visits a grouping (parenthesized) expression by evaluating its inner expression.
    /// </summary>
    /// <param name="expr">
    /// A <see cref="GroupingExpr"/> wrapping a sub-expression in parentheses.
    /// </param>
    /// <returns>The result of evaluating the inner expression.</returns>
    public object? VisitGroupingExpr(GroupingExpr expr)
    {
        return expr.Expression.Accept(this);
    }

    /// <summary>
    /// Visits a unary expression (<c>!</c> or <c>-</c>) and evaluates it.
    /// </summary>
    /// <param name="expr">A <see cref="UnaryExpr"/> with an operator and a right-hand operand.</param>
    /// <returns>
    /// For <c>!</c>: a <see cref="bool"/> — the logical negation using Stash truthiness rules.
    /// For <c>-</c>: a <see cref="long"/> or <see cref="double"/> — the arithmetic negation.
    /// </returns>
    /// <exception cref="RuntimeError">
    /// Thrown if <c>-</c> is applied to a non-numeric operand, or if an unknown unary operator
    /// is encountered.
    /// </exception>
    public object? VisitUnaryExpr(UnaryExpr expr)
    {
        object? right = expr.Right.Accept(this);

        switch (expr.Operator.Type)
        {
            case TokenType.Bang:
                return !IsTruthy(right);

            case TokenType.Minus:
                if (right is long i)
                {
                    return -i;
                }

                if (right is double d)
                {
                    return -d;
                }

                throw new RuntimeError("Operand must be a number.", expr.Operator.Span);

            default:
                throw new RuntimeError($"Unknown unary operator '{expr.Operator.Lexeme}'.", expr.Operator.Span);
        }
    }

    /// <summary>
    /// Visits a binary expression and evaluates it according to the operator type.
    /// </summary>
    /// <param name="expr">
    /// A <see cref="BinaryExpr"/> with a left operand, operator token, and right operand.
    /// </param>
    /// <returns>
    /// The result of the binary operation. The type depends on the operator:
    /// arithmetic operators return <see cref="long"/> or <see cref="double"/>;
    /// comparison and equality operators return <see cref="bool"/>;
    /// <c>+</c> may return a <see cref="string"/> for concatenation;
    /// <c>&amp;&amp;</c> and <c>||</c> return the determining operand value.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <c>&amp;&amp;</c> and <c>||</c> use short-circuit evaluation: the right operand is
    /// only evaluated if necessary. They return the actual operand value that determined the
    /// result, not a coerced boolean. For example, <c>null || "default"</c> returns
    /// <c>"default"</c>.
    /// </para>
    /// <para>
    /// All other operators eagerly evaluate both sides before applying the operation.
    /// </para>
    /// </remarks>
    /// <exception cref="RuntimeError">
    /// Thrown for type mismatches (e.g., subtracting strings), division by zero, or unknown operators.
    /// </exception>
    public object? VisitBinaryExpr(BinaryExpr expr)
    {
        // Short-circuit operators evaluate left first, then conditionally evaluate right.
        if (expr.Operator.Type == TokenType.AmpersandAmpersand)
        {
            object? left = expr.Left.Accept(this);
            return !IsTruthy(left) ? left : expr.Right.Accept(this);
        }

        if (expr.Operator.Type == TokenType.PipePipe)
        {
            object? left = expr.Left.Accept(this);
            return IsTruthy(left) ? left : expr.Right.Accept(this);
        }

        // Non-short-circuit operators evaluate both sides.
        object? leftVal = expr.Left.Accept(this);
        object? rightVal = expr.Right.Accept(this);

        switch (expr.Operator.Type)
        {
            case TokenType.Plus:
                return EvaluatePlus(leftVal, rightVal, expr);

            case TokenType.Minus:
                return EvaluateArithmetic(leftVal, rightVal, expr, (a, b) => a - b, (a, b) => a - b);

            case TokenType.Star:
                return EvaluateArithmetic(leftVal, rightVal, expr, (a, b) => a * b, (a, b) => a * b);

            case TokenType.Slash:
                return EvaluateDivision(leftVal, rightVal, expr);

            case TokenType.Percent:
                return EvaluateModulo(leftVal, rightVal, expr);

            case TokenType.EqualEqual:
                return IsEqual(leftVal, rightVal);

            case TokenType.BangEqual:
                return !IsEqual(leftVal, rightVal);

            case TokenType.Less:
                return CompareNumeric(leftVal, rightVal, expr, (a, b) => a < b, (a, b) => a < b);

            case TokenType.Greater:
                return CompareNumeric(leftVal, rightVal, expr, (a, b) => a > b, (a, b) => a > b);

            case TokenType.LessEqual:
                return CompareNumeric(leftVal, rightVal, expr, (a, b) => a <= b, (a, b) => a <= b);

            case TokenType.GreaterEqual:
                return CompareNumeric(leftVal, rightVal, expr, (a, b) => a >= b, (a, b) => a >= b);

            default:
                throw new RuntimeError($"Unknown binary operator '{expr.Operator.Lexeme}'.", expr.Operator.Span);
        }
    }

    /// <summary>
    /// Visits a ternary (conditional) expression: <c>condition ? thenBranch : elseBranch</c>.
    /// </summary>
    /// <param name="expr">A <see cref="TernaryExpr"/> with condition, then-branch, and else-branch.</param>
    /// <returns>
    /// The result of evaluating <paramref name="expr"/>'s then-branch if the condition is truthy,
    /// or the else-branch if the condition is falsy. Only the selected branch is evaluated.
    /// </returns>
    public object? VisitTernaryExpr(TernaryExpr expr)
    {
        object? condition = expr.Condition.Accept(this);
        return IsTruthy(condition)
            ? expr.ThenBranch.Accept(this)
            : expr.ElseBranch.Accept(this);
    }

    /// <summary>
    /// Determines whether a runtime value is truthy according to Stash's truthiness rules.
    /// </summary>
    /// <param name="value">The runtime value to test.</param>
    /// <returns>
    /// <c>false</c> for <c>null</c>, <c>false</c>, <c>0L</c>, <c>0.0</c>, and <c>""</c>
    /// (empty string). <c>true</c> for everything else.
    /// </returns>
    /// <remarks>
    /// These rules are defined by the Stash language specification. Unlike C# (where only
    /// <see cref="bool"/> is allowed in conditionals), Stash treats every value as having an
    /// inherent truthiness, similar to Python or JavaScript — but with stricter falsy values
    /// (e.g., <c>0</c> and <c>""</c> are also falsy).
    /// </remarks>
    private bool IsTruthy(object? value)
    {
        if (value is null)
        {
            return false;
        }

        if (value is bool b)
        {
            return b;
        }

        if (value is long i)
        {
            return i != 0;
        }

        if (value is double d)
        {
            return d != 0.0;
        }

        if (value is string s)
        {
            return s.Length != 0;
        }

        return true;
    }

    /// <summary>
    /// Converts a runtime value to its Stash string representation for display in the REPL.
    /// </summary>
    /// <param name="value">The runtime value to convert.</param>
    /// <returns>
    /// <c>"null"</c> for <c>null</c>; <c>"true"</c>/<c>"false"</c> for booleans (lowercase,
    /// matching Stash syntax); locale-invariant formatting for doubles (e.g., always <c>"3.14"</c>,
    /// never <c>"3,14"</c>); and <see cref="object.ToString"/> for all other types.
    /// </returns>
    /// <remarks>
    /// Uses <see cref="System.Globalization.CultureInfo.InvariantCulture"/> for doubles to
    /// ensure consistent output regardless of the user's system locale. This is also used
    /// internally by string concatenation via the <c>+</c> operator when one operand is a
    /// string and the other is not.
    /// </remarks>
    public string Stringify(object? value)
    {
        if (value is null)
        {
            return "null";
        }

        if (value is bool b)
        {
            return b ? "true" : "false";
        }

        if (value is double d)
        {
            return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return value.ToString()!;
    }

    /// <summary>
    /// Evaluates the <c>+</c> operator, which supports both numeric addition and string concatenation.
    /// </summary>
    /// <param name="left">The evaluated left operand.</param>
    /// <param name="right">The evaluated right operand.</param>
    /// <param name="expr">The <see cref="BinaryExpr"/> node, used for error-location reporting.</param>
    /// <returns>
    /// If both operands are numeric: the sum as <see cref="long"/> (when both are <c>long</c>) or
    /// <see cref="double"/> (when at least one is <c>double</c>). If either operand is a
    /// <see cref="string"/>: a concatenated string where the non-string operand is converted
    /// via <see cref="Stringify"/>.
    /// </returns>
    /// <exception cref="RuntimeError">
    /// Thrown when neither numeric addition nor string concatenation applies (e.g., <c>true + false</c>).
    /// </exception>
    private object? EvaluatePlus(object? left, object? right, BinaryExpr expr)
    {
        // Both numeric — add with promotion.
        if (left is long li && right is long ri)
        {
            return li + ri;
        }

        if (IsNumeric(left) && IsNumeric(right))
        {
            return ToDouble(left) + ToDouble(right);
        }

        // String concatenation — if either side is a string.
        if (left is string || right is string)
        {
            return Stringify(left) + Stringify(right);
        }

        throw new RuntimeError("Operands must be numbers or strings.", expr.Operator.Span);
    }

    /// <summary>
    /// Evaluates a generic arithmetic binary operation (<c>-</c>, <c>*</c>) using separate
    /// functions for integer and floating-point arithmetic.
    /// </summary>
    /// <param name="left">The evaluated left operand.</param>
    /// <param name="right">The evaluated right operand.</param>
    /// <param name="expr">The <see cref="BinaryExpr"/> node, used for error-location reporting.</param>
    /// <param name="intOp">
    /// The operation to apply when both operands are <see cref="long"/> (e.g., <c>(a, b) =&gt; a - b</c>).
    /// </param>
    /// <param name="doubleOp">
    /// The operation to apply when at least one operand is <see cref="double"/> and the other
    /// is numeric. The <c>long</c> operand is promoted to <c>double</c> via <see cref="ToDouble"/>.
    /// </param>
    /// <returns>
    /// A <see cref="long"/> when both operands are <c>long</c>, or a <see cref="double"/> when
    /// type promotion occurs.
    /// </returns>
    /// <exception cref="RuntimeError">
    /// Thrown when either operand is not a number (e.g., <c>"hello" - 1</c>).
    /// </exception>
    private object? EvaluateArithmetic(
        object? left, object? right, BinaryExpr expr,
        Func<long, long, long> intOp, Func<double, double, double> doubleOp)
    {
        if (left is long li && right is long ri)
        {
            return intOp(li, ri);
        }

        if (IsNumeric(left) && IsNumeric(right))
        {
            return doubleOp(ToDouble(left), ToDouble(right));
        }

        throw new RuntimeError("Operands must be numbers.", expr.Operator.Span);
    }

    /// <summary>
    /// Evaluates the <c>/</c> (division) operator with division-by-zero checking.
    /// </summary>
    /// <param name="left">The evaluated left operand (dividend).</param>
    /// <param name="right">The evaluated right operand (divisor).</param>
    /// <param name="expr">The <see cref="BinaryExpr"/> node, used for error-location reporting.</param>
    /// <returns>
    /// A <see cref="long"/> when both operands are <c>long</c> (truncating integer division),
    /// or a <see cref="double"/> when at least one operand is <c>double</c>.
    /// </returns>
    /// <remarks>
    /// Integer division truncates toward zero (C# default for <c>long / long</c>).
    /// Division by zero is always a <see cref="RuntimeError"/> — Stash does not produce
    /// <c>Infinity</c> or <c>NaN</c>.
    /// </remarks>
    /// <exception cref="RuntimeError">
    /// Thrown when either operand is not numeric, or when the divisor is zero.
    /// </exception>
    private object? EvaluateDivision(object? left, object? right, BinaryExpr expr)
    {
        if (!IsNumeric(left) || !IsNumeric(right))
        {
            throw new RuntimeError("Operands must be numbers.", expr.Operator.Span);
        }

        if (left is long li && right is long ri)
        {
            if (ri == 0)
            {
                throw new RuntimeError("Division by zero.", expr.Operator.Span);
            }

            return li / ri;
        }

        double dr = ToDouble(right);
        if (dr == 0.0)
        {
            throw new RuntimeError("Division by zero.", expr.Operator.Span);
        }

        return ToDouble(left) / dr;
    }

    /// <summary>
    /// Evaluates the <c>%</c> (modulo) operator with division-by-zero checking.
    /// </summary>
    /// <param name="left">The evaluated left operand.</param>
    /// <param name="right">The evaluated right operand (modulus).</param>
    /// <param name="expr">The <see cref="BinaryExpr"/> node, used for error-location reporting.</param>
    /// <returns>
    /// The remainder as <see cref="long"/> when both operands are <c>long</c>, or
    /// <see cref="double"/> when type promotion occurs.
    /// </returns>
    /// <exception cref="RuntimeError">
    /// Thrown when either operand is not numeric, or when the modulus is zero.
    /// </exception>
    private object? EvaluateModulo(object? left, object? right, BinaryExpr expr)
    {
        if (!IsNumeric(left) || !IsNumeric(right))
        {
            throw new RuntimeError("Operands must be numbers.", expr.Operator.Span);
        }

        if (left is long li && right is long ri)
        {
            if (ri == 0)
            {
                throw new RuntimeError("Division by zero.", expr.Operator.Span);
            }

            return li % ri;
        }

        double dr = ToDouble(right);
        if (dr == 0.0)
        {
            throw new RuntimeError("Division by zero.", expr.Operator.Span);
        }

        return ToDouble(left) % dr;
    }

    /// <summary>
    /// Tests two runtime values for equality without any type coercion.
    /// </summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns>
    /// <c>true</c> if both are <c>null</c>, or if both are the same type and
    /// <see cref="object.Equals(object, object)"/> returns <c>true</c>. Returns <c>false</c>
    /// if the types differ, even if the values are "equivalent" (e.g., <c>5 == 5.0</c> is
    /// <c>false</c>, <c>0 == false</c> is <c>false</c>).
    /// </returns>
    /// <remarks>
    /// The strict no-coercion rule is a deliberate design decision to prevent subtle bugs
    /// common in languages with loose equality semantics (e.g., JavaScript's <c>==</c>).
    /// </remarks>
    private bool IsEqual(object? left, object? right)
    {
        if (left is null && right is null)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }
        // No type coercion — different types are never equal.
        if (left.GetType() != right.GetType())
        {
            return false;
        }

        return object.Equals(left, right);
    }

    /// <summary>
    /// Evaluates a numeric comparison operator (<c>&lt;</c>, <c>&gt;</c>, <c>&lt;=</c>, <c>&gt;=</c>).
    /// </summary>
    /// <param name="left">The evaluated left operand.</param>
    /// <param name="right">The evaluated right operand.</param>
    /// <param name="expr">The <see cref="BinaryExpr"/> node, used for error-location reporting.</param>
    /// <param name="intOp">
    /// The comparison to apply when both operands are <see cref="long"/>.
    /// </param>
    /// <param name="doubleOp">
    /// The comparison to apply when at least one operand is <see cref="double"/>.
    /// The <c>long</c> operand is promoted via <see cref="ToDouble"/>.
    /// </param>
    /// <returns><c>true</c> or <c>false</c> based on the comparison result.</returns>
    /// <exception cref="RuntimeError">
    /// Thrown when either operand is not numeric (e.g., <c>"a" &lt; "b"</c> is not supported).
    /// </exception>
    private bool CompareNumeric(
        object? left, object? right, BinaryExpr expr,
        Func<long, long, bool> intOp, Func<double, double, bool> doubleOp)
    {
        if (left is long li && right is long ri)
        {
            return intOp(li, ri);
        }

        if (IsNumeric(left) && IsNumeric(right))
        {
            return doubleOp(ToDouble(left), ToDouble(right));
        }

        throw new RuntimeError("Operands must be numbers.", expr.Operator.Span);
    }

    /// <summary>
    /// Checks whether a runtime value is a numeric type (<see cref="long"/> or <see cref="double"/>).
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <returns><c>true</c> if <paramref name="value"/> is <see cref="long"/> or <see cref="double"/>.</returns>
    private static bool IsNumeric(object? value) => value is long or double;

    /// <summary>
    /// Converts a numeric value to <see cref="double"/> for type-promoted arithmetic.
    /// </summary>
    /// <param name="value">
    /// A value that must be <see cref="long"/> or <see cref="double"/>. Callers should verify
    /// this with <see cref="IsNumeric"/> first.
    /// </param>
    /// <returns>The value as a <see cref="double"/>.</returns>
    private static double ToDouble(object? value) => value is long i ? (double)i : (double)value!;
}
