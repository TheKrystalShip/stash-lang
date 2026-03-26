namespace Stash.Interpreting;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Stash.Common;
using Stash.Debugging;
using Stash.Lexing;
using Stash.Parsing.AST;
using Stash.Interpreting.Types;

public partial class Interpreter
{
    private static readonly List<object?> _emptyArgs = new(0);
    /// <inheritdoc />
    public object? VisitLiteralExpr(LiteralExpr expr)
    {
        return expr.Value;
    }

    /// <inheritdoc />
    public object? VisitIdentifierExpr(IdentifierExpr expr)
    {
        if (_locals.TryGetValue(expr, out var resolved))
        {
            return _environment.GetAtSlot(resolved.Distance, resolved.Slot);
        }
        return _environment.Get(expr.Name.Lexeme, expr.Span);
    }

    /// <inheritdoc />
    public object? VisitGroupingExpr(GroupingExpr expr)
    {
        return expr.Expression.Accept(this);
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public object? VisitUpdateExpr(UpdateExpr expr)
    {
        // Fast path: resolved identifier with long value — skip Accept() dispatch
        if (expr.Operand is IdentifierExpr fastId && _locals.TryGetValue(expr, out var fastResolved))
        {
            object? fastOld = _environment.GetAtSlot(fastResolved.Distance, fastResolved.Slot);
            if (fastOld is long fastL)
            {
                long fastNew = expr.Operator.Type == TokenType.PlusPlus ? fastL + 1 : fastL - 1;
                object boxedNew = fastNew;
                _environment.SetAtSlot(fastResolved.Distance, fastResolved.Slot, fastId.Name.Lexeme, boxedNew, fastId.Name.Span);
                return expr.IsPrefix ? boxedNew : fastOld;
            }
        }

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
            if (_locals.TryGetValue(expr, out var resolved))
            {
                _environment.SetAtSlot(resolved.Distance, resolved.Slot, id.Name.Lexeme, newValue, id.Name.Span);
            }
            else
            {
                _environment.Assign(id.Name.Lexeme, newValue, id.Name.Span);
            }
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

    /// <inheritdoc />
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

        // Fast path: evaluate nested integer arithmetic without boxing intermediates.
        // Only activates when at least one operand is itself a BinaryExpr, where
        // eliminating intermediate boxing provides measurable benefit (~15-25%).
        // For simple leaf-to-leaf expressions (e.g. a + b), the overhead of entering
        // TryEvalLong exceeds the savings, so we skip it.
        if (expr.Left is BinaryExpr || expr.Right is BinaryExpr)
        {
            switch (expr.Operator.Type)
            {
                case TokenType.Plus:
                case TokenType.Minus:
                case TokenType.Star:
                case TokenType.Slash:
                case TokenType.Percent:
                    if (TryEvalLong(expr, out long longResult))
                    {
                        return longResult;
                    }
                    break;
            }
        }

        // General path: evaluate both sides (boxing intermediates).
        object? leftVal = expr.Left.Accept(this);
        object? rightVal = expr.Right.Accept(this);

        switch (expr.Operator.Type)
        {
            case TokenType.Plus:
                return EvaluatePlus(leftVal, rightVal, expr);

            case TokenType.Minus:
                if (leftVal is long lMinus && rightVal is long rMinus)
                {
                    return lMinus - rMinus;
                }

                if (IsNumeric(leftVal) && IsNumeric(rightVal))
                {
                    return ToDouble(leftVal) - ToDouble(rightVal);
                }

                throw new RuntimeError("Operands must be numbers.", expr.Operator.Span);

            case TokenType.Star:
                if (leftVal is string ls && rightVal is long ri)
                {
                    if (ri < 0)
                    {
                        throw new RuntimeError("String repeat count must be non-negative.", expr.Operator.Span);
                    }

                    return ri == 0 ? "" : string.Concat(Enumerable.Repeat(ls, (int)ri));
                }
                if (leftVal is long li2 && rightVal is string rs)
                {
                    if (li2 < 0)
                    {
                        throw new RuntimeError("String repeat count must be non-negative.", expr.Operator.Span);
                    }

                    return li2 == 0 ? "" : string.Concat(Enumerable.Repeat(rs, (int)li2));
                }
                if (leftVal is long lMul && rightVal is long rMul)
                {
                    return lMul * rMul;
                }

                if (IsNumeric(leftVal) && IsNumeric(rightVal))
                {
                    return ToDouble(leftVal) * ToDouble(rightVal);
                }

                throw new RuntimeError("Operands must be numbers.", expr.Operator.Span);

            case TokenType.Slash:
                return EvaluateDivision(leftVal, rightVal, expr);

            case TokenType.Percent:
                return EvaluateModulo(leftVal, rightVal, expr);

            case TokenType.EqualEqual:
                return IsEqual(leftVal, rightVal);

            case TokenType.BangEqual:
                return !IsEqual(leftVal, rightVal);

            case TokenType.Less:
                if (leftVal is long lLt && rightVal is long rLt)
                {
                    return lLt < rLt;
                }

                if (IsNumeric(leftVal) && IsNumeric(rightVal))
                {
                    return ToDouble(leftVal) < ToDouble(rightVal);
                }

                throw new RuntimeError("Operands must be numbers.", expr.Operator.Span);

            case TokenType.Greater:
                if (leftVal is long lGt && rightVal is long rGt)
                {
                    return lGt > rGt;
                }

                if (IsNumeric(leftVal) && IsNumeric(rightVal))
                {
                    return ToDouble(leftVal) > ToDouble(rightVal);
                }

                throw new RuntimeError("Operands must be numbers.", expr.Operator.Span);

            case TokenType.LessEqual:
                if (leftVal is long lLe && rightVal is long rLe)
                {
                    return lLe <= rLe;
                }

                if (IsNumeric(leftVal) && IsNumeric(rightVal))
                {
                    return ToDouble(leftVal) <= ToDouble(rightVal);
                }

                throw new RuntimeError("Operands must be numbers.", expr.Operator.Span);

            case TokenType.GreaterEqual:
                if (leftVal is long lGe && rightVal is long rGe)
                {
                    return lGe >= rGe;
                }

                if (IsNumeric(leftVal) && IsNumeric(rightVal))
                {
                    return ToDouble(leftVal) >= ToDouble(rightVal);
                }

                throw new RuntimeError("Operands must be numbers.", expr.Operator.Span);

            case TokenType.In:
                return EvaluateIn(leftVal, rightVal, expr);

            default:
                throw new RuntimeError($"Unknown binary operator '{expr.Operator.Lexeme}'.", expr.Operator.Span);
        }
    }

    /// <summary>Evaluates the <c>in</c> operator for membership testing across arrays, strings, dictionaries, and ranges.</summary>
    private static bool EvaluateIn(object? left, object? right, BinaryExpr expr)
    {
        return right switch
        {
            List<object?> list => list.Any(item => RuntimeValues.IsEqual(left, item)),
            string str when left is string sub => str.Contains(sub),
            StashDictionary dict => left is not null && dict.Has(left),
            StashRange range when left is long l => range.Contains(l),
            StashRange range when left is double d && d == Math.Floor(d) => range.Contains((long)d),
            StashRange => throw new RuntimeError("Left operand of 'in' must be an integer when checking range membership.", expr.Span),
            string => throw new RuntimeError("Left operand of 'in' must be a string when checking string containment.", expr.Span),
            _ => throw new RuntimeError("Right operand of 'in' must be an array, string, dictionary, or range.", expr.Span)
        };
    }

    /// <inheritdoc />
    public object? VisitIsExpr(IsExpr expr)
    {
        object? value = expr.Left.Accept(this);
        string typeName = expr.TypeName.Lexeme;

        return typeName switch
        {
            "null" => value is null,
            "int" => value is long,
            "float" => value is double,
            "string" => value is string,
            "bool" => value is bool,
            "array" => value is List<object?>,
            "Error" => value is StashError,
            "struct" => value is StashInstance or StashStruct,
            "enum" => value is StashEnumValue or StashEnum,
            "dict" => value is StashDictionary,
            "range" => value is StashRange,
            "namespace" => value is StashNamespace,
            "function" => value is IStashCallable,
            _ => false
        };
    }

    /// <inheritdoc />
    public object? VisitTernaryExpr(TernaryExpr expr)
    {
        object? condition = expr.Condition.Accept(this);
        return IsTruthy(condition)
            ? expr.ThenBranch.Accept(this)
            : expr.ElseBranch.Accept(this);
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public object? VisitTryExpr(TryExpr expr)
    {
        try
        {
            return expr.Expression.Accept(this);
        }
        catch (RuntimeError e)
        {
            var error = StashError.FromRuntimeError(e, _ctx.CallStack);
            LastError = error;
            return error;
        }
    }

    /// <inheritdoc />
    public object? VisitNullCoalesceExpr(NullCoalesceExpr expr)
    {
        object? left = expr.Left.Accept(this);
        if (left is not null and not StashError)
        {
            return left;
        }
        return expr.Right.Accept(this);
    }

    /// <inheritdoc />
    public object? VisitRangeExpr(RangeExpr expr)
    {
        object? startVal = expr.Start.Accept(this);
        object? endVal = expr.End.Accept(this);

        if (startVal is not long start)
        {
            throw new RuntimeError("Range start must be an integer.", expr.Start.Span);
        }

        if (endVal is not long end)
        {
            throw new RuntimeError("Range end must be an integer.", expr.End.Span);
        }

        long step = start <= end ? 1 : -1;
        if (expr.Step is not null)
        {
            object? stepVal = expr.Step.Accept(this);
            if (stepVal is not long s)
            {
                throw new RuntimeError("Range step must be an integer.", expr.Step.Span);
            }

            if (s == 0)
            {
                throw new RuntimeError("Range step cannot be zero.", expr.Step.Span);
            }

            step = s;
        }

        return new StashRange(start, end, step);
    }

    /// <inheritdoc />
    public object? VisitAssignExpr(AssignExpr expr)
    {
        object? value = expr.Value.Accept(this);
        if (_locals.TryGetValue(expr, out var resolved))
        {
            _environment.SetAtSlot(resolved.Distance, resolved.Slot, expr.Name.Lexeme, value, expr.Name.Span);
        }
        else
        {
            _environment.Assign(expr.Name.Lexeme, value, expr.Name.Span);
        }
        return value;
    }

    /// <inheritdoc />
    public object? VisitCallExpr(CallExpr expr)
    {
        object? callee = expr.Callee.Accept(this);

        List<object?> arguments;
        if (expr.Arguments.Count == 0)
        {
            arguments = _emptyArgs;
        }
        else
        {
            arguments = new List<object?>(expr.Arguments.Count);
            foreach (Expr argument in expr.Arguments)
            {
                arguments.Add(argument.Accept(this));
            }
        }

        if (callee is not IStashCallable function)
        {
            throw new RuntimeError("Can only call functions.", expr.Paren.Span);
        }

        if (function.Arity != -1)
        {
            int minArity = function.MinArity;
            if (arguments.Count < minArity || arguments.Count > function.Arity)
            {
                string expected = minArity == function.Arity
                    ? $"{function.Arity}"
                    : $"{minArity} to {function.Arity}";
                throw new RuntimeError(
                    $"Expected {expected} arguments but got {arguments.Count}.",
                    expr.Paren.Span);
            }
        }

        if (_debugger == null)
        {
            return function.Call(this, arguments);
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
        _debugger?.OnFunctionEnter(functionName, expr.Span, _environment, DebugThreadId);

        try
        {
            object? result = function.Call(this, arguments);
            return result;
        }
        finally
        {
            _callStack.RemoveAt(_callStack.Count - 1);
            _debugger?.OnFunctionExit(functionName, DebugThreadId);
        }
    }

    /// <inheritdoc />
    public object? VisitLambdaExpr(LambdaExpr expr)
    {
        return new StashLambda(expr, _environment);
    }

    /// <inheritdoc />
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

        return new StashInstance(template.Name, template, fieldValues);
    }

    /// <inheritdoc />
    public object? VisitDotExpr(DotExpr expr)
    {
        object? obj = expr.Object.Accept(this);

        // Optional chaining: a?.b returns null if a is null
        if (expr.IsOptional && obj is null)
        {
            return null;
        }

        if (obj is StashError error)
        {
            return expr.Name.Lexeme switch
            {
                "message" => error.Message,
                "type" => error.Type,
                "stack" => error.Stack is not null ? new List<object?>(error.Stack) : null,
                _ => throw new RuntimeError($"Error has no field '{expr.Name.Lexeme}'. Available fields: message, type, stack.", expr.Name.Span)
            };
        }

        if (obj is StashInstance instance)
        {
            return instance.GetField(expr.Name.Lexeme, expr.Name.Span);
        }

        if (obj is StashDictionary dict)
        {
            return dict.Get(expr.Name.Lexeme);
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

        throw new RuntimeError("Only struct instances, dictionaries, enums, and namespaces have members.", expr.Name.Span);
    }

    /// <inheritdoc />
    public object? VisitDotAssignExpr(DotAssignExpr expr)
    {
        object? obj = expr.Object.Accept(this);

        if (obj is StashNamespace)
        {
            throw new RuntimeError("Cannot assign to namespace members.", expr.Name.Span);
        }

        if (obj is StashDictionary dict)
        {
            object? value = expr.Value.Accept(this);
            dict.Set(expr.Name.Lexeme, value);
            return value;
        }

        if (obj is not StashInstance instance)
        {
            throw new RuntimeError("Only struct instances and dictionaries have fields.", expr.Name.Span);
        }

        object? assignValue = expr.Value.Accept(this);
        instance.SetField(expr.Name.Lexeme, assignValue, expr.Name.Span);
        return assignValue;
    }

    /// <inheritdoc />
    public object? VisitInterpolatedStringExpr(InterpolatedStringExpr expr)
    {
        var sb = new StringBuilder();
        foreach (Expr part in expr.Parts)
        {
            object? value = part.Accept(this);
            sb.Append(Stringify(value));
        }
        return sb.ToString();
    }

    /// <inheritdoc />
    public object? VisitArrayExpr(ArrayExpr expr)
    {
        var elements = new List<object?>();
        foreach (Expr element in expr.Elements)
        {
            elements.Add(element.Accept(this));
        }
        return elements;
    }

    /// <inheritdoc />
    public object? VisitDictLiteralExpr(DictLiteralExpr expr)
    {
        var dict = new StashDictionary();
        foreach (var (key, value) in expr.Entries)
        {
            dict.Set(key.Lexeme, value.Accept(this));
        }
        return dict;
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <summary>Determines whether a value is truthy per Stash semantics.</summary>
    private bool IsTruthy(object? value) => RuntimeValues.IsTruthy(value);

    /// <summary>Converts a runtime value to its string representation for display.</summary>
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

    /// <summary>Compares two values for equality without type coercion.</summary>
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

    /// <summary>Returns whether a value is a numeric type (<see cref="long"/> or <see cref="double"/>).</summary>
    private static bool IsNumeric(object? value) => RuntimeValues.IsNumeric(value);

    /// <summary>Converts a numeric value to <see cref="double"/> for type promotion.</summary>
    private static double ToDouble(object? value) => RuntimeValues.ToDouble(value);

    /// <summary>
    /// Attempts to evaluate an expression directly as a <see cref="long"/> without boxing.
    /// Handles literals, resolved identifiers, grouping, unary negation, and recursive
    /// binary arithmetic — eliminating intermediate heap allocations on the fast path.
    /// </summary>
    private bool TryEvalLong(Expr expr, out long result)
    {
        switch (expr)
        {
            case LiteralExpr lit when lit.Value is long l:
                result = l;
                return true;

            case IdentifierExpr id:
                if (_locals.TryGetValue(id, out var resolved))
                {
                    object? val = _environment.GetAtSlot(resolved.Distance, resolved.Slot);
                    if (val is long lv)
                    {
                        result = lv;
                        return true;
                    }
                }
                break;

            case GroupingExpr g:
                return TryEvalLong(g.Expression, out result);

            case UnaryExpr u when u.Operator.Type == TokenType.Minus:
                if (TryEvalLong(u.Right, out long neg))
                {
                    result = -neg;
                    return true;
                }
                break;

            case BinaryExpr bin:
                if (TryEvalLong(bin.Left, out long left) && TryEvalLong(bin.Right, out long right))
                {
                    switch (bin.Operator.Type)
                    {
                        case TokenType.Plus:
                            result = left + right;
                            return true;
                        case TokenType.Minus:
                            result = left - right;
                            return true;
                        case TokenType.Star:
                            result = left * right;
                            return true;
                        case TokenType.Slash:
                            if (right != 0) { result = left / right; return true; }
                            break;
                        case TokenType.Percent:
                            if (right != 0) { result = left % right; return true; }
                            break;
                    }
                }
                break;
        }

        result = 0;
        return false;
    }

    /// <summary>
    /// Evaluates an expression's truthiness with a fast path for integer comparisons,
    /// avoiding boxing the intermediate boolean result.
    /// Directly inlines slot reads for the common <c>identifier &lt; identifier</c> pattern
    /// to minimize overhead on tight loops.
    /// </summary>
    internal bool EvalConditionTruthy(Expr condition)
    {
        if (condition is BinaryExpr bin
            && TryReadLong(bin.Left, out long left)
            && TryReadLong(bin.Right, out long right))
        {
            switch (bin.Operator.Type)
            {
                case TokenType.Less: return left < right;
                case TokenType.LessEqual: return left <= right;
                case TokenType.Greater: return left > right;
                case TokenType.GreaterEqual: return left >= right;
                case TokenType.EqualEqual: return left == right;
                case TokenType.BangEqual: return left != right;
            }
        }

        return IsTruthy(condition.Accept(this));
    }

    /// <summary>
    /// Reads a long value from a leaf expression (literal or resolved identifier) without boxing.
    /// Does not evaluate sub-expressions — only handles leaf nodes for minimal overhead.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private bool TryReadLong(Expr expr, out long value)
    {
        if (expr is IdentifierExpr id && _locals.TryGetValue(id, out var res))
        {
            object? v = _environment.GetAtSlot(res.Distance, res.Slot);
            if (v is long lv) { value = lv; return true; }
        }
        else if (expr is LiteralExpr lit && lit.Value is long l)
        {
            value = l;
            return true;
        }
        value = 0;
        return false;
    }

}
