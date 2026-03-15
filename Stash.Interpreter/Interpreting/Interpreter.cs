namespace Stash.Interpreting;

using System;
using Stash.Interpreting.BuiltIns;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Stash.Common;
using Stash.Debugging;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Interpreting.Types;

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
    internal string? LastError;
    private readonly Dictionary<string, Environment> _moduleCache = new();
    private readonly HashSet<string> _importStack = new();
    private string? _currentFile;
    private readonly List<CallFrame> _callStack = new();
    private IDebugger? _debugger;
    private readonly HashSet<string> _loadedSources = new(StringComparer.OrdinalIgnoreCase);
    private Stash.Testing.ITestHarness? _testHarness;
    private string? _currentDescribe;
    private SourceSpan? _currentSpan;
    internal string[] ScriptArgs = Array.Empty<string>();
    internal readonly List<(StashInstance Handle, System.Diagnostics.Process OsProcess)> TrackedProcesses = new();
    internal readonly Dictionary<StashInstance, StashInstance> ProcessWaitCache = new(ReferenceEqualityComparer.Instance);
    private TextWriter _output = Console.Out;
    private TextWriter _errorOutput = Console.Error;

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
        ScriptArgs = args;
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
    /// Gets or sets the test harness. When set, test() and describe() report results to it.
    /// </summary>
    public Stash.Testing.ITestHarness? TestHarness
    {
        get => _testHarness;
        set => _testHarness = value;
    }

    /// <summary>
    /// Gets or sets the current describe block name for nesting test names.
    /// </summary>
    public string? CurrentDescribe
    {
        get => _currentDescribe;
        set => _currentDescribe = value;
    }

    /// <summary>
    /// Gets or sets the output writer used by io.println and io.print.
    /// Defaults to Console.Out. Override to capture or redirect output.
    /// </summary>
    public TextWriter Output
    {
        get => _output;
        set => _output = value;
    }

    /// <summary>
    /// Gets or sets the error output writer.
    /// Defaults to Console.Error. Override to capture or redirect error output.
    /// </summary>
    public TextWriter ErrorOutput
    {
        get => _errorOutput;
        set => _errorOutput = value;
    }

    /// <summary>
    /// Gets the current call stack as a read-only list.
    /// </summary>
    public IReadOnlyList<CallFrame> CallStack => _callStack;

    /// <summary>
    /// Gets all source files that have been loaded during execution (main script + imports).
    /// Used for DAP "loadedSources" request.
    /// </summary>
    public IReadOnlyCollection<string> LoadedSources => _loadedSources;

    /// <summary>
    /// Gets the source span of the statement currently being executed.
    /// Null when the interpreter is not executing. Useful for DAP to determine
    /// the current position when paused.
    /// </summary>
    public SourceSpan? CurrentSpan => _currentSpan;

    /// <summary>
    /// Gets the global environment. Useful for DAP to enumerate global variables.
    /// </summary>
    public Environment Globals => _globals;

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
        // Track loaded source
        if (_currentFile is not null && _loadedSources.Add(_currentFile))
        {
            _debugger?.OnSourceLoaded(_currentFile);
        }

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
        _currentSpan = stmt.Span;
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
    /// returning <c>null</c> in that case. The error message is stored in <see cref="LastError"/>.
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
            LastError = e.Message;
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

    private bool IsTruthy(object? value) => RuntimeValues.IsTruthy(value);

    public string Stringify(object? value) => RuntimeValues.Stringify(value);

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

    private bool IsEqual(object? left, object? right) => RuntimeValues.IsEqual(left, right);

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

    private static bool IsNumeric(object? value) => RuntimeValues.IsNumeric(value);

    private static double ToDouble(object? value) => RuntimeValues.ToDouble(value);

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

        SourceSpan? functionSpan = callee switch
        {
            StashFunction fn => fn.DefinitionSpan,
            StashLambda lm => lm.DefinitionSpan,
            _ => null
        };

        var callFrame = new CallFrame
        {
            FunctionName = functionName,
            CallSite = expr.Span,
            LocalScope = _environment,
            FunctionSpan = functionSpan
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

    /// <summary>
    /// Parses and evaluates a string expression in the given environment.
    /// Used for DAP "evaluate" requests (watch expressions, debug console, hover).
    /// Returns a tuple of (result value, error message or null).
    /// </summary>
    public (object? Value, string? Error) EvaluateString(string expression, Environment environment)
    {
        try
        {
            var lexer = new Lexer(expression, "<eval>");
            var tokens = lexer.ScanTokens();
            if (lexer.Errors.Count > 0)
                return (null, lexer.Errors[0].ToString());

            var parser = new Parser(tokens);
            Expr expr = parser.Parse();
            if (parser.Errors.Count > 0)
                return (null, parser.Errors[0].ToString());

            object? result = EvaluateInEnvironment(expr, environment);
            return (result, null);
        }
        catch (RuntimeError e)
        {
            return (null, e.Message);
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
            items = dict.IterableKeys().ToList();
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

    private static IEnumerable<object?> StringToChars(string str) => RuntimeValues.StringToChars(str);

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

            // Track loaded source for DAP
            if (_loadedSources.Add(resolvedPath))
            {
                _debugger?.OnSourceLoaded(resolvedPath);
            }

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
            StashEnumValue? value = enumDef.GetMember(expr.Name.Lexeme) ?? throw new RuntimeError($"Enum '{enumDef.Name}' has no member '{expr.Name.Lexeme}'.", expr.Name.Span);
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
            var (program, arguments) = CommandParser.Parse(command);
            var psi = new ProcessStartInfo
            {
                FileName = program,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = _pendingStdin is not null,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var arg in arguments)
            {
                psi.ArgumentList.Add(arg);
            }

            using var process = Process.Start(psi) ?? throw new RuntimeError("Failed to start process.", expr.Span);
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
        GlobalBuiltIns.Register(_globals);
        IoBuiltIns.Register(_globals);
        ConvBuiltIns.Register(_globals);
        EnvBuiltIns.Register(_globals);
        ProcessBuiltIns.Register(_globals);
        FsBuiltIns.Register(_globals);
        PathBuiltIns.Register(_globals);
        ArrBuiltIns.Register(_globals);
        DictBuiltIns.Register(_globals);
        StrBuiltIns.Register(_globals);
        TestBuiltIns.Register(_globals);
    }

    /// <summary>
    /// Cleans up all tracked processes on script exit.
    /// Sends SIGTERM, waits up to 3 seconds, then SIGKILL.
    /// </summary>
    public void CleanupTrackedProcesses()
    {
        foreach (var (_, osProcess) in TrackedProcesses)
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
        TrackedProcesses.Clear();
    }
}
