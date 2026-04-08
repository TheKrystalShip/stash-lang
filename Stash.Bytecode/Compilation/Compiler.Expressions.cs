using System;
using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;
using Stash.Parsing.AST;
using Stash.Runtime.Types;

namespace Stash.Bytecode;

/// <summary>
/// Core expression visitor implementations.
/// </summary>
public sealed partial class Compiler
{
    // =========================================================================
    // Expression Visitors
    // =========================================================================

    /// <inheritdoc />
    public object? VisitLiteralExpr(LiteralExpr expr)
    {
        object? value = expr.Value;
        switch (value)
        {
            case null:
                _builder.Emit(OpCode.Null);
                break;
            case true:
                _builder.Emit(OpCode.True);
                break;
            case false:
                _builder.Emit(OpCode.False);
                break;
            default:
                ushort idx = _builder.AddConstant(value);
                _builder.Emit(OpCode.Const, idx);
                break;
        }
        return null;
    }

    /// <inheritdoc />
    public object? VisitIdentifierExpr(IdentifierExpr expr)
    {
        EmitVariable(expr.Name.Lexeme, expr.ResolvedDistance, expr.ResolvedSlot, isLoad: true);
        return null;
    }

    /// <inheritdoc />
    public object? VisitGroupingExpr(GroupingExpr expr)
    {
        CompileExpr(expr.Expression);
        return null;
    }

    /// <inheritdoc />
    public object? VisitUnaryExpr(UnaryExpr expr)
    {
        // Compile-time constant folding for unary operators
        if (TryEvaluateConstant(expr, out object? folded))
        {
            EmitFoldedConstant(folded);
            return null;
        }

        _builder.AddSourceMapping(expr.Span);
        CompileExpr(expr.Right);
        switch (expr.Operator.Type)
        {
            case TokenType.Minus:
                _builder.Emit(OpCode.Negate);
                break;
            case TokenType.Bang:
                _builder.Emit(OpCode.Not);
                break;
            case TokenType.Tilde:
                _builder.Emit(OpCode.BitNot);
                break;
            default:
                throw new CompileError(
                    $"Unknown unary operator '{expr.Operator.Lexeme}'.", expr.Operator.Span);
        }
        return null;
    }

    /// <inheritdoc />
    public object? VisitBinaryExpr(BinaryExpr expr)
    {
        // Compile-time constant folding: recursive evaluation
        if (TryEvaluateConstant(expr, out object? folded))
        {
            EmitFoldedConstant(folded);
            return null;
        }

        // Short-circuit AND — if left is falsy, skip right and leave left on stack
        if (expr.Operator.Type == TokenType.AmpersandAmpersand)
        {
            CompileExpr(expr.Left);
            int endJump = _builder.EmitJump(OpCode.And);
            CompileExpr(expr.Right);
            _builder.PatchJump(endJump);
            return null;
        }

        // Short-circuit OR — if left is truthy, skip right and leave left on stack
        if (expr.Operator.Type == TokenType.PipePipe)
        {
            CompileExpr(expr.Left);
            int endJump = _builder.EmitJump(OpCode.Or);
            CompileExpr(expr.Right);
            _builder.PatchJump(endJump);
            return null;
        }

        _builder.AddSourceMapping(expr.Span);
        CompileExpr(expr.Left);
        CompileExpr(expr.Right);

        OpCode op = expr.Operator.Type switch
        {
            TokenType.Plus           => OpCode.Add,
            TokenType.Minus          => OpCode.Subtract,
            TokenType.Star           => OpCode.Multiply,
            TokenType.Slash          => OpCode.Divide,
            TokenType.Percent        => OpCode.Modulo,
            TokenType.EqualEqual     => OpCode.Equal,
            TokenType.BangEqual      => OpCode.NotEqual,
            TokenType.Less           => OpCode.LessThan,
            TokenType.Greater        => OpCode.GreaterThan,
            TokenType.LessEqual      => OpCode.LessEqual,
            TokenType.GreaterEqual   => OpCode.GreaterEqual,
            TokenType.Ampersand      => OpCode.BitAnd,
            TokenType.Pipe           => OpCode.BitOr,
            TokenType.Caret          => OpCode.BitXor,
            TokenType.LessLess       => OpCode.ShiftLeft,
            TokenType.GreaterGreater => OpCode.ShiftRight,
            TokenType.In             => OpCode.In,
            _ => throw new CompileError(
                     $"Unsupported binary operator '{expr.Operator.Lexeme}'.", expr.Operator.Span),
        };
        _builder.Emit(op);
        return null;
    }

    /// <summary>
    /// Attempts to evaluate a binary operation on two literal values at compile time.
    /// Returns the result if successful, or null if the operation cannot be folded.
    /// </summary>
    private static object? TryFoldBinary(object? left, object? right, TokenType op)
    {
        // Numeric arithmetic (int × int → int, anything with float → float)
        if (left is long li && right is long ri)
        {
            return op switch
            {
                TokenType.Plus           => li + ri,
                TokenType.Minus          => li - ri,
                TokenType.Star           => li * ri,
                TokenType.Slash          when ri != 0              => li / ri,
                TokenType.Percent        when ri != 0              => li % ri,
                TokenType.EqualEqual     => li == ri,
                TokenType.BangEqual      => li != ri,
                TokenType.Less           => li < ri,
                TokenType.Greater        => li > ri,
                TokenType.LessEqual      => li <= ri,
                TokenType.GreaterEqual   => li >= ri,
                TokenType.Ampersand      => li & ri,
                TokenType.Pipe           => li | ri,
                TokenType.Caret          => li ^ ri,
                TokenType.LessLess       when ri >= 0 && ri < 64   => li << (int)ri,
                TokenType.GreaterGreater when ri >= 0 && ri < 64   => li >> (int)ri,
                _ => null,
            };
        }

        // Float arithmetic
        double? ld = left  is long ll ? ll : left  is double dl ? dl : null;
        double? rd = right is long rl ? rl : right is double dr ? dr : null;
        if (ld.HasValue && rd.HasValue && (left is double || right is double))
        {
            return op switch
            {
                TokenType.Plus         => ld.Value + rd.Value,
                TokenType.Minus        => ld.Value - rd.Value,
                TokenType.Star         => ld.Value * rd.Value,
                TokenType.Slash        when rd.Value != 0.0 => ld.Value / rd.Value,
                TokenType.Percent      when rd.Value != 0.0 => ld.Value % rd.Value,
                TokenType.EqualEqual   => ld.Value == rd.Value,
                TokenType.BangEqual    => ld.Value != rd.Value,
                TokenType.Less         => ld.Value < rd.Value,
                TokenType.Greater      => ld.Value > rd.Value,
                TokenType.LessEqual    => ld.Value <= rd.Value,
                TokenType.GreaterEqual => ld.Value >= rd.Value,
                _ => null,
            };
        }

        // String concatenation
        if (left is string ls && right is string rs && op == TokenType.Plus)
        {
            return ls + rs;
        }

        // Boolean equality
        if (left is bool lb && right is bool rb)
        {
            return op switch
            {
                TokenType.EqualEqual => lb == rb,
                TokenType.BangEqual  => lb != rb,
                _ => null,
            };
        }

        return null;
    }

    /// <summary>Emits a single instruction for a compile-time-folded constant value.</summary>
    private void EmitFoldedConstant(object? value)
    {
        switch (value)
        {
            case null:
                _builder.Emit(OpCode.Null);
                break;
            case true:
                _builder.Emit(OpCode.True);
                break;
            case false:
                _builder.Emit(OpCode.False);
                break;
            default:
                ushort idx = _builder.AddConstant(value);
                _builder.Emit(OpCode.Const, idx);
                break;
        }
    }

    /// <summary>
    /// Recursively evaluates an expression at compile time. Returns true and the
    /// computed value if every leaf is compile-time-known, false otherwise.
    /// </summary>
    private bool TryEvaluateConstant(Expr expr, out object? value)
    {
        switch (expr)
        {
            case LiteralExpr lit:
                value = lit.Value;
                return true;

            case GroupingExpr grp:
                return TryEvaluateConstant(grp.Expression, out value);

            case UnaryExpr unary:
                if (!TryEvaluateConstant(unary.Right, out object? operand))
                {
                    value = null;
                    return false;
                }
                value = unary.Operator.Type switch
                {
                    TokenType.Minus when operand is long l   => (object)(-l),
                    TokenType.Minus when operand is double d => (object)(-d),
                    TokenType.Bang  when operand is bool b   => (object)(!b),
                    TokenType.Tilde when operand is long l   => (object)(~l),
                    _ => null,
                };
                return value is not null;

            case BinaryExpr bin:
                // Short-circuit AND
                if (bin.Operator.Type == TokenType.AmpersandAmpersand)
                {
                    if (!TryEvaluateConstant(bin.Left, out object? left))
                    {
                        value = null;
                        return false;
                    }
                    if (CompileTimeIsFalsy(left))
                    {
                        value = left;
                        return true;
                    }
                    return TryEvaluateConstant(bin.Right, out value);
                }

                // Short-circuit OR
                if (bin.Operator.Type == TokenType.PipePipe)
                {
                    if (!TryEvaluateConstant(bin.Left, out object? left))
                    {
                        value = null;
                        return false;
                    }
                    if (!CompileTimeIsFalsy(left))
                    {
                        value = left;
                        return true;
                    }
                    return TryEvaluateConstant(bin.Right, out value);
                }

                // Regular binary ops
                if (TryEvaluateConstant(bin.Left, out object? lhs) &&
                    TryEvaluateConstant(bin.Right, out object? rhs))
                {
                    value = TryFoldBinary(lhs, rhs, bin.Operator.Type);
                    return value is not null;
                }
                value = null;
                return false;

            case IdentifierExpr id when id.ResolvedDistance == -1:
                if (_globalSlots.TryGetConstValue(id.Name.Lexeme, out object? constVal))
                {
                    value = constVal;
                    return true;
                }
                value = null;
                return false;

            case InterpolatedStringExpr interp:
                var sb = new System.Text.StringBuilder();
                foreach (Expr part in interp.Parts)
                {
                    if (!TryEvaluateConstant(part, out object? partVal))
                    {
                        value = null;
                        return false;
                    }
                    sb.Append(CompileTimeStringify(partVal));
                }
                value = sb.ToString();
                return true;

            case TernaryExpr ternary:
                if (!TryEvaluateConstant(ternary.Condition, out object? condVal))
                {
                    value = null;
                    return false;
                }
                return CompileTimeIsFalsy(condVal)
                    ? TryEvaluateConstant(ternary.ElseBranch, out value)
                    : TryEvaluateConstant(ternary.ThenBranch, out value);

            default:
                value = null;
                return false;
        }
    }

    /// <summary>
    /// Mirrors RuntimeOps.IsFalsy for compile-time constant values (boxed as object?).
    /// </summary>
    private static bool CompileTimeIsFalsy(object? value) => value switch
    {
        null => true,
        bool b => !b,
        long l => l == 0,
        double d => d == 0.0,
        string s => s.Length == 0,
        _ => false,
    };

    /// <summary>
    /// Converts a compile-time constant value to its string representation,
    /// matching the runtime Stringify behavior.
    /// </summary>
    private static string CompileTimeStringify(object? value) => value switch
    {
        string s => s,
        long l => l.ToString(),
        double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        null => "null",
        _ => value.ToString() ?? "",
    };

    /// <inheritdoc />
    public object? VisitTernaryExpr(TernaryExpr expr)
    {
        // Compile-time branch selection for constant conditions
        if (TryEvaluateConstant(expr.Condition, out object? condValue))
        {
            if (!CompileTimeIsFalsy(condValue))
                CompileExpr(expr.ThenBranch);
            else
                CompileExpr(expr.ElseBranch);
            return null;
        }

        _builder.AddSourceMapping(expr.Span);
        CompileExpr(expr.Condition);
        int elseJump = _builder.EmitJump(OpCode.JumpFalse);
        CompileExpr(expr.ThenBranch);
        int endJump = _builder.EmitJump(OpCode.Jump);
        _builder.PatchJump(elseJump);
        CompileExpr(expr.ElseBranch);
        _builder.PatchJump(endJump);
        return null;
    }

    /// <inheritdoc />
    public object? VisitAssignExpr(AssignExpr expr)
    {
        _builder.AddSourceMapping(expr.Span);
        CompileExpr(expr.Value);
        // DUP so that the value remains on the stack as the result of the expression after the store
        _builder.Emit(OpCode.Dup);
        EmitVariable(expr.Name.Lexeme, expr.ResolvedDistance, expr.ResolvedSlot, isLoad: false);
        return null;
    }

    /// <inheritdoc />
    /// <inheritdoc />
    public object? VisitCallExpr(CallExpr expr)
    {
        _builder.AddSourceMapping(expr.Span);

        CompileExpr(expr.Callee);

        if (expr.IsOptional)
        {
            // Optional call: if callee is null, short-circuit to null
            _builder.Emit(OpCode.Dup);
            _builder.Emit(OpCode.Null);
            _builder.Emit(OpCode.Equal);
            int nullJump = _builder.EmitJump(OpCode.JumpTrue);

            // Callee is not null — compile and execute call
            bool hasSpreadOpt = false;
            foreach (Expr arg in expr.Arguments)
            {
                if (arg is SpreadExpr)
                {
                    hasSpreadOpt = true;
                    break;
                }
            }

            if (hasSpreadOpt)
            {
                _builder.Emit(OpCode.ArgMark);
                foreach (Expr arg in expr.Arguments)
                {
                    CompileExpr(arg);
                }

                _builder.Emit(OpCode.CallSpread);
            }
            else
            {
                foreach (Expr arg in expr.Arguments)
                {
                    CompileExpr(arg);
                }

                _builder.Emit(OpCode.Call, (byte)expr.Arguments.Count);
            }
            int endJump = _builder.EmitJump(OpCode.Jump);

            // Callee was null — pop callee, push null as result
            _builder.PatchJump(nullJump);
            _builder.Emit(OpCode.Pop); // pop the callee (null)
            _builder.Emit(OpCode.Null); // push null as result

            _builder.PatchJump(endJump);
            return null;
        }

        // Non-optional: compile args, emit call
        bool hasSpread = false;
        foreach (Expr arg in expr.Arguments)
        {
            if (arg is SpreadExpr)
            {
                hasSpread = true;
                break;
            }
        }

        if (hasSpread)
        {
            _builder.Emit(OpCode.ArgMark);
            foreach (Expr arg in expr.Arguments)
            {
                CompileExpr(arg);
            }

            _builder.Emit(OpCode.CallSpread);
        }
        else
        {
            foreach (Expr arg in expr.Arguments)
            {
                CompileExpr(arg);
            }

            _builder.Emit(OpCode.Call, (byte)expr.Arguments.Count);
        }
        return null;
    }

    /// <inheritdoc />
    public object? VisitDotExpr(DotExpr expr)
    {
        _builder.AddSourceMapping(expr.Span);
        CompileExpr(expr.Object);
        ushort nameIdx = _builder.AddConstant(expr.Name.Lexeme);

        if (expr.IsOptional)
        {
            // If object is null, short-circuit to null (leave null on stack)
            _builder.Emit(OpCode.Dup);
            _builder.Emit(OpCode.Null);
            _builder.Emit(OpCode.Equal);
            int nullJump = _builder.EmitJump(OpCode.JumpTrue);

            // Not null — do field access
            ushort icSlot1 = _builder.AllocateICSlot();
            _builder.Emit(OpCode.GetFieldIC, nameIdx, icSlot1);
            int endJump = _builder.EmitJump(OpCode.Jump);

            // Was null — object is already null on stack, which is the result
            _builder.PatchJump(nullJump);

            _builder.PatchJump(endJump);
        }
        else
        {
            ushort icSlot = _builder.AllocateICSlot();
            _builder.Emit(OpCode.GetFieldIC, nameIdx, icSlot);
        }

        return null;
    }

    /// <inheritdoc />
    public object? VisitDotAssignExpr(DotAssignExpr expr)
    {
        _builder.AddSourceMapping(expr.Span);
        CompileExpr(expr.Object);
        CompileExpr(expr.Value);
        ushort nameIdx = _builder.AddConstant(expr.Name.Lexeme);
        _builder.Emit(OpCode.SetField, nameIdx);
        return null;
    }

    /// <inheritdoc />
    public object? VisitTryExpr(TryExpr expr)
    {
        _builder.AddSourceMapping(expr.Span);
        // Set up an exception handler that catches to the "null" branch
        int catchJump = _builder.EmitJump(OpCode.TryBegin);
        CompileExpr(expr.Expression);
        _builder.Emit(OpCode.TryEnd);
        int endJump = _builder.EmitJump(OpCode.Jump);
        _builder.PatchJump(catchJump);
        // VM already pushed the StashError onto the stack — leave it as the expression result
        _builder.PatchJump(endJump);
        return null;
    }

    /// <inheritdoc />
    public object? VisitNullCoalesceExpr(NullCoalesceExpr expr)
    {
        CompileExpr(expr.Left);
        int endJump = _builder.EmitJump(OpCode.NullCoalesce);
        CompileExpr(expr.Right);
        _builder.PatchJump(endJump);
        return null;
    }

    /// <inheritdoc />
    public object? VisitSwitchExpr(SwitchExpr expr)
    {
        _builder.AddSourceMapping(expr.Span);
        CompileExpr(expr.Subject);

        var endJumps = new List<int>();
        bool hasDefault = false;

        foreach (SwitchArm arm in expr.Arms)
        {
            if (arm.IsDiscard)
            {
                // Default arm — pop subject, evaluate and leave result
                hasDefault = true;
                _builder.Emit(OpCode.Pop);
                CompileExpr(arm.Body);
            }
            else
            {
                // Pattern arm — duplicate subject, compare, branch
                _builder.Emit(OpCode.Dup);
                CompileExpr(arm.Pattern!);
                _builder.Emit(OpCode.Equal);
                int nextArm = _builder.EmitJump(OpCode.JumpFalse);
                _builder.Emit(OpCode.Pop);   // pop subject (matched)
                CompileExpr(arm.Body);
                endJumps.Add(_builder.EmitJump(OpCode.Jump));
                _builder.PatchJump(nextArm);
            }
        }

        if (!hasDefault)
        {
            _builder.Emit(OpCode.Pop);
            ushort msgIdx = _builder.AddConstant("No matching case in switch expression.");
            _builder.Emit(OpCode.Const, msgIdx);
            _builder.Emit(OpCode.Throw);
        }

        foreach (int endJump in endJumps)
        {
            _builder.PatchJump(endJump);
        }

        return null;
    }

    /// <inheritdoc />
    public object? VisitRedirectExpr(RedirectExpr expr)
    {
        _builder.AddSourceMapping(expr.Span);
        CompileExpr(expr.Expression);
        CompileExpr(expr.Target);
        // Encode stream (bits 0-1) and append flag (bit 2)
        byte flags = (byte)expr.Stream;
        if (expr.Append)
        {
            flags |= 0x04;
        }

        _builder.Emit(OpCode.Redirect, flags);
        return null;
    }

    /// <inheritdoc />
    public object? VisitIsExpr(IsExpr expr)
    {
        _builder.AddSourceMapping(expr.Span);
        CompileExpr(expr.Left);
        if (expr.TypeName != null)
        {
            string name = expr.TypeName.Lexeme;
            // If the identifier resolves as a local variable, emit a dynamic type check
            // so that variables holding struct/interface/enum definitions are handled correctly.
            int localSlot = _scope.ResolveLocal(name);
            if (localSlot >= 0)
            {
                _builder.Emit(OpCode.LoadLocal, (byte)localSlot);
                _builder.Emit(OpCode.Is, (ushort)0xFFFF);
            }
            else
            {
                // Built-in or global — emit static type name; VM resolves globals holding type defs.
                ushort typeIdx = _builder.AddConstant(name);
                _builder.Emit(OpCode.Is, typeIdx);
            }
        }
        else if (expr.TypeExpr != null)
        {
            CompileExpr(expr.TypeExpr);
            _builder.Emit(OpCode.Is, (ushort)0xFFFF);  // sentinel: dynamic type check
        }
        return null;
    }

    /// <inheritdoc />
    public object? VisitAwaitExpr(AwaitExpr expr)
    {
        _builder.AddSourceMapping(expr.Span);
        CompileExpr(expr.Expression);
        _builder.Emit(OpCode.Await);
        return null;
    }

    /// <inheritdoc />
    public object? VisitSpreadExpr(SpreadExpr expr)
    {
        CompileExpr(expr.Expression);
        _builder.Emit(OpCode.Spread);
        return null;
    }

}
