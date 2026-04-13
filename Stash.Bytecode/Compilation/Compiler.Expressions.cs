using System;
using System.Collections.Generic;
using System.Linq;
using Stash.Common;
using Stash.Lexing;
using Stash.Parsing.AST;
using Stash.Runtime;

namespace Stash.Bytecode;

partial class Compiler
{
    // =========================================================================
    // Expression Visitors
    // =========================================================================

    public object? VisitLiteralExpr(LiteralExpr expr)
    {
        byte dest = _destReg;
        object? value = expr.Value;

        if (value == null)
        {
            _builder.EmitA(OpCode.LoadNull, dest);
        }
        else if (value is bool b)
        {
            _builder.EmitABC(OpCode.LoadBool, dest, b ? (byte)1 : (byte)0, 0);
        }
        else
        {
            ushort idx = _builder.AddConstant(StashValue.FromObject(value));
            _builder.EmitABx(OpCode.LoadK, dest, idx);
        }
        return null;
    }

    public object? VisitIdentifierExpr(IdentifierExpr expr)
    {
        byte dest = _destReg;
        EmitVariable(expr.Name.Lexeme, expr.ResolvedDistance, expr.ResolvedSlot, isLoad: true, dest);
        return null;
    }

    public object? VisitGroupingExpr(GroupingExpr expr)
    {
        CompileExprTo(expr.Expression, _destReg);
        return null;
    }

    public object? VisitUnaryExpr(UnaryExpr expr)
    {
        byte dest = _destReg;

        if (TryEvaluateConstant(expr, out object? folded))
        {
            EmitFoldedConstant(folded, dest);
            return null;
        }

        _builder.AddSourceMapping(expr.Span);
        bool operandIsLocal = TryGetLocalReg(expr.Right, out byte operandReg);
        byte operand = operandIsLocal ? operandReg : CompileExpr(expr.Right);

        switch (expr.Operator.Type)
        {
            case TokenType.Minus:
                _builder.EmitABC(OpCode.Neg, dest, operand, 0);
                break;
            case TokenType.Bang:
                _builder.EmitABC(OpCode.Not, dest, operand, 0);
                break;
            case TokenType.Tilde:
                _builder.EmitABC(OpCode.BNot, dest, operand, 0);
                break;
            default:
                throw new CompileError($"Unknown unary operator '{expr.Operator.Lexeme}'.", expr.Operator.Span);
        }
        if (!operandIsLocal) _scope.FreeTemp(operand);
        return null;
    }

    public object? VisitBinaryExpr(BinaryExpr expr)
    {
        byte dest = _destReg;

        // Compile-time constant folding
        if (TryEvaluateConstant(expr, out object? folded))
        {
            EmitFoldedConstant(folded, dest);
            return null;
        }

        // Short-circuit AND: a && b
        if (expr.Operator.Type == TokenType.AmpersandAmpersand)
        {
            CompileExprTo(expr.Left, dest);
            // TestSet: if !IsTruthy(dest) keep dest (falsy), skip right
            _builder.EmitABC(OpCode.TestSet, dest, dest, 0);
            int skipRight = _builder.EmitJump(OpCode.Jmp);
            CompileExprTo(expr.Right, dest);
            _builder.PatchJump(skipRight);
            return null;
        }

        // Short-circuit OR: a || b
        if (expr.Operator.Type == TokenType.PipePipe)
        {
            CompileExprTo(expr.Left, dest);
            // TestSet: if IsTruthy(dest) keep dest (truthy), skip right
            _builder.EmitABC(OpCode.TestSet, dest, dest, 1);
            int skipRight = _builder.EmitJump(OpCode.Jmp);
            CompileExprTo(expr.Right, dest);
            _builder.PatchJump(skipRight);
            return null;
        }

        _builder.AddSourceMapping(expr.Span);
        bool leftIsLocal = TryGetLocalReg(expr.Left, out byte leftReg);
        byte left = leftIsLocal ? leftReg : CompileExpr(expr.Left);
        bool rightIsLocal = TryGetLocalReg(expr.Right, out byte rightReg);
        byte right = rightIsLocal ? rightReg : CompileExpr(expr.Right);

        OpCode op = expr.Operator.Type switch
        {
            TokenType.Plus           => OpCode.Add,
            TokenType.Minus          => OpCode.Sub,
            TokenType.Star           => OpCode.Mul,
            TokenType.Slash          => OpCode.Div,
            TokenType.Percent        => OpCode.Mod,
            TokenType.EqualEqual     => OpCode.Eq,
            TokenType.BangEqual      => OpCode.Ne,
            TokenType.Less           => OpCode.Lt,
            TokenType.LessEqual      => OpCode.Le,
            TokenType.Greater        => OpCode.Gt,
            TokenType.GreaterEqual   => OpCode.Ge,
            TokenType.Ampersand      => OpCode.BAnd,
            TokenType.Pipe           => OpCode.BOr,
            TokenType.Caret          => OpCode.BXor,
            TokenType.LessLess       => OpCode.Shl,
            TokenType.GreaterGreater => OpCode.Shr,
            TokenType.In             => OpCode.In,
            _ => throw new CompileError($"Unknown binary operator '{expr.Operator.Lexeme}'.", expr.Operator.Span),
        };

        _builder.EmitABC(op, dest, left, right);
        if (!rightIsLocal) _scope.FreeTemp(right);
        if (!leftIsLocal) _scope.FreeTemp(left);
        return null;
    }

    public object? VisitTernaryExpr(TernaryExpr expr)
    {
        byte dest = _destReg;
        _builder.AddSourceMapping(expr.Span);

        byte condReg = CompileExpr(expr.Condition);
        int elseJump = _builder.EmitJump(OpCode.JmpFalse, condReg);
        _scope.FreeTemp(condReg);

        CompileExprTo(expr.ThenBranch, dest);
        int endJump = _builder.EmitJump(OpCode.Jmp);

        _builder.PatchJump(elseJump);
        CompileExprTo(expr.ElseBranch, dest);
        _builder.PatchJump(endJump);

        return null;
    }

    public object? VisitAssignExpr(AssignExpr expr)
    {
        byte dest = _destReg;
        _builder.AddSourceMapping(expr.Span);

        // OPT-8: Compound assignment with integer constant → AddI
        // Parser desugars `x += N` to AssignExpr(x, BinaryExpr(x, +, N))
        if (expr.ResolvedDistance >= 0 && expr.Value is BinaryExpr bin &&
            bin.Left is IdentifierExpr lhsId &&
            lhsId.Name.Lexeme == expr.Name.Lexeme &&
            bin.Right is LiteralExpr lit && lit.Value is long intVal)
        {
            int localReg = _scope.ResolveLocal(expr.Name.Lexeme);
            if (localReg >= 0 && !_scope.IsLocalConst(localReg))
            {
                // x += N → AddI R(local) += N
                if (bin.Operator.Type == TokenType.Plus &&
                    intVal >= Instruction.SBxMin && intVal <= Instruction.SBxMax)
                {
                    _builder.EmitAsBx(OpCode.AddI, (byte)localReg, (int)intVal);
                    _scope.MarkNumeric(localReg);
                    if ((byte)localReg != dest)
                        _builder.EmitAB(OpCode.Move, dest, (byte)localReg);
                    return null;
                }
                // x -= N → AddI R(local) += -N
                if (bin.Operator.Type == TokenType.Minus &&
                    -intVal >= Instruction.SBxMin && -intVal <= Instruction.SBxMax)
                {
                    _builder.EmitAsBx(OpCode.AddI, (byte)localReg, -(int)intVal);
                    _scope.MarkNumeric(localReg);
                    if ((byte)localReg != dest)
                        _builder.EmitAB(OpCode.Move, dest, (byte)localReg);
                    return null;
                }
            }
        }

        // OPT-2: if the target is a local variable, compile RHS directly into its register.
        if (expr.ResolvedDistance >= 0)
        {
            int localReg = _scope.ResolveLocal(expr.Name.Lexeme);
            if (localReg >= 0 && !_scope.IsLocalConst(localReg))
            {
                // Compile RHS directly into the local's register
                CompileExprTo(expr.Value, (byte)localReg);
                if (IsNumericExpr(expr.Value))
                    _scope.MarkNumeric(localReg);
                else
                    _scope.ClearNumeric(localReg);
                // If the assignment result is needed (e.g., chained: a = b = 1),
                // copy from local to the expected dest
                if ((byte)localReg != dest)
                    _builder.EmitAB(OpCode.Move, dest, (byte)localReg);
                return null;
            }
        }

        // Fallback for globals, upvalues, and const violations
        CompileExprTo(expr.Value, dest);
        EmitVariable(expr.Name.Lexeme, expr.ResolvedDistance, expr.ResolvedSlot, isLoad: false, dest);
        return null;
    }

    public object? VisitCallExpr(CallExpr expr)
    {
        byte dest = _destReg;
        bool isVoid = _voidContext;
        _voidContext = false;  // Sub-expressions are not in void context
        _builder.AddSourceMapping(expr.Span);

        int argc = expr.Arguments.Count;
        bool hasSpread = expr.Arguments.Any(a => a is SpreadExpr);

        // Fused CallBuiltIn: when callee is a simple DotExpr (e.g., math.sqrt)
        // and no optional chaining or spread args are involved
        if (expr.Callee is DotExpr dot && !dot.IsOptional && !expr.IsOptional && !hasSpread && argc <= 255)
        {
            ushort nameIdx = _builder.AddConstant(dot.Name.Lexeme);
            if (nameIdx <= 255)
            {
                // OPT-1: If dest is a temp at the allocation frontier, reuse it as the window base
                byte calleeReg;
                if (dest >= _scope.LocalCount && dest + 1 == _scope.NextFreeReg)
                {
                    calleeReg = dest;
                    if (argc > 0)
                        _scope.ReserveRegs(argc); // Only reserve arg slots; dest already allocated
                }
                else
                {
                    calleeReg = _scope.ReserveRegs(1 + argc);
                }

                // Compile the receiver (namespace) into a temp ABOVE the call window
                // so FreeTemp works correctly (stack-based register allocator)
                byte nsReg = CompileExpr(dot.Object);

                // Compile arguments into consecutive registers after calleeReg
                for (int i = 0; i < argc; i++)
                    CompileExprTo(expr.Arguments[i], (byte)(calleeReg + 1 + i));

                // Emit fused CallBuiltIn + companion word
                ushort icSlot = _builder.AllocateICSlot(nameIdx);
                _builder.EmitABC(OpCode.CallBuiltIn, calleeReg, nsReg, (byte)argc);
                _builder.EmitRaw((uint)icSlot); // companion word: IC slot index

                _scope.FreeTemp(nsReg);

                // Move result to dest and free call window
                if (!isVoid && calleeReg != dest)
                {
                    _builder.EmitAB(OpCode.Move, dest, calleeReg);
                    _scope.FreeTempFrom(calleeReg);
                }
                else if (isVoid)
                {
                    _scope.FreeTempFrom(calleeReg);
                }
                else if (argc > 0)
                {
                    _scope.FreeTempFrom((byte)(calleeReg + 1));
                }

                return null;
            }
        }

        // Generic path: reserve window, compile callee+args, emit Call
        // OPT-1: If dest is a temp at the allocation frontier, reuse it as the window base
        byte calleeReg2;
        if (dest >= _scope.LocalCount && dest + 1 == _scope.NextFreeReg)
        {
            calleeReg2 = dest;
            if (argc > 0)
                _scope.ReserveRegs(argc);
        }
        else
        {
            calleeReg2 = _scope.ReserveRegs(1 + argc);
        }

        // Compile callee into calleeReg
        CompileExprTo(expr.Callee, calleeReg2);

        // Optional call: ?.() — check if callee is null before calling
        int nullJump = -1;
        if (expr.IsOptional)
        {
            byte nullReg = _scope.AllocTemp();
            _builder.EmitA(OpCode.LoadNull, nullReg);
            byte eqReg = _scope.AllocTemp();
            _builder.EmitABC(OpCode.Eq, eqReg, calleeReg2, nullReg);
            nullJump = _builder.EmitJump(OpCode.JmpTrue, eqReg);
            _scope.FreeTemp(eqReg);
            _scope.FreeTemp(nullReg);
        }

        // Compile arguments into consecutive registers after callee
        for (int i = 0; i < argc; i++)
            CompileExprTo(expr.Arguments[i], (byte)(calleeReg2 + 1 + i));

        // Emit call instruction — result lands in calleeReg
        if (hasSpread)
            _builder.EmitABC(OpCode.CallSpread, calleeReg2, (byte)argc, 0);
        else
            _builder.EmitABC(OpCode.Call, calleeReg2, 0, (byte)argc);

        // Optional: if callee was null, jump here and load null instead
        if (expr.IsOptional)
        {
            int endJump = _builder.EmitJump(OpCode.Jmp);
            _builder.PatchJump(nullJump);
            _builder.EmitA(OpCode.LoadNull, calleeReg2);
            _builder.PatchJump(endJump);
        }

        // Move result to dest and free call window
        if (!isVoid && calleeReg2 != dest)
        {
            _builder.EmitAB(OpCode.Move, dest, calleeReg2);
            _scope.FreeTempFrom(calleeReg2);
        }
        else if (isVoid)
        {
            _scope.FreeTempFrom(calleeReg2);
        }
        else if (argc > 0)
        {
            _scope.FreeTempFrom((byte)(calleeReg2 + 1));
        }

        return null;
    }

    public object? VisitNullCoalesceExpr(NullCoalesceExpr expr)
    {
        byte dest = _destReg;
        CompileExprTo(expr.Left, dest);

        byte nullReg = _scope.AllocTemp();
        _builder.EmitA(OpCode.LoadNull, nullReg);
        byte cmpReg = _scope.AllocTemp();
        // Check if dest == null → coalesce
        _builder.EmitABC(OpCode.Eq, cmpReg, dest, nullReg);
        int rightOnNull = _builder.EmitJump(OpCode.JmpTrue, cmpReg);
        // Check if dest is Error → coalesce (so try expr ?? default works)
        ushort errorTypeIdx = _builder.AddConstant("Error");
        byte errCheckReg = _scope.AllocTemp();
        byte errorTypeReg = _scope.AllocTemp();
        _builder.EmitABx(OpCode.LoadK, errorTypeReg, errorTypeIdx);
        _builder.EmitABC(OpCode.Is, errCheckReg, dest, errorTypeReg);
        _scope.FreeTemp(errorTypeReg);
        int rightOnError = _builder.EmitJump(OpCode.JmpTrue, errCheckReg);
        _scope.FreeTemp(errCheckReg);
        // Neither null nor error — skip right side
        int skipRight = _builder.EmitJump(OpCode.Jmp);
        _scope.FreeTemp(cmpReg);
        _scope.FreeTemp(nullReg);

        _builder.PatchJump(rightOnNull);
        _builder.PatchJump(rightOnError);
        CompileExprTo(expr.Right, dest);

        _builder.PatchJump(skipRight);
        return null;
    }

    public object? VisitDotExpr(DotExpr expr)
    {
        byte dest = _destReg;
        _builder.AddSourceMapping(expr.Span);

        byte objReg = CompileExpr(expr.Object);
        ushort nameIdx = _builder.AddConstant(expr.Name.Lexeme);

        if (expr.IsOptional)
        {
            byte nullReg = _scope.AllocTemp();
            _builder.EmitA(OpCode.LoadNull, nullReg);
            byte cmpReg = _scope.AllocTemp();
            _builder.EmitABC(OpCode.Eq, cmpReg, objReg, nullReg);
            int nullJump = _builder.EmitJump(OpCode.JmpTrue, cmpReg);
            _scope.FreeTemp(cmpReg);
            _scope.FreeTemp(nullReg);

            EmitGetField(dest, objReg, nameIdx);
            int endJump = _builder.EmitJump(OpCode.Jmp);

            _builder.PatchJump(nullJump);
            _builder.EmitA(OpCode.LoadNull, dest);
            _builder.PatchJump(endJump);
        }
        else
        {
            EmitGetField(dest, objReg, nameIdx);
        }

        _scope.FreeTemp(objReg);
        return null;
    }

    public object? VisitDotAssignExpr(DotAssignExpr expr)
    {
        byte dest = _destReg;
        _builder.AddSourceMapping(expr.Span);

        byte objReg = CompileExpr(expr.Object);
        ushort nameIdx = _builder.AddConstant(expr.Name.Lexeme);

        // Compound assignments are desugared by the parser.
        CompileExprTo(expr.Value, dest);
        EmitSetField(objReg, nameIdx, dest);

        _scope.FreeTemp(objReg);
        return null;
    }

    public object? VisitIsExpr(IsExpr expr)
    {
        byte dest = _destReg;
        _builder.AddSourceMapping(expr.Span);
        byte valReg = CompileExpr(expr.Left);

        if (expr.TypeExpr != null)
        {
            // Dynamic type check — compile type expression into a register
            byte typeReg = CompileExpr(expr.TypeExpr);
            _builder.EmitABC(OpCode.Is, dest, valReg, (byte)(typeReg | 0x80));
            _scope.FreeTemp(typeReg);
        }
        else
        {
            string name = expr.TypeName!.Lexeme;
            int localReg = _scope.ResolveLocal(name);
            if (localReg >= 0)
            {
                // The type name is a local variable holding a runtime type
                _builder.EmitABC(OpCode.Is, dest, valReg, (byte)((byte)localReg | 0x80));
            }
            else
            {
                // Static type name — load from constant pool
                ushort typeIdx = _builder.AddConstant(name);
                byte typeReg = _scope.AllocTemp();
                _builder.EmitABx(OpCode.LoadK, typeReg, typeIdx);
                _builder.EmitABC(OpCode.Is, dest, valReg, typeReg);
                _scope.FreeTemp(typeReg);
            }
        }

        _scope.FreeTemp(valReg);
        return null;
    }

    public object? VisitTryExpr(TryExpr expr)
    {
        byte dest = _destReg;
        _builder.AddSourceMapping(expr.Span);

        // Push exception handler that will jump to the catch block
        byte errReg = _scope.AllocTemp();
        int tryBegin = _builder.EmitJump(OpCode.TryBegin, errReg);

        // Evaluate the expression — if it succeeds, result goes into dest
        CompileExprTo(expr.Expression, dest);

        // Pop the handler (success path)
        _builder.EmitA(OpCode.TryEnd, 0);

        // Jump past the catch block
        int skipCatch = _builder.EmitJump(OpCode.Jmp);

        // Catch block: error handler jumps here — move error into dest
        _builder.PatchJump(tryBegin);
        if (errReg != dest)
            _builder.EmitAB(OpCode.Move, dest, errReg);

        // End of try expression
        _builder.PatchJump(skipCatch);
        _scope.FreeTemp(errReg);

        return null;
    }

    public object? VisitSpreadExpr(SpreadExpr expr)
    {
        byte dest = _destReg;
        byte valReg = CompileExpr(expr.Expression);
        _builder.EmitABC(OpCode.Spread, dest, valReg, 0);
        _scope.FreeTemp(valReg);
        return null;
    }

    public object? VisitAwaitExpr(AwaitExpr expr)
    {
        byte dest = _destReg;
        _builder.AddSourceMapping(expr.Span);
        byte valReg = CompileExpr(expr.Expression);
        _builder.EmitABC(OpCode.Await, dest, valReg, 0);
        _scope.FreeTemp(valReg);
        return null;
    }

    public object? VisitSwitchExpr(SwitchExpr expr)
    {
        byte dest = _destReg;
        _builder.AddSourceMapping(expr.Span);

        byte subjectReg = CompileExpr(expr.Subject);
        var endJumps = new List<int>();
        bool hasDefault = false;

        foreach (SwitchArm arm in expr.Arms)
        {
            if (arm.IsDiscard)
            {
                hasDefault = true;
                CompileExprTo(arm.Body, dest);
                endJumps.Add(_builder.EmitJump(OpCode.Jmp));
            }
            else
            {
                byte patReg = CompileExpr(arm.Pattern!);
                byte cmpReg = _scope.AllocTemp();
                _builder.EmitABC(OpCode.Eq, cmpReg, subjectReg, patReg);
                int nextArm = _builder.EmitJump(OpCode.JmpFalse, cmpReg);
                _scope.FreeTemp(cmpReg);
                _scope.FreeTemp(patReg);

                CompileExprTo(arm.Body, dest);
                endJumps.Add(_builder.EmitJump(OpCode.Jmp));

                _builder.PatchJump(nextArm);
            }
        }

        if (!hasDefault)
        {
            byte errReg = _scope.AllocTemp();
            ushort msgIdx = _builder.AddConstant("No matching case in switch expression.");
            _builder.EmitABx(OpCode.LoadK, errReg, msgIdx);
            _builder.EmitA(OpCode.Throw, errReg);
            _scope.FreeTemp(errReg);
        }

        foreach (int j in endJumps)
            _builder.PatchJump(j);

        _scope.FreeTemp(subjectReg);
        return null;
    }

    public object? VisitRedirectExpr(RedirectExpr expr)
    {
        byte dest = _destReg;
        _builder.AddSourceMapping(expr.Span);
        byte exprReg = CompileExpr(expr.Expression);
        byte targetReg = CompileExpr(expr.Target);
        byte flags = (byte)expr.Stream;
        if (expr.Append)
            flags |= 0x04;
        _builder.EmitABC(OpCode.Redirect, exprReg, flags, targetReg);
        if (exprReg != dest)
            _builder.EmitAB(OpCode.Move, dest, exprReg);
        _scope.FreeTemp(targetReg);
        _scope.FreeTemp(exprReg);
        return null;
    }

    // =========================================================================
    // Private helpers: GetField / SetField with large constant index support
    // =========================================================================

    private void EmitGetField(byte dest, byte objReg, ushort nameIdx)
    {
        if (nameIdx <= 255)
        {
            ushort icSlot = _builder.AllocateICSlot(nameIdx);
            _builder.EmitABC(OpCode.GetFieldIC, dest, objReg, (byte)nameIdx);
            _builder.EmitRaw((uint)icSlot); // companion word: IC slot index
        }
        else
        {
            byte keyReg = _scope.AllocTemp();
            _builder.EmitABx(OpCode.LoadK, keyReg, nameIdx);
            _builder.EmitABC(OpCode.GetTable, dest, objReg, keyReg);
            _scope.FreeTemp(keyReg);
        }
    }

    private void EmitSetField(byte objReg, ushort nameIdx, byte valReg)
    {
        if (nameIdx <= 255)
        {
            _builder.EmitABC(OpCode.SetField, objReg, (byte)nameIdx, valReg);
        }
        else
        {
            byte keyReg = _scope.AllocTemp();
            _builder.EmitABx(OpCode.LoadK, keyReg, nameIdx);
            _builder.EmitABC(OpCode.SetTable, objReg, keyReg, valReg);
            _scope.FreeTemp(keyReg);
        }
    }

    // =========================================================================
    // Compile-time constant evaluation helpers
    // =========================================================================

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
                if (bin.Operator.Type == TokenType.AmpersandAmpersand)
                {
                    if (!TryEvaluateConstant(bin.Left, out object? left))
                    { value = null; return false; }
                    if (CompileTimeIsFalsy(left)) { value = left; return true; }
                    return TryEvaluateConstant(bin.Right, out value);
                }
                if (bin.Operator.Type == TokenType.PipePipe)
                {
                    if (!TryEvaluateConstant(bin.Left, out object? left))
                    { value = null; return false; }
                    if (!CompileTimeIsFalsy(left)) { value = left; return true; }
                    return TryEvaluateConstant(bin.Right, out value);
                }
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
                { value = constVal; return true; }
                value = null;
                return false;

            case InterpolatedStringExpr interp:
                var sb = new System.Text.StringBuilder();
                foreach (Expr part in interp.Parts)
                {
                    if (!TryEvaluateConstant(part, out object? partVal))
                    { value = null; return false; }
                    sb.Append(CompileTimeStringify(partVal));
                }
                value = sb.ToString();
                return true;

            case TernaryExpr ternary:
                if (!TryEvaluateConstant(ternary.Condition, out object? condVal))
                { value = null; return false; }
                return CompileTimeIsFalsy(condVal)
                    ? TryEvaluateConstant(ternary.ElseBranch, out value)
                    : TryEvaluateConstant(ternary.ThenBranch, out value);

            default:
                value = null;
                return false;
        }
    }

    private static bool CompileTimeIsFalsy(object? value) => value switch
    {
        null     => true,
        bool b   => !b,
        long l   => l == 0,
        double d => d == 0.0,
        string s => s.Length == 0,
        _        => false,
    };

    private static string CompileTimeStringify(object? value) => value switch
    {
        string s => s,
        long l   => l.ToString(),
        double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
        bool b   => b ? "true" : "false",
        null     => "null",
        _        => value.ToString() ?? "",
    };

    private static object? TryFoldBinary(object? left, object? right, TokenType op)
    {
        if (left is long li && right is long ri)
        {
            return op switch
            {
                TokenType.Plus           => li + ri,
                TokenType.Minus          => li - ri,
                TokenType.Star           => li * ri,
                TokenType.Slash          when ri != 0 => li / ri,
                TokenType.Percent        when ri != 0 => li % ri,
                TokenType.EqualEqual     => (object)(li == ri),
                TokenType.BangEqual      => (object)(li != ri),
                TokenType.Less           => (object)(li < ri),
                TokenType.Greater        => (object)(li > ri),
                TokenType.LessEqual      => (object)(li <= ri),
                TokenType.GreaterEqual   => (object)(li >= ri),
                TokenType.Ampersand      => li & ri,
                TokenType.Pipe           => li | ri,
                TokenType.Caret          => li ^ ri,
                TokenType.LessLess       when ri >= 0 && ri < 64 => li << (int)ri,
                TokenType.GreaterGreater when ri >= 0 && ri < 64 => li >> (int)ri,
                _ => null,
            };
        }

        double? ld = left  is long ll ? ll : left  is double dl ? dl : (double?)null;
        double? rd = right is long rl ? rl : right is double dr ? dr : (double?)null;
        if (ld.HasValue && rd.HasValue && (left is double || right is double))
        {
            return op switch
            {
                TokenType.Plus         => ld.Value + rd.Value,
                TokenType.Minus        => ld.Value - rd.Value,
                TokenType.Star         => ld.Value * rd.Value,
                TokenType.Slash        when rd.Value != 0.0 => ld.Value / rd.Value,
                TokenType.Percent      when rd.Value != 0.0 => ld.Value % rd.Value,
                TokenType.EqualEqual   => (object)(ld.Value == rd.Value),
                TokenType.BangEqual    => (object)(ld.Value != rd.Value),
                TokenType.Less         => (object)(ld.Value < rd.Value),
                TokenType.Greater      => (object)(ld.Value > rd.Value),
                TokenType.LessEqual    => (object)(ld.Value <= rd.Value),
                TokenType.GreaterEqual => (object)(ld.Value >= rd.Value),
                _ => null,
            };
        }

        if (left is string ls && right is string rs && op == TokenType.Plus)
            return ls + rs;

        if (left is bool lb && right is bool rb)
        {
            return op switch
            {
                TokenType.EqualEqual => (object)(lb == rb),
                TokenType.BangEqual  => (object)(lb != rb),
                _ => null,
            };
        }

        return null;
    }
}
