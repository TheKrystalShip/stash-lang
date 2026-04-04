namespace Stash.Interpreting;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Stash.Common;
using Stash.Debugging;
using Stash.Lexing;
using Stash.Parsing.AST;
using Stash.Runtime;
using Stash.Interpreting.Types;
using Stash.Runtime.Types;

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
        if (expr.ResolvedDistance >= 0)
        {
            return _environment.GetAtSlot(expr.ResolvedDistance, expr.ResolvedSlot);
        }
        return _environment.Get(expr.Name.Lexeme, expr.Span);
    }

    /// <inheritdoc />
    public object? VisitGroupingExpr(GroupingExpr expr)
    {
        return Evaluate(expr.Expression);
    }

    /// <inheritdoc />
    public object? VisitUnaryExpr(UnaryExpr expr)
    {
        object? right = Evaluate(expr.Right);

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

                if (right is StashDuration durNeg)
                {
                    return durNeg.Negate();
                }

                if (right is StashByteSize bsNeg)
                {
                    return new StashByteSize(-bsNeg.TotalBytes);
                }

                throw new RuntimeError("Operand must be a number.", expr.Operator.Span);

            case TokenType.Tilde:
                if (right is long ti)
                {
                    return ~ti;
                }

                if (right is StashIpAddress tipAddr)
                {
                    return tipAddr.BitwiseNot();
                }

                throw new RuntimeError("Operand must be an integer.", expr.Operator.Span);

            default:
                throw new RuntimeError($"Unknown unary operator '{expr.Operator.Lexeme}'.", expr.Operator.Span);
        }
    }

    /// <inheritdoc />
    public object? VisitUpdateExpr(UpdateExpr expr)
    {
        // Fast path: resolved identifier with long value — skip Accept() dispatch
        if (expr.Operand is IdentifierExpr fastId && expr.ResolvedDistance >= 0)
        {
            object? fastOld = _environment.GetAtSlot(expr.ResolvedDistance, expr.ResolvedSlot);
            if (fastOld is long fastL)
            {
                long fastNew = expr.Operator.Type == TokenType.PlusPlus ? fastL + 1 : fastL - 1;
                object boxedNew = fastNew;
                _environment.SetAtSlot(expr.ResolvedDistance, expr.ResolvedSlot, fastId.Name.Lexeme, boxedNew, fastId.Name.Span);
                return expr.IsPrefix ? boxedNew : fastOld;
            }
        }

        object? oldValue = Evaluate(expr.Operand);

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
            if (expr.ResolvedDistance >= 0)
            {
                _environment.SetAtSlot(expr.ResolvedDistance, expr.ResolvedSlot, id.Name.Lexeme, newValue, id.Name.Span);
            }
            else
            {
                _environment.Assign(id.Name.Lexeme, newValue, id.Name.Span);
            }
        }
        else if (expr.Operand is DotExpr dot)
        {
            object? obj = Evaluate(dot.Object);
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
            object? collection = Evaluate(idx.Object);
            object? index = Evaluate(idx.Index);
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
            object? left = Evaluate(expr.Left);
            return !IsTruthy(left) ? left : Evaluate(expr.Right);
        }

        if (expr.Operator.Type == TokenType.PipePipe)
        {
            object? left = Evaluate(expr.Left);
            return IsTruthy(left) ? left : Evaluate(expr.Right);
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
                case TokenType.Ampersand:
                case TokenType.Pipe:
                case TokenType.Caret:
                case TokenType.LessLess:
                case TokenType.GreaterGreater:
                    if (TryEvalLong(expr, out long longResult))
                    {
                        return longResult;
                    }
                    break;
            }
        }

        // General path: evaluate both sides (boxing intermediates).
        object? leftVal = Evaluate(expr.Left);
        object? rightVal = Evaluate(expr.Right);

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

                if (leftVal is StashIpAddress ipSubL && rightVal is StashIpAddress ipSubR)
                {
                    return ipSubL.Subtract(ipSubR);
                }

                if (leftVal is StashIpAddress ipSubA && rightVal is long offsetSub)
                {
                    return ipSubA.Add(-offsetSub);
                }

                if (leftVal is StashDuration durSubL && rightVal is StashDuration durSubR)
                {
                    return durSubL.Subtract(durSubR);
                }

                if (leftVal is StashByteSize bsSubL && rightVal is StashByteSize bsSubR)
                {
                    return bsSubL.Subtract(bsSubR);
                }

                if ((leftVal is StashDuration || rightVal is StashDuration) || (leftVal is StashByteSize || rightVal is StashByteSize))
                {
                    throw new RuntimeError("Cannot mix duration or byte size with other types in subtraction.", expr.Operator.Span);
                }

                throw new RuntimeError("Operands must be two numbers or two IP addresses.", expr.Operator.Span);

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

                if (leftVal is StashDuration durMulL && IsNumeric(rightVal))
                {
                    return durMulL.Multiply(ToDouble(rightVal));
                }

                if (IsNumeric(leftVal) && rightVal is StashDuration durMulR)
                {
                    return durMulR.Multiply(ToDouble(leftVal));
                }

                if (leftVal is StashByteSize bsMulL && IsNumeric(rightVal))
                {
                    return bsMulL.Multiply(ToDouble(rightVal));
                }

                if (IsNumeric(leftVal) && rightVal is StashByteSize bsMulR)
                {
                    return bsMulR.Multiply(ToDouble(leftVal));
                }

                if ((leftVal is StashDuration || rightVal is StashDuration) || (leftVal is StashByteSize || rightVal is StashByteSize))
                {
                    throw new RuntimeError("Duration and byte size can only be multiplied by a number.", expr.Operator.Span);
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

                if (leftVal is StashIpAddress ipLtL && rightVal is StashIpAddress ipLtR)
                {
                    return ipLtL.CompareTo(ipLtR) < 0;
                }

                if (leftVal is StashDuration durLtL && rightVal is StashDuration durLtR)
                {
                    return durLtL.CompareTo(durLtR) < 0;
                }

                if (leftVal is StashByteSize bsLtL && rightVal is StashByteSize bsLtR)
                {
                    return bsLtL.CompareTo(bsLtR) < 0;
                }

                if (leftVal is StashSemVer svLtL && rightVal is StashSemVer svLtR)
                {
                    return svLtL.CompareTo(svLtR) < 0;
                }

                if (leftVal is StashSemVer || rightVal is StashSemVer)
                {
                    throw new RuntimeError("Semver can only be compared to another semver.", expr.Operator.Span);
                }

                if ((leftVal is StashDuration || rightVal is StashDuration) || (leftVal is StashByteSize || rightVal is StashByteSize))
                {
                    throw new RuntimeError("Cannot compare duration or byte size with incompatible types.", expr.Operator.Span);
                }

                throw new RuntimeError("Operands must be two numbers or two IP addresses.", expr.Operator.Span);

            case TokenType.Greater:
                if (leftVal is long lGt && rightVal is long rGt)
                {
                    return lGt > rGt;
                }

                if (IsNumeric(leftVal) && IsNumeric(rightVal))
                {
                    return ToDouble(leftVal) > ToDouble(rightVal);
                }

                if (leftVal is StashIpAddress ipGtL && rightVal is StashIpAddress ipGtR)
                {
                    return ipGtL.CompareTo(ipGtR) > 0;
                }

                if (leftVal is StashDuration durGtL && rightVal is StashDuration durGtR)
                {
                    return durGtL.CompareTo(durGtR) > 0;
                }

                if (leftVal is StashByteSize bsGtL && rightVal is StashByteSize bsGtR)
                {
                    return bsGtL.CompareTo(bsGtR) > 0;
                }

                if (leftVal is StashSemVer svGtL && rightVal is StashSemVer svGtR)
                {
                    return svGtL.CompareTo(svGtR) > 0;
                }

                if (leftVal is StashSemVer || rightVal is StashSemVer)
                {
                    throw new RuntimeError("Semver can only be compared to another semver.", expr.Operator.Span);
                }

                if ((leftVal is StashDuration || rightVal is StashDuration) || (leftVal is StashByteSize || rightVal is StashByteSize))
                {
                    throw new RuntimeError("Cannot compare duration or byte size with incompatible types.", expr.Operator.Span);
                }

                throw new RuntimeError("Operands must be two numbers or two IP addresses.", expr.Operator.Span);

            case TokenType.LessEqual:
                if (leftVal is long lLe && rightVal is long rLe)
                {
                    return lLe <= rLe;
                }

                if (IsNumeric(leftVal) && IsNumeric(rightVal))
                {
                    return ToDouble(leftVal) <= ToDouble(rightVal);
                }

                if (leftVal is StashIpAddress ipLeL && rightVal is StashIpAddress ipLeR)
                {
                    return ipLeL.CompareTo(ipLeR) <= 0;
                }

                if (leftVal is StashDuration durLeL && rightVal is StashDuration durLeR)
                {
                    return durLeL.CompareTo(durLeR) <= 0;
                }

                if (leftVal is StashByteSize bsLeL && rightVal is StashByteSize bsLeR)
                {
                    return bsLeL.CompareTo(bsLeR) <= 0;
                }

                if (leftVal is StashSemVer svLeL && rightVal is StashSemVer svLeR)
                {
                    return svLeL.CompareTo(svLeR) <= 0;
                }

                if (leftVal is StashSemVer || rightVal is StashSemVer)
                {
                    throw new RuntimeError("Semver can only be compared to another semver.", expr.Operator.Span);
                }

                if ((leftVal is StashDuration || rightVal is StashDuration) || (leftVal is StashByteSize || rightVal is StashByteSize))
                {
                    throw new RuntimeError("Cannot compare duration or byte size with incompatible types.", expr.Operator.Span);
                }

                throw new RuntimeError("Operands must be two numbers or two IP addresses.", expr.Operator.Span);

            case TokenType.GreaterEqual:
                if (leftVal is long lGe && rightVal is long rGe)
                {
                    return lGe >= rGe;
                }

                if (IsNumeric(leftVal) && IsNumeric(rightVal))
                {
                    return ToDouble(leftVal) >= ToDouble(rightVal);
                }

                if (leftVal is StashIpAddress ipGeL && rightVal is StashIpAddress ipGeR)
                {
                    return ipGeL.CompareTo(ipGeR) >= 0;
                }

                if (leftVal is StashDuration durGeL && rightVal is StashDuration durGeR)
                {
                    return durGeL.CompareTo(durGeR) >= 0;
                }

                if (leftVal is StashByteSize bsGeL && rightVal is StashByteSize bsGeR)
                {
                    return bsGeL.CompareTo(bsGeR) >= 0;
                }

                if (leftVal is StashSemVer svGeL && rightVal is StashSemVer svGeR)
                {
                    return svGeL.CompareTo(svGeR) >= 0;
                }

                if (leftVal is StashSemVer || rightVal is StashSemVer)
                {
                    throw new RuntimeError("Semver can only be compared to another semver.", expr.Operator.Span);
                }

                if ((leftVal is StashDuration || rightVal is StashDuration) || (leftVal is StashByteSize || rightVal is StashByteSize))
                {
                    throw new RuntimeError("Cannot compare duration or byte size with incompatible types.", expr.Operator.Span);
                }

                throw new RuntimeError("Operands must be two numbers or two IP addresses.", expr.Operator.Span);

            case TokenType.In:
                return EvaluateIn(leftVal, rightVal, expr);

            case TokenType.Ampersand:
                if (leftVal is long lAnd && rightVal is long rAnd)
                {
                    return lAnd & rAnd;
                }

                if (leftVal is StashIpAddress ipAndL && rightVal is StashIpAddress ipAndR)
                {
                    return ipAndL.BitwiseAnd(ipAndR);
                }

                throw new RuntimeError("Operands must be two integers or two IP addresses.", expr.Operator.Span);

            case TokenType.Pipe:
                if (leftVal is long lOr && rightVal is long rOr)
                {
                    return lOr | rOr;
                }

                if (leftVal is StashIpAddress ipOrL && rightVal is StashIpAddress ipOrR)
                {
                    return ipOrL.BitwiseOr(ipOrR);
                }

                throw new RuntimeError("Operands must be two integers or two IP addresses.", expr.Operator.Span);

            case TokenType.Caret:
                if (leftVal is long lXor && rightVal is long rXor)
                {
                    return lXor ^ rXor;
                }

                throw new RuntimeError("Operands must be integers.", expr.Operator.Span);

            case TokenType.LessLess:
                if (leftVal is long lShl && rightVal is long rShl)
                {
                    if (rShl < 0 || rShl > 63)
                        throw new RuntimeError("Shift count must be in the range 0..63.", expr.Operator.Span);
                    return lShl << (int)rShl;
                }

                throw new RuntimeError("Operands must be integers.", expr.Operator.Span);

            case TokenType.GreaterGreater:
                if (leftVal is long lShr && rightVal is long rShr)
                {
                    if (rShr < 0 || rShr > 63)
                        throw new RuntimeError("Shift count must be in the range 0..63.", expr.Operator.Span);
                    return lShr >> (int)rShr;
                }

                throw new RuntimeError("Operands must be integers.", expr.Operator.Span);

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
            StashIpAddress ipNet when left is StashIpAddress ipAddr => ipNet.Contains(ipAddr),
            StashIpAddress => throw new RuntimeError("Left operand of 'in' must be an IP address when checking CIDR containment.", expr.Span),
            StashSemVer svRange when left is StashSemVer svVal => svRange.Matches(svVal),
            StashSemVer => throw new RuntimeError("Left operand of 'in' must be a semver when checking version range.", expr.Span),
            _ => throw new RuntimeError("Right operand of 'in' must be an array, string, dictionary, range, or semver.", expr.Span)
        };
    }

    /// <inheritdoc />
    public object? VisitIsExpr(IsExpr expr)
    {
        object? value = Evaluate(expr.Left);

        if (expr.TypeName != null)
        {
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
                "Future" => value is StashFuture,
                "duration" => value is StashDuration,
                "bytes" => value is StashByteSize,
                "ip" => value is StashIpAddress,
                "semver" => value is StashSemVer,
                _ => CheckCustomType(value, typeName, expr.TypeName.Span)
            };
        }

        // Expression path: evaluate RHS, check resolved type value
        object? typeValue = Evaluate(expr.TypeExpr!);
        return CheckTypeValue(value, typeValue, expr.Span);
    }

    /// <summary>
    /// Checks if a value matches a resolved type value from an evaluated expression.
    /// </summary>
    private bool CheckTypeValue(object? value, object? typeValue, SourceSpan? span)
    {
        if (typeValue is StashInterface iface)
        {
            return value is StashInstance inst && (inst.Struct?.Interfaces.Contains(iface) ?? false);
        }

        if (typeValue is StashStruct structType)
        {
            return value is StashInstance inst && inst.Struct == structType;
        }

        if (typeValue is StashEnum enumType)
        {
            return value is StashEnumValue enumVal && enumVal.TypeName == enumType.Name;
        }

        throw new RuntimeError("Right operand of 'is' must resolve to an interface, struct, or enum type.", span);
    }

    /// <summary>
    /// Checks if a value matches a custom type name — struct name, enum name, or interface conformance.
    /// </summary>
    private bool CheckCustomType(object? value, string typeName, SourceSpan? span)
    {
        // Direct struct type name match
        if (value is StashInstance instance)
        {
            if (instance.TypeName == typeName)
            {
                return true;
            }

            // Check interface conformance via resolved reference
            object? resolved = null;
            try
            {
                resolved = _environment.Get(typeName, span);
            }
            catch (RuntimeError)
            {
                // Name not found in environment — not a known type
                return false;
            }

            if (resolved is StashInterface iface)
            {
                return instance.Struct?.Interfaces.Contains(iface) ?? false;
            }

            if (resolved is StashStruct resolvedStruct)
            {
                return instance.Struct == resolvedStruct;
            }

            return false;
        }

        // Direct enum type name match
        if (value is StashEnumValue enumVal)
        {
            if (enumVal.TypeName == typeName)
            {
                return true;
            }

            // Check if typeName resolves to a StashEnum
            object? resolved = null;
            try
            {
                resolved = _environment.Get(typeName, span);
            }
            catch (RuntimeError)
            {
                return false;
            }

            if (resolved is StashEnum enumType)
            {
                return enumVal.TypeName == enumType.Name;
            }

            return false;
        }

        return false;
    }

    /// <inheritdoc />
    public object? VisitTernaryExpr(TernaryExpr expr)
    {
        object? condition = Evaluate(expr.Condition);
        return IsTruthy(condition)
            ? Evaluate(expr.ThenBranch)
            : Evaluate(expr.ElseBranch);
    }

    /// <inheritdoc />
    public object? VisitSwitchExpr(SwitchExpr expr)
    {
        object? subject = Evaluate(expr.Subject);
        foreach (var arm in expr.Arms)
        {
            if (arm.IsDiscard)
            {
                return Evaluate(arm.Body);
            }
            object? pattern = Evaluate(arm.Pattern!);
            if (IsEqual(subject, pattern))
            {
                return Evaluate(arm.Body);
            }
        }
        throw new RuntimeError("No matching arm in switch expression.", expr.Span);
    }

    /// <inheritdoc />
    public object? VisitTryExpr(TryExpr expr)
    {
        try
        {
            return Evaluate(expr.Expression);
        }
        catch (RuntimeError e)
        {
            var error = StashError.FromRuntimeError(e, Ctx.CallStack.Select(f => (f.FunctionName, f.CallSite)).ToList());
            LastError = error;
            return error;
        }
    }

    /// <inheritdoc />
    public object? VisitAwaitExpr(AwaitExpr expr)
    {
        object? value = Evaluate(expr.Expression);

        if (value is StashFuture future)
        {
            return future.GetResult();
        }

        // Transparent: non-future values pass through
        return value;
    }

    /// <inheritdoc />
    public object? VisitRetryExpr(RetryExpr expr)
    {
        // 1. Evaluate max attempts
        object? maxAttemptsVal = Evaluate(expr.MaxAttempts);
        if (maxAttemptsVal is not long maxAttempts)
            throw new RuntimeError("First argument to 'retry' must be an integer.", expr.MaxAttempts.Span);
        if (maxAttempts < 0)
            throw new RuntimeError("Retry attempt count must be non-negative.", expr.MaxAttempts.Span);

        // 2. Evaluate options
        long delayMs = 0;
        string backoff = "Fixed";
        long maxDelayMs = long.MaxValue;
        bool jitter = false;
        long timeoutMs = 0;
        List<string>? onTypes = null;

        if (expr.NamedOptions is not null)
        {
            foreach (var (name, value) in expr.NamedOptions)
            {
                object? v = Evaluate(value);
                switch (name.Lexeme)
                {
                    case "delay":
                        delayMs = ExtractDurationMs(v, "delay", name.Span);
                        break;
                    case "backoff":
                        if (v is StashEnumValue bev)
                            backoff = bev.MemberName;
                        else
                            throw new RuntimeError("Option 'backoff' must be a Backoff enum value.", name.Span);
                        break;
                    case "maxDelay":
                        maxDelayMs = ExtractDurationMs(v, "maxDelay", name.Span);
                        break;
                    case "jitter":
                        if (v is not bool jitterVal)
                            throw new RuntimeError("Option 'jitter' must be a boolean.", name.Span);
                        jitter = jitterVal;
                        break;
                    case "timeout":
                        timeoutMs = ExtractDurationMs(v, "timeout", name.Span);
                        break;
                    case "on":
                        if (v is not List<object?> typeList)
                            throw new RuntimeError("Option 'on' must be an array of error type names.", name.Span);
                        onTypes = new List<string>();
                        foreach (object? item in typeList)
                        {
                            if (item is string s) onTypes.Add(s);
                            else if (item is StashEnumValue ev) onTypes.Add(ev.MemberName);
                            else onTypes.Add(RuntimeValues.Stringify(item));
                        }
                        break;
                    default:
                        throw new RuntimeError($"Unknown retry option '{name.Lexeme}'.", name.Span);
                }
            }
        }
        else if (expr.OptionsExpr is not null)
        {
            object? opts = Evaluate(expr.OptionsExpr);
            if (opts is StashInstance optsInst)
            {
                ExtractRetryOptionsFromInstance(optsInst, ref delayMs, ref backoff, ref maxDelayMs,
                    ref jitter, ref timeoutMs, ref onTypes, expr.OptionsExpr.Span);
            }
            else
            {
                throw new RuntimeError("Second argument to 'retry' must be retry options.", expr.OptionsExpr.Span);
            }
        }

        // 3. Evaluate until predicate
        IStashCallable? untilPredicate = null;
        if (expr.UntilClause is not null)
        {
            object? untilVal = Evaluate(expr.UntilClause);
            if (untilVal is not IStashCallable callable)
                throw new RuntimeError("'until' clause must be a function or lambda.", expr.UntilClause.Span);
            untilPredicate = callable;
        }

        // 4. Evaluate onRetry hook
        IStashCallable? onRetryFn = null;
        Token? onRetryParamAttempt = null;
        Token? onRetryParamError = null;
        BlockStmt? onRetryBody = null;
        if (expr.OnRetryClause is not null)
        {
            var clause = expr.OnRetryClause;
            if (clause.IsReference)
            {
                object? refVal = Evaluate(clause.Reference!);
                if (refVal is not IStashCallable refCallable)
                    throw new RuntimeError("'onRetry' function reference must be callable.", clause.Reference!.Span);
                onRetryFn = refCallable;
            }
            else
            {
                onRetryParamAttempt = clause.ParamAttempt;
                onRetryParamError = clause.ParamError;
                onRetryBody = clause.Body;
            }
        }

        // 5. Execute retry loop
        RuntimeError? lastError = null;
        bool lastFailureWasException = false;
        object? lastValue = null;
        var collectedErrors = new List<object?>();
        long startTimeMs = System.Environment.TickCount64;
        var random = jitter ? new Random() : null;

        for (long currentAttempt = 1; currentAttempt <= maxAttempts; currentAttempt++)
        {
            // Check timeout before each attempt
            if (timeoutMs > 0)
            {
                long elapsed = System.Environment.TickCount64 - startTimeMs;
                if (elapsed >= timeoutMs)
                {
                    throw new RuntimeError(
                        $"Retry timed out after {FormatDuration(elapsed)} (completed {currentAttempt - 1} of {maxAttempts} attempts)",
                        expr.Span,
                        errorType: "RetryTimeoutError")
                    {
                        Properties = new Dictionary<string, object?>
                        {
                            ["elapsed"] = new StashDuration(elapsed),
                            ["completedAttempts"] = currentAttempt - 1
                        }
                    };
                }
            }

            // Bind attempt context in a new scope
            var retryEnv = new Environment(_environment);
            var attemptContext = new StashInstance("RetryContext", new Dictionary<string, object?>
            {
                ["current"] = currentAttempt,
                ["max"] = maxAttempts,
                ["remaining"] = maxAttempts - currentAttempt,
                ["elapsed"] = new StashDuration(System.Environment.TickCount64 - startTimeMs),
                ["errors"] = new List<object?>(collectedErrors)
            });
            retryEnv.Define("attempt", attemptContext);

            object? result = null;
            bool bodyThrew = false;
            RuntimeError? bodyError = null;

            try
            {
                result = ExecuteRetryBody(expr.Body, retryEnv);
            }
            catch (RuntimeError e)
            {
                if (onTypes is not null)
                {
                    string errorType = e.ErrorType ?? "RuntimeError";
                    if (!onTypes.Contains(errorType) && !onTypes.Contains("Error"))
                        throw;
                }

                bodyThrew = true;
                bodyError = e;
                lastError = e;
                lastFailureWasException = true;
                var stashErr = StashError.FromRuntimeError(e, Ctx.CallStack.Select(f => (f.FunctionName, f.CallSite)).ToList());
                collectedErrors.Add(stashErr);
                InvokeOnRetryHook(onRetryFn, onRetryParamAttempt, onRetryParamError, onRetryBody,
                    currentAttempt, stashErr, currentAttempt < maxAttempts);
            }

            // Predicate evaluation runs OUTSIDE the try/catch so that predicate throws
            // and onRetry hook throws (on predicate failure) propagate immediately (spec §5.5, §6.4)
            if (!bodyThrew)
            {
                if (untilPredicate is not null)
                {
                    object? predicateResult;
                    int predicateArity = untilPredicate.Arity == -1 ? 1 : untilPredicate.Arity;
                    if (predicateArity >= 2)
                        predicateResult = untilPredicate.Call(this, new List<object?> { result, currentAttempt });
                    else
                        predicateResult = untilPredicate.Call(this, new List<object?> { result });

                    if (RuntimeValues.IsTruthy(predicateResult))
                    {
                        return result;
                    }
                    else
                    {
                        lastValue = result;
                        lastFailureWasException = false;
                        var predErr = new StashError("Predicate not satisfied", "RetryPredicateError");
                        collectedErrors.Add(predErr);
                        InvokeOnRetryHook(onRetryFn, onRetryParamAttempt, onRetryParamError, onRetryBody,
                            currentAttempt, predErr, currentAttempt < maxAttempts);
                    }
                }
                else
                {
                    return result;
                }
            }

            // Delay before next attempt (skip after last attempt)
            if (currentAttempt < maxAttempts && delayMs > 0)
            {
                long computedDelay = ComputeRetryDelay(delayMs, backoff, currentAttempt, maxDelayMs, jitter, random);
                if (computedDelay > 0)
                    Thread.Sleep((int)Math.Min(computedDelay, int.MaxValue));
            }
        }

        // Exhaustion
        if (lastFailureWasException && lastError is not null)
        {
            throw lastError;
        }
        else
        {
            throw new RuntimeError(
                $"All {maxAttempts} retry attempts exhausted \u2014 predicate not satisfied",
                expr.Span,
                errorType: "RetryExhaustedError")
            {
                Properties = new Dictionary<string, object?>
                {
                    ["attempts"] = maxAttempts,
                    ["lastValue"] = lastValue,
                    ["errors"] = new List<object?>(collectedErrors)
                }
            };
        }
    }

    private object? ExecuteRetryBody(BlockStmt body, Environment env)
    {
        Environment previous = _environment;
        try
        {
            _environment = env;
            object? result = null;
            for (int i = 0; i < body.Statements.Count; i++)
            {
                Stmt stmt = body.Statements[i];
                if (i == body.Statements.Count - 1 && stmt is ExprStmt exprStmt)
                {
                    result = Evaluate(exprStmt.Expression);
                }
                else
                {
                    Execute(stmt);
                }
            }
            return result;
        }
        finally
        {
            _environment = previous;
        }
    }

    private static long ExtractDurationMs(object? value, string optionName, SourceSpan span)
    {
        if (value is StashDuration dur) return dur.TotalMilliseconds;
        if (value is long l) return l * 1000;
        if (value is double d) return (long)(d * 1000);
        throw new RuntimeError($"Option '{optionName}' must be a duration, integer, or float.", span);
    }

    private void ExtractRetryOptionsFromInstance(StashInstance inst, ref long delayMs, ref string backoff,
        ref long maxDelayMs, ref bool jitter, ref long timeoutMs, ref List<string>? onTypes, SourceSpan span)
    {
        try { var v = inst.GetField("delay", null); if (v is not null) delayMs = ExtractDurationMs(v, "delay", span); } catch (RuntimeError) { }
        try { var v = inst.GetField("backoff", null); if (v is StashEnumValue bev) backoff = bev.MemberName; } catch (RuntimeError) { }
        try { var v = inst.GetField("maxDelay", null); if (v is not null) maxDelayMs = ExtractDurationMs(v, "maxDelay", span); } catch (RuntimeError) { }
        try { var v = inst.GetField("jitter", null); if (v is bool j) jitter = j; } catch (RuntimeError) { }
        try { var v = inst.GetField("timeout", null); if (v is not null) timeoutMs = ExtractDurationMs(v, "timeout", span); } catch (RuntimeError) { }
        try
        {
            var v = inst.GetField("on", null);
            if (v is List<object?> typeList)
            {
                onTypes = new List<string>();
                foreach (object? item in typeList)
                {
                    if (item is string s) onTypes.Add(s);
                    else onTypes.Add(RuntimeValues.Stringify(item));
                }
            }
        }
        catch (RuntimeError) { }
    }

    private void InvokeOnRetryHook(IStashCallable? onRetryFn, Token? paramAttempt, Token? paramError,
        BlockStmt? onRetryBody, long attemptNumber, object? error, bool hasMoreAttempts)
    {
        if (!hasMoreAttempts) return;

        if (onRetryFn is not null)
        {
            onRetryFn.Call(this, new List<object?> { attemptNumber, error });
        }
        else if (onRetryBody is not null)
        {
            var hookEnv = new Environment(_environment);
            if (paramAttempt is not null)
                hookEnv.Define(paramAttempt.Lexeme, attemptNumber);
            if (paramError is not null)
                hookEnv.Define(paramError.Lexeme, error);
            ExecuteBlock(onRetryBody.Statements, hookEnv);
        }
    }

    private static long ComputeRetryDelay(long baseDelay, string backoff, long attempt, long maxDelay, bool jitter, Random? random)
    {
        long delay = backoff switch
        {
            "Linear" => baseDelay * attempt,
            "Exponential" => baseDelay * (1L << (int)Math.Min(attempt - 1, 30)),
            _ => baseDelay,
        };

        if (delay > maxDelay) delay = maxDelay;

        if (jitter && random is not null)
        {
            double factor = 0.75 + (random.NextDouble() * 0.5);
            delay = (long)(delay * factor);
        }

        return delay;
    }

    private static string FormatDuration(long ms)
    {
        if (ms < 1000) return $"{ms}ms";
        if (ms < 60_000) return $"{ms / 1000.0:F1}s";
        return $"{ms / 60_000.0:F1}m";
    }

    /// <inheritdoc />
    public object? VisitNullCoalesceExpr(NullCoalesceExpr expr)
    {
        object? left = Evaluate(expr.Left);
        if (left is not null and not StashError)
        {
            return left;
        }
        return Evaluate(expr.Right);
    }

    /// <inheritdoc />
    public object? VisitRangeExpr(RangeExpr expr)
    {
        object? startVal = Evaluate(expr.Start);
        object? endVal = Evaluate(expr.End);

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
            object? stepVal = Evaluate(expr.Step);
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
        object? value = Evaluate(expr.Value);
        if (expr.ResolvedDistance >= 0)
        {
            _environment.SetAtSlot(expr.ResolvedDistance, expr.ResolvedSlot, expr.Name.Lexeme, value, expr.Name.Span);
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
        object? callee = Evaluate(expr.Callee);

        // Optional chaining: x?.method() returns null when x is null
        if (expr.IsOptional && callee is null)
        {
            return null;
        }

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
                if (argument is SpreadExpr spreadArg)
                {
                    object? spreadValue = Evaluate(spreadArg.Expression);
                    if (spreadValue is not List<object?> spreadList)
                    {
                        throw new RuntimeError("Cannot spread non-array value in function call.", spreadArg.Span);
                    }
                    arguments.AddRange(spreadList);
                }
                else
                {
                    arguments.Add(Evaluate(argument));
                }
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
        else if (function is UserCallable userCallable && arguments.Count < userCallable.MinArity)
        {
            throw new RuntimeError(
                $"Expected at least {userCallable.MinArity} arguments but got {arguments.Count}.",
                expr.Paren.Span);
        }

        if (_debugger == null)
        {
            return function.Call(this, arguments);
        }

        string functionName = callee.ToString() ?? "<unknown>";
        if (functionName.StartsWith("<fn ") && functionName.EndsWith(">"))
        {
            functionName = functionName.Substring(4, functionName.Length - 5);
        }
        else if (functionName.StartsWith("<built-in fn ") && functionName.EndsWith(">"))
        {
            functionName = functionName.Substring(13, functionName.Length - 14);
        }

        SourceSpan? functionSpan = callee is UserCallable uc ? uc.DefinitionSpan : null;

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
            ? Evaluate(expr.Target)
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

            fieldValues[fieldName] = Evaluate(valueExpr);
        }

        return new StashInstance(template.Name, template, fieldValues);
    }

    /// <inheritdoc />
    public object? VisitDotExpr(DotExpr expr)
    {
        object? obj = Evaluate(expr.Object);

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
                _ => error.Properties is not null && error.Properties.TryGetValue(expr.Name.Lexeme, out object? propValue)
                    ? propValue
                    : throw new RuntimeError($"Error has no field '{expr.Name.Lexeme}'.", expr.Name.Span)
            };
        }

        if (obj is StashDuration dur)
        {
            // Explicit (object) casts prevent C# switch type unification from promoting long → double.
            return expr.Name.Lexeme switch
            {
                "totalMs" => (object)dur.TotalMilliseconds,
                "totalSeconds" => dur.TotalSeconds,
                "totalMinutes" => dur.TotalMinutes,
                "totalHours" => dur.TotalHours,
                "totalDays" => dur.TotalDays,
                "milliseconds" => (object)dur.Milliseconds,
                "seconds" => (object)dur.Seconds,
                "minutes" => (object)dur.Minutes,
                "hours" => (object)dur.Hours,
                "days" => (object)dur.Days,
                _ => throw new RuntimeError(
                    $"Duration has no field '{expr.Name.Lexeme}'. Available fields: totalMs, totalSeconds, totalMinutes, totalHours, totalDays, milliseconds, seconds, minutes, hours, days.",
                    expr.Name.Span)
            };
        }

        if (obj is StashByteSize bs)
        {
            return expr.Name.Lexeme switch
            {
                "bytes" => (object)bs.TotalBytes,
                "kb" => bs.Kb,
                "mb" => bs.Mb,
                "gb" => bs.Gb,
                "tb" => bs.Tb,
                _ => throw new RuntimeError(
                    $"Byte size has no field '{expr.Name.Lexeme}'. Available fields: bytes, kb, mb, gb, tb.",
                    expr.Name.Span)
            };
        }

        if (obj is StashSemVer sv)
        {
            return expr.Name.Lexeme switch
            {
                "major" => (object)sv.Major,
                "minor" => (object)sv.Minor,
                "patch" => (object)sv.Patch,
                "prerelease" => sv.Prerelease ?? (object)"",
                "build" => sv.BuildMetadata ?? (object)"",
                "isPrerelease" => sv.IsPrerelease,
                _ => throw new RuntimeError(
                    $"Semver has no field '{expr.Name.Lexeme}'. Available fields: major, minor, patch, prerelease, build, isPrerelease.",
                    expr.Name.Span)
            };
        }

        if (obj is StashInstance instance)
        {
            return instance.GetField(expr.Name.Lexeme, expr.Name.Span);
        }

        if (obj is StashDictionary dict)
        {
            // Check for dict extension methods before key lookup
            if (ExtensionRegistry.TryGetMethod("dict", expr.Name.Lexeme, out IStashCallable? dictExtMethod) &&
                dictExtMethod is StashFunction dictExtFunc)
            {
                return new ExtensionBoundMethod(obj, dictExtFunc);
            }

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

        // Extension method lookup (priority: after namespace, before UFCS)
        string? extTypeName = obj switch
        {
            StashInstance inst => inst.TypeName,
            string => "string",
            List<object?> => "array",
            StashDictionary => "dict",
            long => "int",
            double => "float",
            _ => null
        };

        if (extTypeName is not null && ExtensionRegistry.TryGetMethod(extTypeName, expr.Name.Lexeme, out IStashCallable? extMethod))
        {
            if (extMethod is StashFunction extFunc)
            {
                return new ExtensionBoundMethod(obj, extFunc);
            }
        }

        // UFCS: allow namespace functions to be called as methods on strings and arrays
        string? ufcsNamespaceName = obj switch
        {
            string => "str",
            List<object?> => "arr",
            _ => null
        };

        if (ufcsNamespaceName is not null)
        {
            string methodName = expr.Name.Lexeme;
            if (_globals.TryGet(ufcsNamespaceName, out object? nsValue) &&
                nsValue is StashNamespace ufcsNs && ufcsNs.HasMember(methodName))
            {
                object? member = ufcsNs.GetMember(methodName, expr.Name.Span);
                if (member is IStashCallable callable)
                {
                    return new BuiltInBoundMethod(obj, callable);
                }
            }

            string typeName = ufcsNamespaceName == "str" ? "string" : "array";
            throw new RuntimeError($"No method '{methodName}' on type '{typeName}'.", expr.Name.Span);
        }

        string objType = obj switch
        {
            long => "int",
            double => "float",
            bool => "bool",
            null => "null",
            _ => obj.GetType().Name
        };
        throw new RuntimeError($"Type '{objType}' does not support member access.", expr.Name.Span);
    }

    /// <inheritdoc />
    public object? VisitDotAssignExpr(DotAssignExpr expr)
    {
        object? obj = Evaluate(expr.Object);

        if (obj is StashNamespace)
        {
            throw new RuntimeError("Cannot assign to namespace members.", expr.Name.Span);
        }

        if (obj is StashDictionary dict)
        {
            object? value = Evaluate(expr.Value);
            dict.Set(expr.Name.Lexeme, value);
            return value;
        }

        if (obj is not StashInstance instance)
        {
            throw new RuntimeError("Only struct instances and dictionaries have fields.", expr.Name.Span);
        }

        object? assignValue = Evaluate(expr.Value);
        instance.SetField(expr.Name.Lexeme, assignValue, expr.Name.Span);
        return assignValue;
    }

    /// <inheritdoc />
    public object? VisitInterpolatedStringExpr(InterpolatedStringExpr expr)
    {
        var sb = new StringBuilder();
        foreach (Expr part in expr.Parts)
        {
            object? value = Evaluate(part);
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
            if (element is SpreadExpr spread)
            {
                object? spreadValue = Evaluate(spread.Expression);
                if (spreadValue is not List<object?> spreadList)
                {
                    throw new RuntimeError("Cannot spread non-array value into array literal.", spread.Span);
                }
                elements.AddRange(spreadList);
            }
            else
            {
                elements.Add(Evaluate(element));
            }
        }
        return elements;
    }

    /// <inheritdoc />
    public object? VisitDictLiteralExpr(DictLiteralExpr expr)
    {
        var dict = new StashDictionary();
        foreach (var (key, value) in expr.Entries)
        {
            if (key == null)
            {
                // Spread entry: ...expr
                object? spreadValue = value is SpreadExpr spread ? Evaluate(spread.Expression) : Evaluate(value);
                if (spreadValue is StashDictionary spreadDict)
                {
                    foreach (var kvp in spreadDict.GetAllEntries())
                    {
                        dict.Set(kvp.Key, kvp.Value);
                    }
                }
                else if (spreadValue is StashInstance instance)
                {
                    foreach (var kvp in instance.GetFields())
                    {
                        dict.Set(kvp.Key, kvp.Value);
                    }
                }
                else
                {
                    SourceSpan errorSpan = value is SpreadExpr s ? s.Span : value.Span;
                    throw new RuntimeError("Cannot spread value into dictionary literal. Expected dictionary or struct instance.", errorSpan);
                }
            }
            else
            {
                dict.Set(key.Lexeme, Evaluate(value));
            }
        }
        return dict;
    }

    /// <inheritdoc />
    public object? VisitIndexExpr(IndexExpr expr)
    {
        object? obj = Evaluate(expr.Object);
        object? index = Evaluate(expr.Index);

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
        object? obj = Evaluate(expr.Object);
        object? index = Evaluate(expr.Index);
        object? value = Evaluate(expr.Value);

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

        if (left is StashIpAddress ipPlus && right is long offsetPlus)
        {
            return ipPlus.Add(offsetPlus);
        }

        if (left is long offsetPlusL && right is StashIpAddress ipPlusR)
        {
            return ipPlusR.Add(offsetPlusL);
        }

        if (left is StashDuration durPlusL && right is StashDuration durPlusR)
        {
            return durPlusL.Add(durPlusR);
        }

        if (left is StashByteSize bsPlusL && right is StashByteSize bsPlusR)
        {
            return bsPlusL.Add(bsPlusR);
        }

        if ((left is StashDuration || right is StashDuration) || (left is StashByteSize || right is StashByteSize))
        {
            throw new RuntimeError("Cannot mix duration or byte size with other types in addition.", expr.Operator.Span);
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
        // Duration / number → duration, Duration / duration → float (ratio)
        if (left is StashDuration durDivL)
        {
            if (right is StashDuration durDivR)
            {
                return durDivL.DivideBy(durDivR);
            }
            if (IsNumeric(right))
            {
                return durDivL.Divide(ToDouble(right));
            }
            throw new RuntimeError("Duration can only be divided by a number or another duration.", expr.Operator.Span);
        }

        // ByteSize / number → bytes, ByteSize / bytes → float (ratio)
        if (left is StashByteSize bsDivL)
        {
            if (right is StashByteSize bsDivR)
            {
                return bsDivL.DivideBy(bsDivR);
            }
            if (IsNumeric(right))
            {
                return bsDivL.Divide(ToDouble(right));
            }
            throw new RuntimeError("Byte size can only be divided by a number or another byte size.", expr.Operator.Span);
        }

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
                if (id.ResolvedDistance >= 0)
                {
                    object? val = _environment.GetAtSlot(id.ResolvedDistance, id.ResolvedSlot);
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

            case UnaryExpr u when u.Operator.Type == TokenType.Tilde:
                if (TryEvalLong(u.Right, out long tRes))
                {
                    result = ~tRes;
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
                        case TokenType.Ampersand: result = left & right; return true;
                        case TokenType.Pipe: result = left | right; return true;
                        case TokenType.Caret: result = left ^ right; return true;
                        case TokenType.LessLess:
                            if (right < 0 || right > 63) break;
                            result = left << (int)right; return true;
                        case TokenType.GreaterGreater:
                            if (right < 0 || right > 63) break;
                            result = left >> (int)right; return true;
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

        return IsTruthy(Evaluate(condition));
    }

    /// <summary>
    /// Reads a long value from a leaf expression (literal or resolved identifier) without boxing.
    /// Does not evaluate sub-expressions — only handles leaf nodes for minimal overhead.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private bool TryReadLong(Expr expr, out long value)
    {
        if (expr is IdentifierExpr id && id.ResolvedDistance >= 0)
        {
            object? v = _environment.GetAtSlot(id.ResolvedDistance, id.ResolvedSlot);
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

    /// <inheritdoc />
    public object? VisitSpreadExpr(SpreadExpr expr)
    {
        throw new RuntimeError("Spread operator '...' can only appear inside function calls, array literals, or dictionary literals.", expr.Span);
    }

}
