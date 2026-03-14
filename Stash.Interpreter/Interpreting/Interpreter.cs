namespace Stash.Interpreting;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Stash.Common;
using Stash.Debugging;
using Stash.Lexing;
using Stash.Parsing;
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
public class Interpreter : IExprVisitor<object?>, IStmtVisitor<object?>
{
    private readonly Environment _globals;
    private Environment _environment;
    private string? _pendingStdin;
    private string? _lastError;
    private readonly Dictionary<string, Environment> _moduleCache = new();
    private readonly HashSet<string> _importStack = new();
    private string? _currentFile;
    private readonly List<CallFrame> _callStack = new();
    private IDebugger? _debugger;
    private string[] _scriptArgs = Array.Empty<string>();
    private readonly List<(StashInstance Handle, System.Diagnostics.Process OsProcess)> _trackedProcesses = new();
    private readonly Dictionary<StashInstance, StashInstance> _processWaitCache = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Gets or sets the current file path being executed.
    /// Used for resolving relative import paths.
    /// </summary>
    public string? CurrentFile
    {
        get => _currentFile;
        set => _currentFile = value;
    }

    /// <summary>
    /// Sets the script arguments that will be parsed by an <c>args</c> declaration.
    /// </summary>
    public void SetScriptArgs(string[] args)
    {
        _scriptArgs = args;
    }

    /// <summary>
    /// Gets or sets the debugger. When set, debug hooks are invoked during execution.
    /// </summary>
    public IDebugger? Debugger
    {
        get => _debugger;
        set => _debugger = value;
    }

    /// <summary>
    /// Gets the current call stack as a read-only list.
    /// </summary>
    public IReadOnlyList<CallFrame> CallStack => _callStack;

    public Interpreter()
    {
        _globals = new Environment();
        _environment = _globals;
        DefineBuiltIns();
    }

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

    public void Interpret(List<Stmt> statements)
    {
        try
        {
            foreach (Stmt statement in statements)
            {
                Execute(statement);
            }
        }
        catch (BreakException)
        {
            var err = new RuntimeError("'break' used outside of a loop.");
            _debugger?.OnError(err, _callStack);
            throw err;
        }
        catch (ContinueException)
        {
            var err = new RuntimeError("'continue' used outside of a loop.");
            _debugger?.OnError(err, _callStack);
            throw err;
        }
        catch (ReturnException)
        {
            var err = new RuntimeError("'return' used outside of a function.");
            _debugger?.OnError(err, _callStack);
            throw err;
        }
    }

    private void Execute(Stmt stmt)
    {
        _debugger?.OnBeforeExecute(stmt.Span, _environment);
        stmt.Accept(this);
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
    /// Visits an identifier expression node, looking up the variable's value in the environment chain.
    /// </summary>
    /// <param name="expr">An <see cref="IdentifierExpr"/> referencing a variable by name.</param>
    /// <returns>The current value of the variable.</returns>
    /// <exception cref="RuntimeError">
    /// Thrown if the variable is not defined in any enclosing scope.
    /// </exception>
    public object? VisitIdentifierExpr(IdentifierExpr expr)
    {
        return _environment.Get(expr.Name.Lexeme, expr.Span);
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

    public object? VisitUpdateExpr(UpdateExpr expr)
    {
        object? oldValue = expr.Operand.Accept(this);

        if (oldValue is not long && oldValue is not double)
        {
            throw new RuntimeError("Operand of '++' or '--' must be a number.", expr.Operator.Span);
        }

        object newValue;
        if (expr.Operator.Type == TokenType.PlusPlus)
        {
            newValue = oldValue is long l ? (object)(l + 1) : (object)((double)oldValue + 1.0);
        }
        else
        {
            newValue = oldValue is long l ? (object)(l - 1) : (object)((double)oldValue - 1.0);
        }

        // Write back
        if (expr.Operand is IdentifierExpr id)
        {
            _environment.Assign(id.Name.Lexeme, newValue, id.Name.Span);
        }
        else if (expr.Operand is DotExpr dot)
        {
            object? obj = dot.Object.Accept(this);
            if (obj is StashInstance instance)
            {
                instance.SetField(dot.Name.Lexeme, newValue, dot.Name.Span);
            }
            else
            {
                throw new RuntimeError("Only struct instances have fields.", dot.Name.Span);
            }
        }
        else if (expr.Operand is IndexExpr idx)
        {
            object? collection = idx.Object.Accept(this);
            object? index = idx.Index.Accept(this);
            if (collection is List<object?> list && index is long i)
            {
                list[(int)i] = newValue;
            }
            else
            {
                throw new RuntimeError("Invalid target for increment/decrement.", expr.Operator.Span);
            }
        }
        else
        {
            throw new RuntimeError("Invalid target for increment/decrement.", expr.Operator.Span);
        }

        return expr.IsPrefix ? newValue : oldValue;
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

    public object? VisitSwitchExpr(SwitchExpr expr)
    {
        object? subject = expr.Subject.Accept(this);
        foreach (var arm in expr.Arms)
        {
            if (arm.IsDiscard)
            {
                return arm.Body.Accept(this);
            }
            object? pattern = arm.Pattern!.Accept(this);
            if (IsEqual(subject, pattern))
            {
                return arm.Body.Accept(this);
            }
        }
        throw new RuntimeError("No matching arm in switch expression.", expr.Span);
    }

    /// <summary>
    /// Visits a try expression (<c>try expr</c>), catching any <see cref="RuntimeError"/> and
    /// returning <c>null</c> in that case. The error message is stored in <see cref="_lastError"/>.
    /// </summary>
    /// <param name="expr">The <see cref="TryExpr"/> to evaluate.</param>
    /// <returns>The result of the inner expression, or <c>null</c> if a RuntimeError was caught.</returns>
    public object? VisitTryExpr(TryExpr expr)
    {
        try
        {
            return expr.Expression.Accept(this);
        }
        catch (RuntimeError e)
        {
            _lastError = e.Message;
            return null;
        }
    }

    /// <summary>
    /// Visits a null-coalescing expression (<c>left ?? right</c>).
    /// Returns <c>left</c> if it is not null; otherwise evaluates and returns <c>right</c>.
    /// </summary>
    /// <param name="expr">The <see cref="NullCoalesceExpr"/> to evaluate.</param>
    /// <returns>The left value if non-null; otherwise the right value.</returns>
    public object? VisitNullCoalesceExpr(NullCoalesceExpr expr)
    {
        object? left = expr.Left.Accept(this);
        if (left is not null)
        {
            return left;
        }
        return expr.Right.Accept(this);
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

        if (value is StashInstance instance)
        {
            return instance.ToString();
        }

        if (value is StashStruct structDef)
        {
            return structDef.ToString();
        }

        if (value is StashEnumValue enumVal)
        {
            return enumVal.ToString();
        }

        if (value is StashEnum enumType)
        {
            return enumType.ToString();
        }

        if (value is StashNamespace ns)
        {
            return ns.ToString();
        }

        if (value is List<object?> list)
        {
            var elements = new System.Text.StringBuilder("[");
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0)
                {
                    elements.Append(", ");
                }

                elements.Append(Stringify(list[i]));
            }
            elements.Append(']');
            return elements.ToString();
        }

        if (value is StashDictionary dict)
        {
            var sb = new System.Text.StringBuilder("{");
            bool first = true;
            foreach (var key in dict.Keys())
            {
                if (!first) sb.Append(", ");
                first = false;
                sb.Append(Stringify(key));
                sb.Append(": ");
                sb.Append(Stringify(dict.Get(key!)));
            }
            sb.Append('}');
            return sb.ToString();
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

    public object? VisitAssignExpr(AssignExpr expr)
    {
        object? value = expr.Value.Accept(this);
        _environment.Assign(expr.Name.Lexeme, value, expr.Name.Span);
        return value;
    }

    public object? VisitCallExpr(CallExpr expr)
    {
        object? callee = expr.Callee.Accept(this);

        List<object?> arguments = new();
        foreach (Expr argument in expr.Arguments)
        {
            arguments.Add(argument.Accept(this));
        }

        if (callee is not IStashCallable function)
        {
            throw new RuntimeError("Can only call functions.", expr.Paren.Span);
        }

        if (function.Arity != -1 && arguments.Count != function.Arity)
        {
            throw new RuntimeError(
                $"Expected {function.Arity} arguments but got {arguments.Count}.",
                expr.Paren.Span);
        }

        string functionName = callee is StashFunction sf ? sf.ToString() : callee.ToString() ?? "<unknown>";
        if (functionName.StartsWith("<fn ") && functionName.EndsWith(">"))
        {
            functionName = functionName.Substring(4, functionName.Length - 5);
        }
        else if (functionName.StartsWith("<built-in fn ") && functionName.EndsWith(">"))
        {
            functionName = functionName.Substring(13, functionName.Length - 14);
        }

        var callFrame = new CallFrame
        {
            FunctionName = functionName,
            CallSite = expr.Span,
            LocalScope = _environment
        };
        _callStack.Add(callFrame);
        _debugger?.OnFunctionEnter(functionName, expr.Span, _environment);

        try
        {
            object? result = function.Call(this, arguments);
            return result;
        }
        finally
        {
            _callStack.RemoveAt(_callStack.Count - 1);
            _debugger?.OnFunctionExit(functionName);
        }
    }

    public void ExecuteBlock(List<Stmt> statements, Environment environment)
    {
        Environment previous = _environment;
        try
        {
            _environment = environment;
            foreach (Stmt statement in statements)
            {
                Execute(statement);
            }
        }
        finally
        {
            _environment = previous;
        }
    }

    /// <summary>
    /// Evaluates an expression in the given environment, then restores the previous environment.
    /// Used by <see cref="StashLambda"/> for expression-body lambdas.
    /// </summary>
    public object? EvaluateInEnvironment(Expr expr, Environment environment)
    {
        Environment previous = _environment;
        try
        {
            _environment = environment;
            return expr.Accept(this);
        }
        finally
        {
            _environment = previous;
        }
    }

    public object? VisitExprStmt(ExprStmt stmt)
    {
        stmt.Expression.Accept(this);
        return null;
    }

    public object? VisitVarDeclStmt(VarDeclStmt stmt)
    {
        object? value = null;
        if (stmt.Initializer is not null)
        {
            value = stmt.Initializer.Accept(this);
        }

        _environment.Define(stmt.Name.Lexeme, value);
        return null;
    }

    public object? VisitConstDeclStmt(ConstDeclStmt stmt)
    {
        object? value = stmt.Initializer.Accept(this);
        _environment.DefineConstant(stmt.Name.Lexeme, value);
        return null;
    }

    public object? VisitBlockStmt(BlockStmt stmt)
    {
        ExecuteBlock(stmt.Statements, new Environment(_environment));
        return null;
    }

    public object? VisitIfStmt(IfStmt stmt)
    {
        if (IsTruthy(stmt.Condition.Accept(this)))
        {
            Execute(stmt.ThenBranch);
        }
        else if (stmt.ElseBranch is not null)
        {
            Execute(stmt.ElseBranch);
        }

        return null;
    }

    public object? VisitWhileStmt(WhileStmt stmt)
    {
        while (IsTruthy(stmt.Condition.Accept(this)))
        {
            try
            {
                Execute(stmt.Body);
            }
            catch (BreakException)
            {
                break;
            }
            catch (ContinueException)
            {
                // Continue to next loop iteration
            }
        }

        return null;
    }

    public object? VisitForInStmt(ForInStmt stmt)
    {
        object? iterable = stmt.Iterable.Accept(this);

        IEnumerable<object?> items;
        if (iterable is List<object?> list)
        {
            items = list;
        }
        else if (iterable is string str)
        {
            items = StringToChars(str);
        }
        else if (iterable is StashDictionary dict)
        {
            items = dict.IterableKeys();
        }
        else
        {
            throw new RuntimeError("Can only iterate over arrays, strings, and dictionaries.", stmt.Iterable.Span);
        }

        foreach (object? item in items)
        {
            var loopEnv = new Environment(_environment);
            loopEnv.Define(stmt.VariableName.Lexeme, item);

            try
            {
                ExecuteBlock(stmt.Body.Statements, loopEnv);
            }
            catch (BreakException)
            {
                break;
            }
            catch (ContinueException)
            {
                // Continue to next iteration
            }
        }

        return null;
    }

    private static IEnumerable<object?> StringToChars(string str)
    {
        foreach (char c in str)
        {
            yield return c.ToString();
        }
    }

    public object? VisitBreakStmt(BreakStmt stmt)
    {
        throw new BreakException();
    }

    public object? VisitContinueStmt(ContinueStmt stmt)
    {
        throw new ContinueException();
    }

    public object? VisitFnDeclStmt(FnDeclStmt stmt)
    {
        var function = new StashFunction(stmt, _environment);
        _environment.Define(stmt.Name.Lexeme, function);
        return null;
    }

    public object? VisitLambdaExpr(LambdaExpr expr)
    {
        return new StashLambda(expr, _environment);
    }

    public object? VisitStructDeclStmt(StructDeclStmt stmt)
    {
        var fields = new List<string>();
        foreach (var field in stmt.Fields)
        {
            fields.Add(field.Lexeme);
        }

        var structDef = new StashStruct(stmt.Name.Lexeme, fields);
        _environment.Define(stmt.Name.Lexeme, structDef);
        return null;
    }

    public object? VisitEnumDeclStmt(EnumDeclStmt stmt)
    {
        var members = new List<string>();
        foreach (var member in stmt.Members)
        {
            members.Add(member.Lexeme);
        }

        var enumDef = new StashEnum(stmt.Name.Lexeme, members);
        _environment.Define(stmt.Name.Lexeme, enumDef);
        return null;
    }

    public object? VisitImportStmt(ImportStmt stmt)
    {
        string modulePath = (string)stmt.Path.Literal!;
        string resolvedPath = ResolveModulePath(modulePath, stmt.Path.Span);

        // Check for circular imports
        if (_importStack.Contains(resolvedPath))
        {
            throw new RuntimeError(
                $"Circular import detected: '{modulePath}' is already being imported.",
                stmt.Span);
        }

        // Get or load the module
        Environment moduleEnv = LoadModule(resolvedPath, stmt.Path.Span);

        // Bind imported names into the current scope
        foreach (Token name in stmt.Names)
        {
            object? value = moduleEnv.Get(name.Lexeme, name.Span);
            _environment.Define(name.Lexeme, value);
        }

        return null;
    }

    public object? VisitImportAsStmt(ImportAsStmt stmt)
    {
        string modulePath = (string)stmt.Path.Literal!;
        string resolvedPath = ResolveModulePath(modulePath, stmt.Path.Span);

        // Check for circular imports
        if (_importStack.Contains(resolvedPath))
        {
            throw new RuntimeError(
                $"Circular import detected: '{modulePath}' is already being imported.",
                stmt.Span);
        }

        // Get or load the module
        Environment moduleEnv = LoadModule(resolvedPath, stmt.Path.Span);

        // Wrap all module-level bindings in a namespace
        var ns = new StashNamespace(stmt.Alias.Lexeme);
        foreach (var (name, value) in moduleEnv.GetAllBindings())
        {
            ns.Define(name, value);
        }

        _environment.Define(stmt.Alias.Lexeme, ns);
        return null;
    }


    private object? CoerceArgValue(string value, string? type, string argName)
    {
        if (type is null || type == "string")
        {
            return value;
        }

        if (type == "int")
        {
            if (long.TryParse(value, out long result))
            {
                return result;
            }
            throw new RuntimeError($"Cannot parse '{value}' as int for argument '{argName}'.");
        }

        if (type == "float")
        {
            if (double.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double result))
            {
                return result;
            }
            throw new RuntimeError($"Cannot parse '{value}' as float for argument '{argName}'.");
        }

        if (type == "bool")
        {
            if (value == "true" || value == "1" || value == "yes")
            {
                return true;
            }

            if (value == "false" || value == "0" || value == "no")
            {
                return false;
            }

            throw new RuntimeError($"Cannot parse '{value}' as bool for argument '{argName}'.");
        }

        throw new RuntimeError($"Unknown type '{type}' for argument '{argName}'.");
    }

    /// <summary>
    /// Implements the parseArgs() built-in function.
    /// Takes an ArgTree StashInstance and parses _scriptArgs against it.
    /// Returns a StashInstance with all parsed argument values.
    /// </summary>
    private object? ExecuteParseArgs(object? treeObj)
    {
        if (treeObj is not StashInstance tree || tree.TypeName != "ArgTree")
        {
            throw new RuntimeError("Argument to 'parseArgs' must be an ArgTree instance.");
        }

        string? scriptName = tree.GetField("name", null) as string;
        string? version = tree.GetField("version", null) as string;
        var flagDefs = tree.GetField("flags", null) as List<object?> ?? new();
        var optionDefs = tree.GetField("options", null) as List<object?> ?? new();
        var commandDefs = tree.GetField("commands", null) as List<object?> ?? new();
        var positionalDefs = tree.GetField("positionals", null) as List<object?> ?? new();

        var fields = new Dictionary<string, object?>();

        // Initialize all flags to false
        foreach (var flagObj in flagDefs)
        {
            var flag = CastArgDef(flagObj, "flags");
            fields[GetArgDefName(flag)] = false;
        }

        // Initialize all options with defaults
        foreach (var optObj in optionDefs)
        {
            var opt = CastArgDef(optObj, "options");
            fields[GetArgDefName(opt)] = opt.GetField("default", null);
        }

        // Initialize all positionals with defaults
        foreach (var posObj in positionalDefs)
        {
            var pos = CastArgDef(posObj, "positionals");
            fields[GetArgDefName(pos)] = pos.GetField("default", null);
        }

        // Initialize command to null
        if (commandDefs.Count > 0)
        {
            fields["command"] = null;
        }

        // Initialize subcommand containers
        foreach (var cmdObj in commandDefs)
        {
            var cmd = CastArgDef(cmdObj, "commands");
            string cmdName = GetArgDefName(cmd);
            var subTree = cmd.GetField("args", null) as StashInstance;
            var cmdFields = new Dictionary<string, object?>();

            if (subTree is not null)
            {
                var subFlags = subTree.GetField("flags", null) as List<object?> ?? new();
                var subOpts = subTree.GetField("options", null) as List<object?> ?? new();
                var subPos = subTree.GetField("positionals", null) as List<object?> ?? new();

                foreach (var f in subFlags)
                {
                    var fd = CastArgDef(f, "flags");
                    cmdFields[GetArgDefName(fd)] = false;
                }
                foreach (var o in subOpts)
                {
                    var od = CastArgDef(o, "options");
                    cmdFields[GetArgDefName(od)] = od.GetField("default", null);
                }
                foreach (var p in subPos)
                {
                    var pd = CastArgDef(p, "positionals");
                    cmdFields[GetArgDefName(pd)] = pd.GetField("default", null);
                }
            }
            fields[cmdName] = new StashInstance("ArgsCommand", cmdFields);
        }

        // Build lookup maps for efficient parsing
        var flagsByLong = new Dictionary<string, StashInstance>();
        var flagsByShort = new Dictionary<string, StashInstance>();
        var optionsByLong = new Dictionary<string, StashInstance>();
        var optionsByShort = new Dictionary<string, StashInstance>();
        var commandsByName = new Dictionary<string, StashInstance>();

        foreach (var flagObj in flagDefs)
        {
            var flag = (StashInstance)flagObj!;
            string name = GetArgDefName(flag);
            flagsByLong[$"--{name}"] = flag;
            string? shortName = flag.GetField("short", null) as string;
            if (shortName is not null)
            {
                flagsByShort[$"-{shortName}"] = flag;
            }
        }
        foreach (var optObj in optionDefs)
        {
            var opt = (StashInstance)optObj!;
            string name = GetArgDefName(opt);
            optionsByLong[$"--{name}"] = opt;
            string? shortName = opt.GetField("short", null) as string;
            if (shortName is not null)
            {
                optionsByShort[$"-{shortName}"] = opt;
            }
        }
        foreach (var cmdObj in commandDefs)
        {
            var cmd = (StashInstance)cmdObj!;
            commandsByName[GetArgDefName(cmd)] = cmd;
        }

        // Parse _scriptArgs
        int positionalIndex = 0;
        StashInstance? activeCommand = null;
        string? activeCommandName = null;

        // Build per-command lookup maps
        var cmdFlagsByLong = new Dictionary<string, Dictionary<string, StashInstance>>();
        var cmdFlagsByShort = new Dictionary<string, Dictionary<string, StashInstance>>();
        var cmdOptionsByLong = new Dictionary<string, Dictionary<string, StashInstance>>();
        var cmdOptionsByShort = new Dictionary<string, Dictionary<string, StashInstance>>();
        var cmdPositionalDefs = new Dictionary<string, List<StashInstance>>();
        var cmdPositionalIndices = new Dictionary<string, int>();

        foreach (var cmdObj in commandDefs)
        {
            var cmd = (StashInstance)cmdObj!;
            string cmdName = GetArgDefName(cmd);
            var subTree = cmd.GetField("args", null) as StashInstance;

            var cfl = new Dictionary<string, StashInstance>();
            var cfs = new Dictionary<string, StashInstance>();
            var col = new Dictionary<string, StashInstance>();
            var cos = new Dictionary<string, StashInstance>();
            var cpos = new List<StashInstance>();
            cmdPositionalIndices[cmdName] = 0;

            if (subTree is not null)
            {
                var subFlags = subTree.GetField("flags", null) as List<object?> ?? new();
                var subOpts = subTree.GetField("options", null) as List<object?> ?? new();
                var subPos = subTree.GetField("positionals", null) as List<object?> ?? new();

                foreach (var f in subFlags)
                {
                    var fd = (StashInstance)f!;
                    string n = GetArgDefName(fd);
                    cfl[$"--{n}"] = fd;
                    string? s = fd.GetField("short", null) as string;
                    if (s is not null)
                    {
                        cfs[$"-{s}"] = fd;
                    }
                }
                foreach (var o in subOpts)
                {
                    var od = (StashInstance)o!;
                    string n = GetArgDefName(od);
                    col[$"--{n}"] = od;
                    string? s = od.GetField("short", null) as string;
                    if (s is not null)
                    {
                        cos[$"-{s}"] = od;
                    }
                }
                foreach (var p in subPos)
                {
                    cpos.Add((StashInstance)p!);
                }
            }

            cmdFlagsByLong[cmdName] = cfl;
            cmdFlagsByShort[cmdName] = cfs;
            cmdOptionsByLong[cmdName] = col;
            cmdOptionsByShort[cmdName] = cos;
            cmdPositionalDefs[cmdName] = cpos;
        }

        int i = 0;
        while (i < _scriptArgs.Length)
        {
            string arg = _scriptArgs[i];

            // Check for --key=value format
            string? equalValue = null;
            if (arg.StartsWith("--") && arg.Contains('='))
            {
                int eqIdx = arg.IndexOf('=');
                equalValue = arg.Substring(eqIdx + 1);
                arg = arg.Substring(0, eqIdx);
            }

            // If we have an active command, try command-level args first
            if (activeCommand is not null && activeCommandName is not null)
            {
                var cmdInstance = (StashInstance)fields[activeCommandName]!;

                if (cmdFlagsByLong[activeCommandName].TryGetValue(arg, out var cmdFlag))
                {
                    cmdInstance.SetField(GetArgDefName(cmdFlag), true, null);
                    i++;
                    continue;
                }
                if (cmdFlagsByShort[activeCommandName].TryGetValue(arg, out cmdFlag))
                {
                    cmdInstance.SetField(GetArgDefName(cmdFlag), true, null);
                    i++;
                    continue;
                }
                if (cmdOptionsByLong[activeCommandName].TryGetValue(arg, out var cmdOpt))
                {
                    string? val = equalValue;
                    if (val is null)
                    {
                        i++;
                        if (i >= _scriptArgs.Length)
                        {
                            throw new RuntimeError($"Option '{arg}' requires a value.");
                        }

                        val = _scriptArgs[i];
                    }
                    string? optType = cmdOpt.GetField("type", null) as string;
                    cmdInstance.SetField(GetArgDefName(cmdOpt), CoerceArgValue(val, optType, arg), null);
                    i++;
                    continue;
                }
                if (cmdOptionsByShort[activeCommandName].TryGetValue(arg, out cmdOpt))
                {
                    string? val = equalValue;
                    if (val is null)
                    {
                        i++;
                        if (i >= _scriptArgs.Length)
                        {
                            throw new RuntimeError($"Option '{arg}' requires a value.");
                        }

                        val = _scriptArgs[i];
                    }
                    string? optType = cmdOpt.GetField("type", null) as string;
                    cmdInstance.SetField(GetArgDefName(cmdOpt), CoerceArgValue(val, optType, arg), null);
                    i++;
                    continue;
                }

                // Check command-level positionals
                int cmdPosIdx = cmdPositionalIndices[activeCommandName];
                if (!arg.StartsWith("-") && cmdPosIdx < cmdPositionalDefs[activeCommandName].Count)
                {
                    var cp = cmdPositionalDefs[activeCommandName][cmdPosIdx];
                    string? posType = cp.GetField("type", null) as string;
                    cmdInstance.SetField(GetArgDefName(cp), CoerceArgValue(arg, posType, GetArgDefName(cp)), null);
                    cmdPositionalIndices[activeCommandName]++;
                    i++;
                    continue;
                }
            }

            // Top-level flag match
            if (flagsByLong.TryGetValue(arg, out var topFlag))
            {
                fields[GetArgDefName(topFlag)] = true;
                i++;
                continue;
            }
            if (flagsByShort.TryGetValue(arg, out topFlag))
            {
                fields[GetArgDefName(topFlag)] = true;
                i++;
                continue;
            }

            // Top-level option match
            if (optionsByLong.TryGetValue(arg, out var topOpt))
            {
                string? val = equalValue;
                if (val is null)
                {
                    i++;
                    if (i >= _scriptArgs.Length)
                    {
                        throw new RuntimeError($"Option '{arg}' requires a value.");
                    }

                    val = _scriptArgs[i];
                }
                string? optType = topOpt.GetField("type", null) as string;
                fields[GetArgDefName(topOpt)] = CoerceArgValue(val, optType, arg);
                i++;
                continue;
            }
            if (optionsByShort.TryGetValue(arg, out topOpt))
            {
                string? val = equalValue;
                if (val is null)
                {
                    i++;
                    if (i >= _scriptArgs.Length)
                    {
                        throw new RuntimeError($"Option '{arg}' requires a value.");
                    }

                    val = _scriptArgs[i];
                }
                string? optType = topOpt.GetField("type", null) as string;
                fields[GetArgDefName(topOpt)] = CoerceArgValue(val, optType, arg);
                i++;
                continue;
            }

            // Command match
            if (!arg.StartsWith("-") && commandsByName.TryGetValue(arg, out var matchedCmd))
            {
                fields["command"] = arg;
                activeCommand = matchedCmd;
                activeCommandName = arg;
                i++;
                continue;
            }

            // Positional (only non-dash args when not matching a command)
            if (!arg.StartsWith("-") && positionalIndex < positionalDefs.Count)
            {
                var pos = (StashInstance)positionalDefs[positionalIndex]!;
                string? posType = pos.GetField("type", null) as string;
                fields[GetArgDefName(pos)] = CoerceArgValue(arg, posType, GetArgDefName(pos));
                positionalIndex++;
                i++;
                continue;
            }

            // Unknown argument
            throw new RuntimeError($"Unknown argument '{_scriptArgs[i]}'.");
        }

        // Auto-handle help flag
        if (fields.TryGetValue("help", out var helpVal) && helpVal is true)
        {
            PrintArgsHelp(tree, fields);
            System.Environment.Exit(0);
        }

        // Auto-handle version flag
        if (fields.TryGetValue("version", out var versionFlag) && versionFlag is true && version is not null)
        {
            Console.WriteLine(version);
            System.Environment.Exit(0);
        }

        // Validate required options
        foreach (var optObj in optionDefs)
        {
            var opt = (StashInstance)optObj!;
            string optName = GetArgDefName(opt);
            bool required = opt.GetField("required", null) is true;
            if (required && fields[optName] is null)
            {
                throw new RuntimeError($"Required option '--{optName}' was not provided.");
            }
        }

        // Validate required positionals
        foreach (var posObj in positionalDefs)
        {
            var pos = (StashInstance)posObj!;
            string posName = GetArgDefName(pos);
            bool required = pos.GetField("required", null) is true;
            if (required && fields[posName] is null)
            {
                throw new RuntimeError($"Required positional argument '{posName}' was not provided.");
            }
        }

        // Validate required command-level args if a command is active
        if (activeCommand is not null && activeCommandName is not null)
        {
            var subTree = activeCommand.GetField("args", null) as StashInstance;
            if (subTree is not null)
            {
                var cmdInstance = (StashInstance)fields[activeCommandName]!;
                var subOpts = subTree.GetField("options", null) as List<object?> ?? new();
                var subPos = subTree.GetField("positionals", null) as List<object?> ?? new();

                foreach (var optObj in subOpts)
                {
                    var opt = (StashInstance)optObj!;
                    string optName = GetArgDefName(opt);
                    bool required = opt.GetField("required", null) is true;
                    if (required && cmdInstance.GetField(optName, null) is null)
                    {
                        throw new RuntimeError($"Required option '--{optName}' for command '{activeCommandName}' was not provided.");
                    }
                }
                foreach (var posObj in subPos)
                {
                    var pos = (StashInstance)posObj!;
                    string posName = GetArgDefName(pos);
                    bool required = pos.GetField("required", null) is true;
                    if (required && cmdInstance.GetField(posName, null) is null)
                    {
                        throw new RuntimeError($"Required positional argument '{posName}' for command '{activeCommandName}' was not provided.");
                    }
                }
            }
        }

        return new StashInstance("Args", fields);
    }

    /// <summary>
    /// Validates that an object from an ArgTree list is an ArgDef StashInstance.
    /// </summary>
    private static StashInstance CastArgDef(object? obj, string listName)
    {
        if (obj is not StashInstance inst || inst.TypeName != "ArgDef")
        {
            throw new RuntimeError($"All entries in ArgTree '{listName}' must be ArgDef instances.");
        }
        return inst;
    }

    /// <summary>
    /// Gets the 'name' field from an ArgDef instance. Throws if null.
    /// </summary>
    private static string GetArgDefName(StashInstance argDef)
    {
        if (argDef.GetField("name", null) is not string name || name == "")
        {
            throw new RuntimeError("ArgDef 'name' field is required and must be a non-empty string.");
        }
        return name;
    }

    private void PrintArgsHelp(StashInstance tree, Dictionary<string, object?> fields)
    {
        var sb = new System.Text.StringBuilder();

        string? scriptName = tree.GetField("name", null) as string;
        string? version = tree.GetField("version", null) as string;
        string? description = tree.GetField("description", null) as string;
        var flagDefs = tree.GetField("flags", null) as List<object?> ?? new();
        var optionDefs = tree.GetField("options", null) as List<object?> ?? new();
        var commandDefs = tree.GetField("commands", null) as List<object?> ?? new();
        var positionalDefs = tree.GetField("positionals", null) as List<object?> ?? new();

        // Header
        if (scriptName is not null)
        {
            sb.Append(scriptName);
            if (version is not null)
            {
                sb.Append($" v{version}");
            }

            sb.AppendLine();
        }

        if (description is not null && description != "")
        {
            sb.AppendLine(description);
        }

        if (scriptName is not null || (description is not null && description != ""))
        {
            sb.AppendLine();
        }

        // Usage line
        sb.AppendLine("USAGE:");
        sb.Append("  ");
        sb.Append(scriptName ?? "script");
        if (commandDefs.Count > 0)
        {
            sb.Append(" [command]");
        }

        if (optionDefs.Count > 0 || flagDefs.Count > 0)
        {
            sb.Append(" [options]");
        }

        foreach (var posObj in positionalDefs)
        {
            var pos = (StashInstance)posObj!;
            string posName = (string)pos.GetField("name", null)!;
            bool required = pos.GetField("required", null) is true;
            if (required)
            {
                sb.Append($" <{posName}>");
            }
            else
            {
                sb.Append($" [{posName}]");
            }
        }
        sb.AppendLine();
        sb.AppendLine();

        // Commands
        if (commandDefs.Count > 0)
        {
            sb.AppendLine("COMMANDS:");
            int maxCmdLen = 0;
            foreach (var cmdObj in commandDefs)
            {
                var cmd = (StashInstance)cmdObj!;
                string cmdName = (string)cmd.GetField("name", null)!;
                if (cmdName.Length > maxCmdLen)
                {
                    maxCmdLen = cmdName.Length;
                }
            }
            foreach (var cmdObj in commandDefs)
            {
                var cmd = (StashInstance)cmdObj!;
                string cmdName = (string)cmd.GetField("name", null)!;
                string? cmdDesc = cmd.GetField("description", null) as string;
                sb.Append($"  {cmdName.PadRight(maxCmdLen + 2)}");
                if (cmdDesc is not null)
                {
                    sb.Append(cmdDesc);
                }

                sb.AppendLine();
            }
            sb.AppendLine();
        }

        // Positional arguments
        if (positionalDefs.Count > 0)
        {
            sb.AppendLine("ARGUMENTS:");
            int maxPosLen = 0;
            foreach (var posObj in positionalDefs)
            {
                var pos = (StashInstance)posObj!;
                string posName = (string)pos.GetField("name", null)!;
                bool required = pos.GetField("required", null) is true;
                string label = required ? $"<{posName}>" : $"[{posName}]";
                if (label.Length > maxPosLen)
                {
                    maxPosLen = label.Length;
                }
            }
            foreach (var posObj in positionalDefs)
            {
                var pos = (StashInstance)posObj!;
                string posName = (string)pos.GetField("name", null)!;
                bool required = pos.GetField("required", null) is true;
                string? posDesc = pos.GetField("description", null) as string;
                object? posDefault = pos.GetField("default", null);
                string label = required ? $"<{posName}>" : $"[{posName}]";
                sb.Append($"  {label.PadRight(maxPosLen + 2)}");
                if (posDesc is not null)
                {
                    sb.Append(posDesc);
                }

                if (posDefault is not null)
                {
                    sb.Append($" (default: {Stringify(posDefault)})");
                }

                sb.AppendLine();
            }
            sb.AppendLine();
        }

        // Options and flags
        if (flagDefs.Count > 0 || optionDefs.Count > 0)
        {
            sb.AppendLine("OPTIONS:");
            var optLines = new List<(string Left, string? Right)>();

            foreach (var flagObj in flagDefs)
            {
                var flag = (StashInstance)flagObj!;
                string flagName = (string)flag.GetField("name", null)!;
                string? shortName = flag.GetField("short", null) as string;
                string? flagDesc = flag.GetField("description", null) as string;
                string left;
                if (shortName is not null)
                {
                    left = $"-{shortName}, --{flagName}";
                }
                else
                {
                    left = $"    --{flagName}";
                }

                optLines.Add((left, flagDesc));
            }

            foreach (var optObj in optionDefs)
            {
                var opt = (StashInstance)optObj!;
                string optName = (string)opt.GetField("name", null)!;
                string? shortName = opt.GetField("short", null) as string;
                string? optType = opt.GetField("type", null) as string;
                string? optDesc = opt.GetField("description", null) as string;
                object? optDefault = opt.GetField("default", null);
                bool required = opt.GetField("required", null) is true;

                string typeHint = optType is not null ? $" <{optType}>" : " <value>";
                string left;
                if (shortName is not null)
                {
                    left = $"-{shortName}, --{optName}{typeHint}";
                }
                else
                {
                    left = $"    --{optName}{typeHint}";
                }

                string? right = optDesc;
                if (required)
                {
                    right = (right ?? "") + " (required)";
                }
                else if (optDefault is not null)
                {
                    right = (right ?? "") + $" (default: {Stringify(optDefault)})";
                }

                optLines.Add((left, right));
            }

            int maxLeft = 0;
            foreach (var (left, _) in optLines)
            {
                if (left.Length > maxLeft)
                {
                    maxLeft = left.Length;
                }
            }

            foreach (var (left, right) in optLines)
            {
                sb.Append($"  {left.PadRight(maxLeft + 2)}");
                if (right is not null)
                {
                    sb.Append(right);
                }

                sb.AppendLine();
            }
            sb.AppendLine();
        }

        // Per-command details
        foreach (var cmdObj in commandDefs)
        {
            var cmd = (StashInstance)cmdObj!;
            string cmdName = (string)cmd.GetField("name", null)!;
            var subTree = cmd.GetField("args", null) as StashInstance;
            if (subTree is null)
            {
                continue;
            }

            var subFlags = subTree.GetField("flags", null) as List<object?> ?? new();
            var subOpts = subTree.GetField("options", null) as List<object?> ?? new();
            var subPos = subTree.GetField("positionals", null) as List<object?> ?? new();

            if (subFlags.Count == 0 && subOpts.Count == 0 && subPos.Count == 0)
            {
                continue;
            }

            sb.AppendLine($"COMMAND '{cmdName}':");

            if (subPos.Count > 0)
            {
                foreach (var posObj in subPos)
                {
                    var pos = (StashInstance)posObj!;
                    string posName = (string)pos.GetField("name", null)!;
                    bool required = pos.GetField("required", null) is true;
                    string? posDesc = pos.GetField("description", null) as string;
                    string label = required ? $"<{posName}>" : $"[{posName}]";
                    sb.Append($"  {label,-20}");
                    if (posDesc is not null)
                    {
                        sb.Append(posDesc);
                    }

                    sb.AppendLine();
                }
            }

            var cmdOptLines = new List<(string Left, string? Right)>();
            foreach (var flagObj in subFlags)
            {
                var flag = (StashInstance)flagObj!;
                string flagName = (string)flag.GetField("name", null)!;
                string? shortName = flag.GetField("short", null) as string;
                string? flagDesc = flag.GetField("description", null) as string;
                string left;
                if (shortName is not null)
                {
                    left = $"-{shortName}, --{flagName}";
                }
                else
                {
                    left = $"    --{flagName}";
                }

                cmdOptLines.Add((left, flagDesc));
            }
            foreach (var optObj in subOpts)
            {
                var opt = (StashInstance)optObj!;
                string optName = (string)opt.GetField("name", null)!;
                string? shortName = opt.GetField("short", null) as string;
                string? optType = opt.GetField("type", null) as string;
                string? optDesc = opt.GetField("description", null) as string;
                object? optDefault = opt.GetField("default", null);
                bool required = opt.GetField("required", null) is true;
                string typeHint = optType is not null ? $" <{optType}>" : " <value>";
                string left;
                if (shortName is not null)
                {
                    left = $"-{shortName}, --{optName}{typeHint}";
                }
                else
                {
                    left = $"    --{optName}{typeHint}";
                }

                string? right = optDesc;
                if (required)
                {
                    right = (right ?? "") + " (required)";
                }
                else if (optDefault is not null)
                {
                    right = (right ?? "") + $" (default: {Stringify(optDefault)})";
                }

                cmdOptLines.Add((left, right));
            }

            if (cmdOptLines.Count > 0)
            {
                int maxLeft = 0;
                foreach (var (left, _) in cmdOptLines)
                {
                    if (left.Length > maxLeft)
                    {
                        maxLeft = left.Length;
                    }
                }
                foreach (var (left, right) in cmdOptLines)
                {
                    sb.Append($"  {left.PadRight(maxLeft + 2)}");
                    if (right is not null)
                    {
                        sb.Append(right);
                    }

                    sb.AppendLine();
                }
            }
            sb.AppendLine();
        }

        Console.Write(sb.ToString());
    }

    private string ResolveModulePath(string modulePath, SourceSpan span)
    {
        string basePath;
        if (_currentFile is not null)
        {
            basePath = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_currentFile))!;
        }
        else
        {
            basePath = System.IO.Directory.GetCurrentDirectory();
        }

        string fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(basePath, modulePath));

        if (!System.IO.File.Exists(fullPath))
        {
            throw new RuntimeError($"Cannot find module '{modulePath}'.", span);
        }

        return fullPath;
    }

    private Environment LoadModule(string resolvedPath, SourceSpan span)
    {
        if (_moduleCache.TryGetValue(resolvedPath, out Environment? cached))
        {
            return cached;
        }

        string source;
        try
        {
            source = System.IO.File.ReadAllText(resolvedPath);
        }
        catch (System.IO.IOException e)
        {
            throw new RuntimeError($"Cannot read module '{resolvedPath}': {e.Message}", span);
        }

        // Lex
        var lexer = new Lexer(source, resolvedPath);
        var tokens = lexer.ScanTokens();
        if (lexer.Errors.Count > 0)
        {
            throw new RuntimeError($"Lex errors in module '{resolvedPath}': {lexer.Errors[0]}", span);
        }

        // Parse
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        if (parser.Errors.Count > 0)
        {
            throw new RuntimeError($"Parse errors in module '{resolvedPath}': {parser.Errors[0]}", span);
        }

        // Execute in isolated environment
        var moduleEnv = new Environment(_globals);

        // Save current state
        Environment previousEnv = _environment;
        string? previousFile = _currentFile;

        try
        {
            _importStack.Add(resolvedPath);
            _currentFile = resolvedPath;
            _environment = moduleEnv;

            foreach (Stmt statement in statements)
            {
                Execute(statement);
            }

            _moduleCache[resolvedPath] = moduleEnv;
            return moduleEnv;
        }
        finally
        {
            _environment = previousEnv;
            _currentFile = previousFile;
            _importStack.Remove(resolvedPath);
        }
    }

    public object? VisitStructInitExpr(StructInitExpr expr)
    {
        object? structDef = expr.Target is not null
            ? expr.Target.Accept(this)
            : _environment.Get(expr.Name.Lexeme, expr.Span);

        if (structDef is not StashStruct template)
        {
            throw new RuntimeError($"'{expr.Name.Lexeme}' is not a struct.", expr.Name.Span);
        }

        var fieldValues = new Dictionary<string, object?>();

        // Initialize all fields to null
        foreach (string field in template.Fields)
        {
            fieldValues[field] = null;
        }

        // Set provided field values
        var seenFields = new HashSet<string>();
        foreach (var (fieldToken, valueExpr) in expr.FieldValues)
        {
            string fieldName = fieldToken.Lexeme;
            if (!fieldValues.ContainsKey(fieldName))
            {
                throw new RuntimeError($"Unknown field '{fieldName}' for struct '{template.Name}'.", fieldToken.Span);
            }

            if (!seenFields.Add(fieldName))
            {
                throw new RuntimeError($"Duplicate field '{fieldName}' in struct initializer.", fieldToken.Span);
            }

            fieldValues[fieldName] = valueExpr.Accept(this);
        }

        return new StashInstance(template.Name, fieldValues);
    }

    public object? VisitDotExpr(DotExpr expr)
    {
        object? obj = expr.Object.Accept(this);

        if (obj is StashInstance instance)
        {
            return instance.GetField(expr.Name.Lexeme, expr.Name.Span);
        }

        if (obj is StashEnum enumDef)
        {
            StashEnumValue? value = enumDef.GetMember(expr.Name.Lexeme);
            if (value is null)
            {
                throw new RuntimeError($"Enum '{enumDef.Name}' has no member '{expr.Name.Lexeme}'.", expr.Name.Span);
            }
            return value;
        }

        if (obj is StashNamespace ns)
        {
            return ns.GetMember(expr.Name.Lexeme, expr.Name.Span);
        }

        throw new RuntimeError("Only struct instances, enums, and namespaces have members.", expr.Name.Span);
    }

    public object? VisitDotAssignExpr(DotAssignExpr expr)
    {
        object? obj = expr.Object.Accept(this);

        if (obj is StashNamespace)
        {
            throw new RuntimeError("Cannot assign to namespace members.", expr.Name.Span);
        }

        if (obj is not StashInstance instance)
        {
            throw new RuntimeError("Only struct instances have fields.", expr.Name.Span);
        }

        object? value = expr.Value.Accept(this);
        instance.SetField(expr.Name.Lexeme, value, expr.Name.Span);
        return value;
    }

    /// <summary>
    /// Visits an interpolated string expression by evaluating each part, converting it to
    /// a string, and concatenating the results.
    /// </summary>
    /// <param name="expr">The <see cref="InterpolatedStringExpr"/> to evaluate.</param>
    /// <returns>The concatenated string result.</returns>
    public object? VisitInterpolatedStringExpr(InterpolatedStringExpr expr)
    {
        var sb = new System.Text.StringBuilder();
        foreach (Expr part in expr.Parts)
        {
            object? value = part.Accept(this);
            sb.Append(Stringify(value));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Evaluates a <see cref="CommandExpr"/> by building the command string from its parts,
    /// executing it via the system shell, and returning a <see cref="StashInstance"/> with
    /// <c>stdout</c>, <c>stderr</c>, and <c>exitCode</c> fields.
    /// </summary>
    /// <param name="expr">The command expression to evaluate.</param>
    /// <returns>A <see cref="StashInstance"/> representing the command result.</returns>
    public object? VisitCommandExpr(CommandExpr expr)
    {
        var commandBuilder = new System.Text.StringBuilder();
        foreach (Expr part in expr.Parts)
        {
            object? value = part.Accept(this);
            commandBuilder.Append(Stringify(value));
        }

        string command = commandBuilder.ToString().Trim();

        if (string.IsNullOrEmpty(command))
        {
            throw new RuntimeError("Command cannot be empty.", expr.Span);
        }

        string stdout;
        string stderr;
        int exitCode;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/bin/sh",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = _pendingStdin is not null,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(command);

            using var process = Process.Start(psi);
            if (process is null)
            {
                throw new RuntimeError("Failed to start process.", expr.Span);
            }

            if (_pendingStdin is not null)
            {
                process.StandardInput.Write(_pendingStdin);
                process.StandardInput.Close();
                _pendingStdin = null;
            }

            // Read stdout and stderr concurrently to avoid deadlock
            // when either stream's buffer fills.
            var stdoutTask = Task.Run(() => process.StandardOutput.ReadToEnd());
            var stderrTask = Task.Run(() => process.StandardError.ReadToEnd());
            Task.WaitAll(stdoutTask, stderrTask);
            stdout = stdoutTask.Result;
            stderr = stderrTask.Result;

            process.WaitForExit();
            exitCode = process.ExitCode;
        }
        catch (RuntimeError)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new RuntimeError($"Command execution failed: {ex.Message}", expr.Span);
        }

        var fields = new Dictionary<string, object?>
        {
            ["stdout"] = stdout,
            ["stderr"] = stderr,
            ["exitCode"] = (long)exitCode
        };

        return new StashInstance("CommandResult", fields);
    }

    /// <summary>
    /// Evaluates a pipe expression by chaining stdout of the left command to stdin of the right.
    /// Short-circuits on non-zero exit code.
    /// </summary>
    public object? VisitPipeExpr(PipeExpr expr)
    {
        object? leftResult = expr.Left.Accept(this);

        if (leftResult is not StashInstance leftCmd || leftCmd.TypeName != "CommandResult")
        {
            throw new RuntimeError("Left side of pipe must be a command expression.", expr.Span);
        }

        object? exitCodeVal = leftCmd.GetField("exitCode", expr.Span);
        if (exitCodeVal is long exitCode && exitCode != 0)
        {
            return leftResult;
        }

        object? stdoutVal = leftCmd.GetField("stdout", expr.Span);
        string stdinForRight = stdoutVal as string ?? "";

        _pendingStdin = stdinForRight;

        try
        {
            object? rightResult = expr.Right.Accept(this);

            if (rightResult is not StashInstance rightCmd || rightCmd.TypeName != "CommandResult")
            {
                throw new RuntimeError("Right side of pipe must be a command expression.", expr.Span);
            }

            return rightResult;
        }
        finally
        {
            _pendingStdin = null;
        }
    }

    /// <summary>
    /// Evaluates a redirect expression by executing the command and writing the selected
    /// stream(s) to the target file path.
    /// </summary>
    public object? VisitRedirectExpr(RedirectExpr expr)
    {
        // Evaluate the inner command/pipe expression
        object? result = expr.Expression.Accept(this);

        if (result is not StashInstance cmdResult || cmdResult.TypeName != "CommandResult")
        {
            throw new RuntimeError("Output redirection requires a command expression.", expr.Span);
        }

        // Evaluate the target file path
        object? targetVal = expr.Target.Accept(this);
        if (targetVal is not string filePath)
        {
            throw new RuntimeError("Redirection target must be a string file path.", expr.Target.Span);
        }

        string? stdoutContent = cmdResult.GetField("stdout", expr.Span) as string;
        string? stderrContent = cmdResult.GetField("stderr", expr.Span) as string;

        try
        {
            // Determine which content to write based on the stream selector
            string contentToWrite = expr.Stream switch
            {
                RedirectStream.Stdout => stdoutContent ?? "",
                RedirectStream.Stderr => stderrContent ?? "",
                RedirectStream.All => (stdoutContent ?? "") + (stderrContent ?? ""),
                _ => throw new RuntimeError($"Unknown redirect stream: {expr.Stream}.", expr.Span)
            };

            if (expr.Append)
            {
                File.AppendAllText(filePath, contentToWrite);
            }
            else
            {
                File.WriteAllText(filePath, contentToWrite);
            }

            // Clear the redirected stream(s) in the result since they went to a file.
            // Return a new CommandResult with the redirected stream(s) emptied.
            var newFields = new Dictionary<string, object?>
            {
                ["stdout"] = expr.Stream is RedirectStream.Stdout or RedirectStream.All ? "" : stdoutContent,
                ["stderr"] = expr.Stream is RedirectStream.Stderr or RedirectStream.All ? "" : stderrContent,
                ["exitCode"] = cmdResult.GetField("exitCode", expr.Span)
            };

            return new StashInstance("CommandResult", newFields);
        }
        catch (RuntimeError)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new RuntimeError($"Redirection failed: {ex.Message}", expr.Span);
        }
    }

    public object? VisitReturnStmt(ReturnStmt stmt)
    {
        object? value = null;
        if (stmt.Value is not null)
        {
            value = stmt.Value.Accept(this);
        }

        throw new ReturnException(value);
    }

    public object? VisitArrayExpr(ArrayExpr expr)
    {
        var elements = new List<object?>();
        foreach (Expr element in expr.Elements)
        {
            elements.Add(element.Accept(this));
        }
        return elements;
    }

    public object? VisitIndexExpr(IndexExpr expr)
    {
        object? obj = expr.Object.Accept(this);
        object? index = expr.Index.Accept(this);

        if (obj is List<object?> list)
        {
            if (index is not long i)
            {
                throw new RuntimeError("Array index must be an integer.", expr.BracketSpan);
            }

            if (i < 0 || i >= list.Count)
            {
                throw new RuntimeError($"Array index {i} out of bounds (length {list.Count}).", expr.BracketSpan);
            }

            return list[(int)i];
        }

        if (obj is string str)
        {
            if (index is not long i)
            {
                throw new RuntimeError("String index must be an integer.", expr.BracketSpan);
            }

            if (i < 0 || i >= str.Length)
            {
                throw new RuntimeError($"String index {i} out of bounds (length {str.Length}).", expr.BracketSpan);
            }

            return str[(int)i].ToString();
        }

        if (obj is StashDictionary dict)
        {
            if (index is null)
            {
                throw new RuntimeError("Dictionary key cannot be null.", expr.BracketSpan);
            }

            return dict.Get(index);
        }

        throw new RuntimeError("Only arrays, strings, and dictionaries can be indexed.", expr.BracketSpan);
    }

    public object? VisitIndexAssignExpr(IndexAssignExpr expr)
    {
        object? obj = expr.Object.Accept(this);
        object? index = expr.Index.Accept(this);
        object? value = expr.Value.Accept(this);

        if (obj is StashDictionary dict)
        {
            if (index is null)
            {
                throw new RuntimeError("Dictionary key cannot be null.", expr.BracketSpan);
            }

            dict.Set(index, value);
            return value;
        }

        if (obj is not List<object?> list)
        {
            throw new RuntimeError("Only arrays and dictionaries can be assigned to by index.", expr.BracketSpan);
        }

        if (index is not long i)
        {
            throw new RuntimeError("Array index must be an integer.", expr.BracketSpan);
        }

        if (i < 0 || i >= list.Count)
        {
            throw new RuntimeError($"Array index {i} out of bounds (length {list.Count}).", expr.BracketSpan);
        }

        list[(int)i] = value;
        return value;
    }

    private void DefineBuiltIns()
    {
        _globals.Define("typeof", new BuiltInFunction("typeof", 1, (_, args) =>
        {
            object? val = args[0];
            if (val is null)
            {
                return "null";
            }

            if (val is long)
            {
                return "int";
            }

            if (val is double)
            {
                return "float";
            }

            if (val is string)
            {
                return "string";
            }

            if (val is bool)
            {
                return "bool";
            }

            if (val is List<object?>)
            {
                return "array";
            }

            if (val is StashInstance)
            {
                return "struct";
            }

            if (val is StashStruct)
            {
                return "struct";
            }

            if (val is StashEnumValue)
            {
                return "enum";
            }

            if (val is StashEnum)
            {
                return "enum";
            }

            if (val is StashDictionary)
            {
                return "dict";
            }

            if (val is StashNamespace)
            {
                return "namespace";
            }

            if (val is IStashCallable)
            {
                return "function";
            }

            return "unknown";
        }));

        _globals.Define("len", new BuiltInFunction("len", 1, (_, args) =>
        {
            object? val = args[0];
            if (val is string s)
            {
                return (long)s.Length;
            }

            if (val is List<object?> list)
            {
                return (long)list.Count;
            }

            if (val is StashDictionary dict)
            {
                return (long)dict.Count;
            }

            throw new RuntimeError("Argument to 'len' must be a string, array, or dictionary.");
        }));

        _globals.Define("lastError", new BuiltInFunction("lastError", 0, (interpreter, args) =>
        {
            return interpreter._lastError;
        }));

        // Built-in structs for argument parsing
        _globals.Define("ArgTree", new StashStruct("ArgTree", new List<string>
        {
            "name", "version", "description", "flags", "options", "commands", "positionals"
        }));

        _globals.Define("ArgDef", new StashStruct("ArgDef", new List<string>
        {
            "name", "short", "type", "default", "description", "required", "args"
        }));

        // parseArgs built-in function
        _globals.Define("parseArgs", new BuiltInFunction("parseArgs", 1, (interpreter, fnArgs) =>
        {
            return interpreter.ExecuteParseArgs(fnArgs[0]);
        }));

        // ── io namespace ─────────────────────────────────────────────────
        var io = new StashNamespace("io");

        io.Define("println", new BuiltInFunction("io.println", 1, (_, args) =>
        {
            Console.WriteLine(Stringify(args[0]));
            return null;
        }));

        io.Define("print", new BuiltInFunction("io.print", 1, (_, args) =>
        {
            Console.Write(Stringify(args[0]));
            return null;
        }));

        _globals.Define("io", io);

        // ── conv namespace ───────────────────────────────────────────────
        var conv = new StashNamespace("conv");

        conv.Define("toStr", new BuiltInFunction("conv.toStr", 1, (_, args) =>
        {
            return Stringify(args[0]);
        }));

        conv.Define("toInt", new BuiltInFunction("conv.toInt", 1, (_, args) =>
        {
            object? val = args[0];
            if (val is long l)
            {
                return l;
            }

            if (val is double d)
            {
                return (long)d;
            }

            if (val is string s)
            {
                if (long.TryParse(s, out long result))
                {
                    return result;
                }

                throw new RuntimeError($"Cannot parse '{s}' as integer.");
            }
            throw new RuntimeError("Argument to 'conv.toInt' must be a number or string.");
        }));

        conv.Define("toFloat", new BuiltInFunction("conv.toFloat", 1, (_, args) =>
        {
            object? val = args[0];
            if (val is double d)
            {
                return d;
            }

            if (val is long l)
            {
                return (double)l;
            }

            if (val is string s)
            {
                if (double.TryParse(s, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double result))
                {
                    return result;
                }

                throw new RuntimeError($"Cannot parse '{s}' as float.");
            }
            throw new RuntimeError("Argument to 'conv.toFloat' must be a number or string.");
        }));

        _globals.Define("conv", conv);

        // ── env namespace ────────────────────────────────────────────────
        var envNs = new StashNamespace("env");

        envNs.Define("get", new BuiltInFunction("env.get", 1, (_, args) =>
        {
            if (args[0] is not string name)
            {
                throw new RuntimeError("Argument to 'env.get' must be a string.");
            }

            return System.Environment.GetEnvironmentVariable(name);
        }));

        envNs.Define("set", new BuiltInFunction("env.set", 2, (_, args) =>
        {
            if (args[0] is not string name)
            {
                throw new RuntimeError("First argument to 'env.set' must be a string.");
            }

            if (args[1] is not string value)
            {
                throw new RuntimeError("Second argument to 'env.set' must be a string.");
            }

            System.Environment.SetEnvironmentVariable(name, value);
            return null;
        }));

        _globals.Define("env", envNs);

        // ── process namespace ────────────────────────────────────────────
        var process = new StashNamespace("process");

        // Signal constants
        process.Define("SIGHUP", (long)1);
        process.Define("SIGINT", (long)2);
        process.Define("SIGQUIT", (long)3);
        process.Define("SIGKILL", (long)9);
        process.Define("SIGUSR1", (long)10);
        process.Define("SIGUSR2", (long)12);
        process.Define("SIGTERM", (long)15);

        process.Define("exit", new BuiltInFunction("process.exit", 1, (interp, args) =>
        {
            if (args[0] is not long code)
            {
                throw new RuntimeError("Argument to 'process.exit' must be an integer.");
            }

            interp.CleanupTrackedProcesses();
            System.Environment.Exit((int)code);
            return null;
        }));

        process.Define("spawn", new BuiltInFunction("process.spawn", 1, (interp, args) =>
        {
            if (args[0] is not string command)
            {
                throw new RuntimeError("Argument to 'process.spawn' must be a string.");
            }

            var psi = new ProcessStartInfo
            {
                FileName = "/bin/sh",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(command);

            var osProcess = System.Diagnostics.Process.Start(psi);
            if (osProcess is null)
            {
                throw new RuntimeError("Failed to start process.");
            }

            var fields = new Dictionary<string, object?>
            {
                ["pid"] = (long)osProcess.Id,
                ["command"] = command
            };
            var handle = new StashInstance("Process", fields);
            interp._trackedProcesses.Add((handle, osProcess));
            return handle;
        }));

        process.Define("wait", new BuiltInFunction("process.wait", 1, (interp, args) =>
        {
            if (args[0] is not StashInstance handle || handle.TypeName != "Process")
            {
                throw new RuntimeError("Argument to 'process.wait' must be a Process handle.");
            }

            var entry = interp._trackedProcesses.Find(e => ReferenceEquals(e.Handle, handle));
            if (entry.OsProcess is null)
            {
                // Already waited — return cached result if available
                if (interp._processWaitCache.TryGetValue(handle, out var cached))
                {
                    return cached;
                }

                return new StashInstance("CommandResult", new Dictionary<string, object?>
                {
                    ["stdout"] = "",
                    ["stderr"] = "",
                    ["exitCode"] = (long)-1
                });
            }

            var osProcess = entry.OsProcess;
            var stdoutTask = Task.Run(() => osProcess.StandardOutput.ReadToEnd());
            var stderrTask = Task.Run(() => osProcess.StandardError.ReadToEnd());
            osProcess.WaitForExit();
            Task.WaitAll(stdoutTask, stderrTask);

            var result = new StashInstance("CommandResult", new Dictionary<string, object?>
            {
                ["stdout"] = stdoutTask.Result,
                ["stderr"] = stderrTask.Result,
                ["exitCode"] = (long)osProcess.ExitCode
            });

            // Cache the result so subsequent wait() calls return the same data
            interp._processWaitCache[handle] = result;
            return result;
        }));

        process.Define("waitTimeout", new BuiltInFunction("process.waitTimeout", 2, (interp, args) =>
        {
            if (args[0] is not StashInstance handle || handle.TypeName != "Process")
            {
                throw new RuntimeError("First argument to 'process.waitTimeout' must be a Process handle.");
            }

            if (args[1] is not long ms)
            {
                throw new RuntimeError("Second argument to 'process.waitTimeout' must be an integer (milliseconds).");
            }

            var entry = interp._trackedProcesses.Find(e => ReferenceEquals(e.Handle, handle));
            if (entry.OsProcess is null)
            {
                return null;
            }

            var osProcess = entry.OsProcess;
            if (!osProcess.WaitForExit((int)ms))
            {
                return null; // timed out
            }

            var stdoutTask = Task.Run(() => osProcess.StandardOutput.ReadToEnd());
            var stderrTask = Task.Run(() => osProcess.StandardError.ReadToEnd());
            Task.WaitAll(stdoutTask, stderrTask);

            return new StashInstance("CommandResult", new Dictionary<string, object?>
            {
                ["stdout"] = stdoutTask.Result,
                ["stderr"] = stderrTask.Result,
                ["exitCode"] = (long)osProcess.ExitCode
            });
        }));

        process.Define("kill", new BuiltInFunction("process.kill", 1, (interp, args) =>
        {
            if (args[0] is not StashInstance handle || handle.TypeName != "Process")
            {
                throw new RuntimeError("Argument to 'process.kill' must be a Process handle.");
            }

            var entry = interp._trackedProcesses.Find(e => ReferenceEquals(e.Handle, handle));
            if (entry.OsProcess is null || entry.OsProcess.HasExited)
            {
                return false;
            }

            try
            {
                entry.OsProcess.Kill(false); // SIGKILL on Linux
                return true;
            }
            catch
            {
                return false;
            }
        }));

        process.Define("isAlive", new BuiltInFunction("process.isAlive", 1, (interp, args) =>
        {
            if (args[0] is not StashInstance handle || handle.TypeName != "Process")
            {
                throw new RuntimeError("Argument to 'process.isAlive' must be a Process handle.");
            }

            var entry = interp._trackedProcesses.Find(e => ReferenceEquals(e.Handle, handle));
            if (entry.OsProcess is null)
            {
                return false;
            }

            try { return !entry.OsProcess.HasExited; }
            catch { return false; }
        }));

        process.Define("pid", new BuiltInFunction("process.pid", 1, (interp, args) =>
        {
            if (args[0] is not StashInstance handle || handle.TypeName != "Process")
            {
                throw new RuntimeError("Argument to 'process.pid' must be a Process handle.");
            }

            return handle.GetField("pid", null);
        }));

        process.Define("signal", new BuiltInFunction("process.signal", 2, (interp, args) =>
        {
            if (args[0] is not StashInstance handle || handle.TypeName != "Process")
            {
                throw new RuntimeError("First argument to 'process.signal' must be a Process handle.");
            }

            if (args[1] is not long sig)
            {
                throw new RuntimeError("Second argument to 'process.signal' must be an integer (signal number).");
            }

            if (sig < 1 || sig > 64)
            {
                throw new RuntimeError($"Signal number must be between 1 and 64, got {sig}.");
            }

            var entry = interp._trackedProcesses.Find(e => ReferenceEquals(e.Handle, handle));
            if (entry.OsProcess is null || entry.OsProcess.HasExited)
            {
                return false;
            }

            try
            {
                // Use kill command for arbitrary signals since .NET only supports SIGTERM/SIGKILL directly
                var killPsi = new ProcessStartInfo
                {
                    FileName = "/bin/kill",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                killPsi.ArgumentList.Add($"-{sig}");
                killPsi.ArgumentList.Add(entry.OsProcess.Id.ToString());

                using var killProc = System.Diagnostics.Process.Start(killPsi);
                killProc?.WaitForExit();
                return killProc?.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }));

        process.Define("detach", new BuiltInFunction("process.detach", 1, (interp, args) =>
        {
            if (args[0] is not StashInstance handle || handle.TypeName != "Process")
            {
                throw new RuntimeError("Argument to 'process.detach' must be a Process handle.");
            }

            int idx = interp._trackedProcesses.FindIndex(e => ReferenceEquals(e.Handle, handle));
            if (idx >= 0)
            {
                interp._trackedProcesses.RemoveAt(idx);
                return true;
            }

            return false;
        }));

        process.Define("list", new BuiltInFunction("process.list", 0, (interp, _) =>
        {
            var result = new List<object?>();
            foreach (var (handle, _) in interp._trackedProcesses)
            {
                result.Add(handle);
            }
            return result;
        }));

        process.Define("read", new BuiltInFunction("process.read", 1, (interp, args) =>
        {
            if (args[0] is not StashInstance handle || handle.TypeName != "Process")
            {
                throw new RuntimeError("Argument to 'process.read' must be a Process handle.");
            }

            var entry = interp._trackedProcesses.Find(e => ReferenceEquals(e.Handle, handle));
            if (entry.OsProcess is null)
            {
                return null;
            }

            try
            {
                var stream = entry.OsProcess.StandardOutput;
                if (stream.Peek() == -1)
                {
                    return null;
                }

                var buffer = new char[4096];
                int read = stream.Read(buffer, 0, buffer.Length);
                return read > 0 ? new string(buffer, 0, read) : null;
            }
            catch
            {
                return null;
            }
        }));

        process.Define("write", new BuiltInFunction("process.write", 2, (interp, args) =>
        {
            if (args[0] is not StashInstance handle || handle.TypeName != "Process")
            {
                throw new RuntimeError("First argument to 'process.write' must be a Process handle.");
            }

            if (args[1] is not string data)
            {
                throw new RuntimeError("Second argument to 'process.write' must be a string.");
            }

            var entry = interp._trackedProcesses.Find(e => ReferenceEquals(e.Handle, handle));
            if (entry.OsProcess is null || entry.OsProcess.HasExited)
            {
                return false;
            }

            try
            {
                entry.OsProcess.StandardInput.Write(data);
                entry.OsProcess.StandardInput.Flush();
                return true;
            }
            catch
            {
                return false;
            }
        }));

        _globals.Define("process", process);

        // ── fs namespace ─────────────────────────────────────────────────
        var fs = new StashNamespace("fs");

        fs.Define("readFile", new BuiltInFunction("fs.readFile", 1, (_, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.readFile' must be a string.");
            }

            try { return System.IO.File.ReadAllText(path); }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot read file '{path}': {e.Message}"); }
        }));

        fs.Define("writeFile", new BuiltInFunction("fs.writeFile", 2, (_, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("First argument to 'fs.writeFile' must be a string.");
            }

            if (args[1] is not string content)
            {
                throw new RuntimeError("Second argument to 'fs.writeFile' must be a string.");
            }

            try { System.IO.File.WriteAllText(path, content); }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot write file '{path}': {e.Message}"); }
            return null;
        }));

        fs.Define("exists", new BuiltInFunction("fs.exists", 1, (_, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.exists' must be a string.");
            }

            return System.IO.File.Exists(path);
        }));

        fs.Define("dirExists", new BuiltInFunction("fs.dirExists", 1, (_, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.dirExists' must be a string.");
            }

            return System.IO.Directory.Exists(path);
        }));

        fs.Define("pathExists", new BuiltInFunction("fs.pathExists", 1, (_, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.pathExists' must be a string.");
            }

            return System.IO.File.Exists(path) || System.IO.Directory.Exists(path);
        }));

        fs.Define("createDir", new BuiltInFunction("fs.createDir", 1, (_, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.createDir' must be a string.");
            }

            try { System.IO.Directory.CreateDirectory(path); }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot create directory '{path}': {e.Message}"); }
            return null;
        }));

        fs.Define("delete", new BuiltInFunction("fs.delete", 1, (_, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.delete' must be a string.");
            }

            try
            {
                if (System.IO.File.Exists(path))
                {
                    System.IO.File.Delete(path);
                }
                else if (System.IO.Directory.Exists(path))
                {
                    System.IO.Directory.Delete(path, true);
                }
                else
                {
                    throw new RuntimeError($"Path does not exist: '{path}'.");
                }
            }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot delete '{path}': {e.Message}"); }
            return null;
        }));

        fs.Define("copy", new BuiltInFunction("fs.copy", 2, (_, args) =>
        {
            if (args[0] is not string src)
            {
                throw new RuntimeError("First argument to 'fs.copy' must be a string.");
            }

            if (args[1] is not string dst)
            {
                throw new RuntimeError("Second argument to 'fs.copy' must be a string.");
            }

            try { System.IO.File.Copy(src, dst, overwrite: true); }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot copy '{src}' to '{dst}': {e.Message}"); }
            return null;
        }));

        fs.Define("move", new BuiltInFunction("fs.move", 2, (_, args) =>
        {
            if (args[0] is not string src)
            {
                throw new RuntimeError("First argument to 'fs.move' must be a string.");
            }

            if (args[1] is not string dst)
            {
                throw new RuntimeError("Second argument to 'fs.move' must be a string.");
            }

            try { System.IO.File.Move(src, dst, overwrite: true); }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot move '{src}' to '{dst}': {e.Message}"); }
            return null;
        }));

        fs.Define("size", new BuiltInFunction("fs.size", 1, (_, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.size' must be a string.");
            }

            try { return new System.IO.FileInfo(path).Length; }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot get size of '{path}': {e.Message}"); }
        }));

        fs.Define("listDir", new BuiltInFunction("fs.listDir", 1, (_, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("Argument to 'fs.listDir' must be a string.");
            }

            try
            {
                var entries = System.IO.Directory.GetFileSystemEntries(path);
                var result = new List<object?>();
                foreach (var entry in entries)
                {
                    result.Add(entry);
                }

                return result;
            }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot list directory '{path}': {e.Message}"); }
        }));

        fs.Define("appendFile", new BuiltInFunction("fs.appendFile", 2, (_, args) =>
        {
            if (args[0] is not string path)
            {
                throw new RuntimeError("First argument to 'fs.appendFile' must be a string.");
            }

            if (args[1] is not string content)
            {
                throw new RuntimeError("Second argument to 'fs.appendFile' must be a string.");
            }

            try { System.IO.File.AppendAllText(path, content); }
            catch (System.IO.IOException e) { throw new RuntimeError($"Cannot append to file '{path}': {e.Message}"); }
            return null;
        }));

        _globals.Define("fs", fs);

        // ── path namespace ───────────────────────────────────────────────
        var pathNs = new StashNamespace("path");

        pathNs.Define("abs", new BuiltInFunction("path.abs", 1, (_, args) =>
        {
            if (args[0] is not string p)
            {
                throw new RuntimeError("Argument to 'path.abs' must be a string.");
            }

            return System.IO.Path.GetFullPath(p);
        }));

        pathNs.Define("dir", new BuiltInFunction("path.dir", 1, (_, args) =>
        {
            if (args[0] is not string p)
            {
                throw new RuntimeError("Argument to 'path.dir' must be a string.");
            }

            return System.IO.Path.GetDirectoryName(p) ?? "";
        }));

        pathNs.Define("base", new BuiltInFunction("path.base", 1, (_, args) =>
        {
            if (args[0] is not string p)
            {
                throw new RuntimeError("Argument to 'path.base' must be a string.");
            }

            return System.IO.Path.GetFileName(p);
        }));

        pathNs.Define("ext", new BuiltInFunction("path.ext", 1, (_, args) =>
        {
            if (args[0] is not string p)
            {
                throw new RuntimeError("Argument to 'path.ext' must be a string.");
            }

            return System.IO.Path.GetExtension(p);
        }));

        pathNs.Define("join", new BuiltInFunction("path.join", 2, (_, args) =>
        {
            if (args[0] is not string a)
            {
                throw new RuntimeError("First argument to 'path.join' must be a string.");
            }

            if (args[1] is not string b)
            {
                throw new RuntimeError("Second argument to 'path.join' must be a string.");
            }

            return System.IO.Path.Combine(a, b);
        }));

        pathNs.Define("name", new BuiltInFunction("path.name", 1, (_, args) =>
        {
            if (args[0] is not string p)
            {
                throw new RuntimeError("Argument to 'path.name' must be a string.");
            }

            return System.IO.Path.GetFileNameWithoutExtension(p);
        }));

        _globals.Define("path", pathNs);

        // ── arr namespace ────────────────────────────────────────────────
        var arr = new StashNamespace("arr");

        arr.Define("push", new BuiltInFunction("arr.push", 2, (_, args) =>
        {
            if (args[0] is not List<object?> list)
                throw new RuntimeError("First argument to 'arr.push' must be an array.");
            list.Add(args[1]);
            return null;
        }));

        arr.Define("pop", new BuiltInFunction("arr.pop", 1, (_, args) =>
        {
            if (args[0] is not List<object?> list)
                throw new RuntimeError("First argument to 'arr.pop' must be an array.");
            if (list.Count == 0)
                throw new RuntimeError("Cannot pop from an empty array.");
            var last = list[list.Count - 1];
            list.RemoveAt(list.Count - 1);
            return last;
        }));

        arr.Define("peek", new BuiltInFunction("arr.peek", 1, (_, args) =>
        {
            if (args[0] is not List<object?> list)
                throw new RuntimeError("First argument to 'arr.peek' must be an array.");
            if (list.Count == 0)
                throw new RuntimeError("Cannot peek an empty array.");
            return list[list.Count - 1];
        }));

        arr.Define("insert", new BuiltInFunction("arr.insert", 3, (_, args) =>
        {
            if (args[0] is not List<object?> list)
                throw new RuntimeError("First argument to 'arr.insert' must be an array.");
            if (args[1] is not long idx)
                throw new RuntimeError("Second argument to 'arr.insert' must be an integer.");
            if (idx < 0 || idx > list.Count)
                throw new RuntimeError($"Index {idx} is out of bounds for 'arr.insert'.");
            list.Insert((int)idx, args[2]);
            return null;
        }));

        arr.Define("removeAt", new BuiltInFunction("arr.removeAt", 2, (_, args) =>
        {
            if (args[0] is not List<object?> list)
                throw new RuntimeError("First argument to 'arr.removeAt' must be an array.");
            if (args[1] is not long idx)
                throw new RuntimeError("Second argument to 'arr.removeAt' must be an integer.");
            if (idx < 0 || idx >= list.Count)
                throw new RuntimeError($"Index {idx} is out of bounds for 'arr.removeAt'.");
            var removed = list[(int)idx];
            list.RemoveAt((int)idx);
            return removed;
        }));

        arr.Define("remove", new BuiltInFunction("arr.remove", 2, (_, args) =>
        {
            if (args[0] is not List<object?> list)
                throw new RuntimeError("First argument to 'arr.remove' must be an array.");
            for (int i = 0; i < list.Count; i++)
            {
                if (IsEqual(list[i], args[1]))
                {
                    list.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }));

        arr.Define("clear", new BuiltInFunction("arr.clear", 1, (_, args) =>
        {
            if (args[0] is not List<object?> list)
                throw new RuntimeError("First argument to 'arr.clear' must be an array.");
            list.Clear();
            return null;
        }));

        arr.Define("contains", new BuiltInFunction("arr.contains", 2, (_, args) =>
        {
            if (args[0] is not List<object?> list)
                throw new RuntimeError("First argument to 'arr.contains' must be an array.");
            foreach (var item in list)
            {
                if (IsEqual(item, args[1]))
                    return true;
            }
            return false;
        }));

        arr.Define("indexOf", new BuiltInFunction("arr.indexOf", 2, (_, args) =>
        {
            if (args[0] is not List<object?> list)
                throw new RuntimeError("First argument to 'arr.indexOf' must be an array.");
            for (int i = 0; i < list.Count; i++)
            {
                if (IsEqual(list[i], args[1]))
                    return (long)i;
            }
            return -1L;
        }));

        arr.Define("slice", new BuiltInFunction("arr.slice", 3, (_, args) =>
        {
            if (args[0] is not List<object?> list)
                throw new RuntimeError("First argument to 'arr.slice' must be an array.");
            if (args[1] is not long start)
                throw new RuntimeError("Second argument to 'arr.slice' must be an integer.");
            if (args[2] is not long end)
                throw new RuntimeError("Third argument to 'arr.slice' must be an integer.");
            int s = (int)Math.Max(0, Math.Min(start, list.Count));
            int e = (int)Math.Max(0, Math.Min(end, list.Count));
            if (e < s) e = s;
            return list.GetRange(s, e - s);
        }));

        arr.Define("concat", new BuiltInFunction("arr.concat", 2, (_, args) =>
        {
            if (args[0] is not List<object?> list1)
                throw new RuntimeError("First argument to 'arr.concat' must be an array.");
            if (args[1] is not List<object?> list2)
                throw new RuntimeError("Second argument to 'arr.concat' must be an array.");
            var result = new List<object?>(list1);
            result.AddRange(list2);
            return result;
        }));

        arr.Define("join", new BuiltInFunction("arr.join", 2, (_, args) =>
        {
            if (args[0] is not List<object?> list)
                throw new RuntimeError("First argument to 'arr.join' must be an array.");
            if (args[1] is not string sep)
                throw new RuntimeError("Second argument to 'arr.join' must be a string.");
            var parts = new string[list.Count];
            for (int i = 0; i < list.Count; i++) parts[i] = Stringify(list[i]);
            return string.Join(sep, parts);
        }));

        arr.Define("reverse", new BuiltInFunction("arr.reverse", 1, (_, args) =>
        {
            if (args[0] is not List<object?> list)
                throw new RuntimeError("First argument to 'arr.reverse' must be an array.");
            list.Reverse();
            return null;
        }));

        arr.Define("sort", new BuiltInFunction("arr.sort", 1, (_, args) =>
        {
            if (args[0] is not List<object?> list)
                throw new RuntimeError("First argument to 'arr.sort' must be an array.");
            try
            {
                list.Sort((a, b) =>
                {
                    if (a is long la && b is long lb) return la.CompareTo(lb);
                    if (a is double da && b is double db) return da.CompareTo(db);
                    if (a is long la2 && b is double db2) return ((double)la2).CompareTo(db2);
                    if (a is double da2 && b is long lb2) return da2.CompareTo((double)lb2);
                    if (a is string sa && b is string sb) return string.Compare(sa, sb, StringComparison.Ordinal);
                    throw new RuntimeError("Cannot compare values of incompatible types in 'arr.sort'.");
                });
            }
            catch (InvalidOperationException ex) when (ex.InnerException is RuntimeError re)
            {
                throw re;
            }
            return null;
        }));

        arr.Define("map", new BuiltInFunction("arr.map", 2, (interp, args) =>
        {
            if (args[0] is not List<object?> list)
                throw new RuntimeError("First argument to 'arr.map' must be an array.");
            if (args[1] is not IStashCallable fn)
                throw new RuntimeError("Second argument to 'arr.map' must be a function.");
            var result = new List<object?>();
            foreach (var item in list)
                result.Add(fn.Call(interp, new List<object?> { item }));
            return result;
        }));

        arr.Define("filter", new BuiltInFunction("arr.filter", 2, (interp, args) =>
        {
            if (args[0] is not List<object?> list)
                throw new RuntimeError("First argument to 'arr.filter' must be an array.");
            if (args[1] is not IStashCallable fn)
                throw new RuntimeError("Second argument to 'arr.filter' must be a function.");
            var result = new List<object?>();
            foreach (var item in list)
            {
                if (IsTruthy(fn.Call(interp, new List<object?> { item })))
                    result.Add(item);
            }
            return result;
        }));

        arr.Define("forEach", new BuiltInFunction("arr.forEach", 2, (interp, args) =>
        {
            if (args[0] is not List<object?> list)
                throw new RuntimeError("First argument to 'arr.forEach' must be an array.");
            if (args[1] is not IStashCallable fn)
                throw new RuntimeError("Second argument to 'arr.forEach' must be a function.");
            foreach (var item in list)
                fn.Call(interp, new List<object?> { item });
            return null;
        }));

        arr.Define("find", new BuiltInFunction("arr.find", 2, (interp, args) =>
        {
            if (args[0] is not List<object?> list)
                throw new RuntimeError("First argument to 'arr.find' must be an array.");
            if (args[1] is not IStashCallable fn)
                throw new RuntimeError("Second argument to 'arr.find' must be a function.");
            foreach (var item in list)
            {
                if (IsTruthy(fn.Call(interp, new List<object?> { item })))
                    return item;
            }
            return null;
        }));

        arr.Define("reduce", new BuiltInFunction("arr.reduce", 3, (interp, args) =>
        {
            if (args[0] is not List<object?> list)
                throw new RuntimeError("First argument to 'arr.reduce' must be an array.");
            if (args[1] is not IStashCallable fn)
                throw new RuntimeError("Second argument to 'arr.reduce' must be a function.");
            var accumulator = args[2];
            foreach (var item in list)
                accumulator = fn.Call(interp, new List<object?> { accumulator, item });
            return accumulator;
        }));

        _globals.Define("arr", arr);

        // ── dict namespace ───────────────────────────────────────────────
        var dict = new StashNamespace("dict");

        dict.Define("new", new BuiltInFunction("dict.new", 0, (_, args) =>
        {
            return new StashDictionary();
        }));

        dict.Define("get", new BuiltInFunction("dict.get", 2, (_, args) =>
        {
            if (args[0] is not StashDictionary d)
                throw new RuntimeError("First argument to 'dict.get' must be a dictionary.");
            if (args[1] is null)
                throw new RuntimeError("Dictionary key cannot be null.");
            return d.Get(args[1]);
        }));

        dict.Define("set", new BuiltInFunction("dict.set", 3, (_, args) =>
        {
            if (args[0] is not StashDictionary d)
                throw new RuntimeError("First argument to 'dict.set' must be a dictionary.");
            if (args[1] is null)
                throw new RuntimeError("Dictionary key cannot be null.");
            d.Set(args[1], args[2]);
            return null;
        }));

        dict.Define("has", new BuiltInFunction("dict.has", 2, (_, args) =>
        {
            if (args[0] is not StashDictionary d)
                throw new RuntimeError("First argument to 'dict.has' must be a dictionary.");
            if (args[1] is null)
                throw new RuntimeError("Dictionary key cannot be null.");
            return d.Has(args[1]);
        }));

        dict.Define("remove", new BuiltInFunction("dict.remove", 2, (_, args) =>
        {
            if (args[0] is not StashDictionary d)
                throw new RuntimeError("First argument to 'dict.remove' must be a dictionary.");
            if (args[1] is null)
                throw new RuntimeError("Dictionary key cannot be null.");
            return d.Remove(args[1]);
        }));

        dict.Define("clear", new BuiltInFunction("dict.clear", 1, (_, args) =>
        {
            if (args[0] is not StashDictionary d)
                throw new RuntimeError("First argument to 'dict.clear' must be a dictionary.");
            d.Clear();
            return null;
        }));

        dict.Define("keys", new BuiltInFunction("dict.keys", 1, (_, args) =>
        {
            if (args[0] is not StashDictionary d)
                throw new RuntimeError("First argument to 'dict.keys' must be a dictionary.");
            return d.Keys();
        }));

        dict.Define("values", new BuiltInFunction("dict.values", 1, (_, args) =>
        {
            if (args[0] is not StashDictionary d)
                throw new RuntimeError("First argument to 'dict.values' must be a dictionary.");
            return d.Values();
        }));

        dict.Define("size", new BuiltInFunction("dict.size", 1, (_, args) =>
        {
            if (args[0] is not StashDictionary d)
                throw new RuntimeError("First argument to 'dict.size' must be a dictionary.");
            return (long)d.Count;
        }));

        dict.Define("pairs", new BuiltInFunction("dict.pairs", 1, (_, args) =>
        {
            if (args[0] is not StashDictionary d)
                throw new RuntimeError("First argument to 'dict.pairs' must be a dictionary.");
            return d.Pairs();
        }));

        dict.Define("forEach", new BuiltInFunction("dict.forEach", 2, (interp, args) =>
        {
            if (args[0] is not StashDictionary d)
                throw new RuntimeError("First argument to 'dict.forEach' must be a dictionary.");
            if (args[1] is not IStashCallable fn)
                throw new RuntimeError("Second argument to 'dict.forEach' must be a function.");
            foreach (var entry in d.RawEntries())
            {
                fn.Call(interp, new List<object?> { entry.Key, entry.Value });
            }
            return null;
        }));

        dict.Define("merge", new BuiltInFunction("dict.merge", 2, (_, args) =>
        {
            if (args[0] is not StashDictionary d1)
                throw new RuntimeError("First argument to 'dict.merge' must be a dictionary.");
            if (args[1] is not StashDictionary d2)
                throw new RuntimeError("Second argument to 'dict.merge' must be a dictionary.");
            var result = new StashDictionary();
            foreach (var entry in d1.RawEntries())
            {
                result.Set(entry.Key, entry.Value);
            }
            foreach (var entry in d2.RawEntries())
            {
                result.Set(entry.Key, entry.Value);
            }
            return result;
        }));

        _globals.Define("dict", dict);

        // ── str namespace ────────────────────────────────────────────────
        var str = new StashNamespace("str");

        str.Define("upper", new BuiltInFunction("str.upper", 1, (_, args) =>
        {
            if (args[0] is not string s)
                throw new RuntimeError("First argument to 'str.upper' must be a string.");
            return s.ToUpperInvariant();
        }));

        str.Define("lower", new BuiltInFunction("str.lower", 1, (_, args) =>
        {
            if (args[0] is not string s)
                throw new RuntimeError("First argument to 'str.lower' must be a string.");
            return s.ToLowerInvariant();
        }));

        str.Define("trim", new BuiltInFunction("str.trim", 1, (_, args) =>
        {
            if (args[0] is not string s)
                throw new RuntimeError("First argument to 'str.trim' must be a string.");
            return s.Trim();
        }));

        str.Define("trimStart", new BuiltInFunction("str.trimStart", 1, (_, args) =>
        {
            if (args[0] is not string s)
                throw new RuntimeError("First argument to 'str.trimStart' must be a string.");
            return s.TrimStart();
        }));

        str.Define("trimEnd", new BuiltInFunction("str.trimEnd", 1, (_, args) =>
        {
            if (args[0] is not string s)
                throw new RuntimeError("First argument to 'str.trimEnd' must be a string.");
            return s.TrimEnd();
        }));

        str.Define("contains", new BuiltInFunction("str.contains", 2, (_, args) =>
        {
            if (args[0] is not string s)
                throw new RuntimeError("First argument to 'str.contains' must be a string.");
            if (args[1] is not string sub)
                throw new RuntimeError("Second argument to 'str.contains' must be a string.");
            return s.Contains(sub, StringComparison.Ordinal);
        }));

        str.Define("startsWith", new BuiltInFunction("str.startsWith", 2, (_, args) =>
        {
            if (args[0] is not string s)
                throw new RuntimeError("First argument to 'str.startsWith' must be a string.");
            if (args[1] is not string prefix)
                throw new RuntimeError("Second argument to 'str.startsWith' must be a string.");
            return s.StartsWith(prefix, StringComparison.Ordinal);
        }));

        str.Define("endsWith", new BuiltInFunction("str.endsWith", 2, (_, args) =>
        {
            if (args[0] is not string s)
                throw new RuntimeError("First argument to 'str.endsWith' must be a string.");
            if (args[1] is not string suffix)
                throw new RuntimeError("Second argument to 'str.endsWith' must be a string.");
            return s.EndsWith(suffix, StringComparison.Ordinal);
        }));

        str.Define("indexOf", new BuiltInFunction("str.indexOf", 2, (_, args) =>
        {
            if (args[0] is not string s)
                throw new RuntimeError("First argument to 'str.indexOf' must be a string.");
            if (args[1] is not string sub)
                throw new RuntimeError("Second argument to 'str.indexOf' must be a string.");
            return (long)s.IndexOf(sub, StringComparison.Ordinal);
        }));

        str.Define("lastIndexOf", new BuiltInFunction("str.lastIndexOf", 2, (_, args) =>
        {
            if (args[0] is not string s)
                throw new RuntimeError("First argument to 'str.lastIndexOf' must be a string.");
            if (args[1] is not string sub)
                throw new RuntimeError("Second argument to 'str.lastIndexOf' must be a string.");
            return (long)s.LastIndexOf(sub, StringComparison.Ordinal);
        }));

        str.Define("substring", new BuiltInFunction("str.substring", -1, (_, args) =>
        {
            if (args.Count < 2 || args.Count > 3)
                throw new RuntimeError("'str.substring' requires 2 or 3 arguments.");
            if (args[0] is not string s)
                throw new RuntimeError("First argument to 'str.substring' must be a string.");
            if (args[1] is not long start)
                throw new RuntimeError("Second argument to 'str.substring' must be an integer.");
            if (start < 0 || start > s.Length)
                throw new RuntimeError($"'str.substring' start index {start} is out of range for string of length {s.Length}.");
            if (args.Count == 3)
            {
                if (args[2] is not long end)
                    throw new RuntimeError("Third argument to 'str.substring' must be an integer.");
                if (end < start || end > s.Length)
                    throw new RuntimeError($"'str.substring' end index {end} is out of range.");
                return s.Substring((int)start, (int)(end - start));
            }
            return s.Substring((int)start);
        }));

        str.Define("replace", new BuiltInFunction("str.replace", 3, (_, args) =>
        {
            if (args[0] is not string s)
                throw new RuntimeError("First argument to 'str.replace' must be a string.");
            if (args[1] is not string oldStr)
                throw new RuntimeError("Second argument to 'str.replace' must be a string.");
            if (args[2] is not string newStr)
                throw new RuntimeError("Third argument to 'str.replace' must be a string.");
            int idx = s.IndexOf(oldStr, StringComparison.Ordinal);
            if (idx < 0) return s;
            return s.Substring(0, idx) + newStr + s.Substring(idx + oldStr.Length);
        }));

        str.Define("replaceAll", new BuiltInFunction("str.replaceAll", 3, (_, args) =>
        {
            if (args[0] is not string s)
                throw new RuntimeError("First argument to 'str.replaceAll' must be a string.");
            if (args[1] is not string oldStr)
                throw new RuntimeError("Second argument to 'str.replaceAll' must be a string.");
            if (args[2] is not string newStr)
                throw new RuntimeError("Third argument to 'str.replaceAll' must be a string.");
            return s.Replace(oldStr, newStr);
        }));

        str.Define("split", new BuiltInFunction("str.split", 2, (_, args) =>
        {
            if (args[0] is not string s)
                throw new RuntimeError("First argument to 'str.split' must be a string.");
            if (args[1] is not string delimiter)
                throw new RuntimeError("Second argument to 'str.split' must be a string.");
            var parts = s.Split(new[] { delimiter }, StringSplitOptions.None);
            return parts.Select(p => (object?)p).ToList();
        }));

        str.Define("repeat", new BuiltInFunction("str.repeat", 2, (_, args) =>
        {
            if (args[0] is not string s)
                throw new RuntimeError("First argument to 'str.repeat' must be a string.");
            if (args[1] is not long count)
                throw new RuntimeError("Second argument to 'str.repeat' must be an integer.");
            if (count < 0)
                throw new RuntimeError("'str.repeat' count must be >= 0.");
            return string.Concat(Enumerable.Repeat(s, (int)count));
        }));

        str.Define("reverse", new BuiltInFunction("str.reverse", 1, (_, args) =>
        {
            if (args[0] is not string s)
                throw new RuntimeError("First argument to 'str.reverse' must be a string.");
            return new string(s.Reverse().ToArray());
        }));

        str.Define("chars", new BuiltInFunction("str.chars", 1, (_, args) =>
        {
            if (args[0] is not string s)
                throw new RuntimeError("First argument to 'str.chars' must be a string.");
            return s.Select(c => (object?)c.ToString()).ToList();
        }));

        str.Define("padStart", new BuiltInFunction("str.padStart", -1, (_, args) =>
        {
            if (args.Count < 2 || args.Count > 3)
                throw new RuntimeError("'str.padStart' requires 2 or 3 arguments.");
            if (args[0] is not string s)
                throw new RuntimeError("First argument to 'str.padStart' must be a string.");
            if (args[1] is not long length)
                throw new RuntimeError("Second argument to 'str.padStart' must be an integer.");
            char fillChar = ' ';
            if (args.Count == 3)
            {
                if (args[2] is not string fill || fill.Length != 1)
                    throw new RuntimeError("Third argument to 'str.padStart' must be a single-character string.");
                fillChar = fill[0];
            }
            return s.PadLeft((int)length, fillChar);
        }));

        str.Define("padEnd", new BuiltInFunction("str.padEnd", -1, (_, args) =>
        {
            if (args.Count < 2 || args.Count > 3)
                throw new RuntimeError("'str.padEnd' requires 2 or 3 arguments.");
            if (args[0] is not string s)
                throw new RuntimeError("First argument to 'str.padEnd' must be a string.");
            if (args[1] is not long length)
                throw new RuntimeError("Second argument to 'str.padEnd' must be an integer.");
            char fillChar = ' ';
            if (args.Count == 3)
            {
                if (args[2] is not string fill || fill.Length != 1)
                    throw new RuntimeError("Third argument to 'str.padEnd' must be a single-character string.");
                fillChar = fill[0];
            }
            return s.PadRight((int)length, fillChar);
        }));

        _globals.Define("str", str);
    }

    /// <summary>
    /// Cleans up all tracked processes on script exit.
    /// Sends SIGTERM, waits up to 3 seconds, then SIGKILL.
    /// </summary>
    public void CleanupTrackedProcesses()
    {
        foreach (var (_, osProcess) in _trackedProcesses)
        {
            try
            {
                if (!osProcess.HasExited)
                {
                    osProcess.Kill(false); // SIGTERM on Linux
                    if (!osProcess.WaitForExit(3000))
                    {
                        osProcess.Kill(true); // SIGKILL
                    }
                }
            }
            catch { /* Process may have already exited */ }
        }
        _trackedProcesses.Clear();
    }

    private class BuiltInFunction : IStashCallable
    {
        private readonly string _name;
        private readonly Func<Interpreter, List<object?>, object?> _body;

        public int Arity { get; }

        public BuiltInFunction(string name, int arity, Func<Interpreter, List<object?>, object?> body)
        {
            _name = name;
            Arity = arity;
            _body = body;
        }

        public object? Call(Interpreter interpreter, List<object?> arguments)
        {
            return _body(interpreter, arguments);
        }

        public override string ToString() => $"<built-in fn {_name}>";
    }
}
