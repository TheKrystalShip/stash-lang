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

    /// <inheritdoc />
    public object? VisitTernaryExpr(TernaryExpr expr)
    {
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
            _builder.Emit(OpCode.GetField, nameIdx);
            int endJump = _builder.EmitJump(OpCode.Jump);

            // Was null — object is already null on stack, which is the result
            _builder.PatchJump(nullJump);

            _builder.PatchJump(endJump);
        }
        else
        {
            _builder.Emit(OpCode.GetField, nameIdx);
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
