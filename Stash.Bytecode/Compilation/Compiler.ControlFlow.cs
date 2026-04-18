using System;
using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;
using Stash.Parsing.AST;
using Stash.Runtime;
using Stash.Runtime.Types;

namespace Stash.Bytecode;

/// <summary>
/// Control flow statement visitor implementations.
/// </summary>
public sealed partial class Compiler
{
    /// <inheritdoc />
    public object? VisitIfStmt(IfStmt stmt)
    {
        // Dead branch elimination: if condition is a compile-time constant,
        // only compile the taken branch.
        if (TryEvaluateConstant(stmt.Condition, out object? condValue))
        {
            if (!CompileTimeIsFalsy(condValue))
                CompileStmt(stmt.ThenBranch);
            else if (stmt.ElseBranch != null)
                CompileStmt(stmt.ElseBranch);
            return null;
        }

        _builder.AddSourceMapping(stmt.Span);

        // OPT-5: Negation inversion — if (!x) → compile x, JmpTrue (skip Not + JmpFalse)
        var (condReg, elseJump, condIsLocal) = CompileConditionJump(stmt.Condition);
        if (!condIsLocal) _scope.FreeTemp(condReg);

        CompileStmt(stmt.ThenBranch);

        if (stmt.ElseBranch != null)
        {
            int endJump = _builder.EmitJump(OpCode.Jmp);
            _builder.PatchJump(elseJump);
            CompileStmt(stmt.ElseBranch);
            _builder.PatchJump(endJump);
        }
        else
        {
            _builder.PatchJump(elseJump);
        }

        return null;
    }

    /// <inheritdoc />
    public object? VisitWhileStmt(WhileStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);
        int loopStart = _builder.CurrentOffset;

        var loopCtx = new LoopContext
        {
            LoopStart = loopStart,
            ContinueTarget = loopStart,
            ScopeDepth = _scope.ScopeDepth,
        };
        (_loops ??= new()).Push(loopCtx);

        // OPT-5: Negation inversion — while (!x) → compile x, JmpTrue (skip Not + JmpFalse)
        var (condReg, exitJump, condIsLocal) = CompileConditionJump(stmt.Condition);
        if (!condIsLocal) _scope.FreeTemp(condReg);

        CompileStmt(stmt.Body);

        _builder.EmitLoop(0, loopStart);
        _builder.PatchJump(exitJump);

        PatchBreakJumps();
        return null;
    }

    /// <inheritdoc />
    public object? VisitDoWhileStmt(DoWhileStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);
        int loopStart = _builder.CurrentOffset;

        var loopCtx = new LoopContext
        {
            LoopStart = loopStart,
            ScopeDepth = _scope.ScopeDepth,
        };
        (_loops ??= new()).Push(loopCtx);

        CompileStmt(stmt.Body);

        // Continue target is the condition check — patch any pending continue jumps here
        loopCtx.ContinueTarget = _builder.CurrentOffset;
        foreach (int j in loopCtx.ContinueJumps)
            _builder.PatchJump(j);
        loopCtx.ContinueJumps.Clear();

        // Condition: if true, loop back; if false, fall through to exit
        bool condIsLocal = TryGetLocalReg(stmt.Condition, out byte condLocalReg);
        byte condReg = condIsLocal ? condLocalReg : CompileExpr(stmt.Condition);
        int exitJump = _builder.EmitJump(OpCode.JmpFalse, condReg);
        if (!condIsLocal) _scope.FreeTemp(condReg);
        _builder.EmitLoop(0, loopStart);
        _builder.PatchJump(exitJump);

        PatchBreakJumps();
        return null;
    }

    /// <inheritdoc />
    public object? VisitForStmt(ForStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);

        if (TryCompileNumericFor(stmt))
            return null;

        _scope.BeginScope();

        if (stmt.Initializer != null)
            CompileStmt(stmt.Initializer);

        int loopStart = _builder.CurrentOffset;

        var loopCtx = new LoopContext
        {
            LoopStart = loopStart,
            ScopeDepth = _scope.ScopeDepth,
        };
        (_loops ??= new()).Push(loopCtx);

        int exitJump = -1;
        if (stmt.Condition != null)
        {
            bool condIsLocal = TryGetLocalReg(stmt.Condition, out byte condLocalReg);
            byte condReg = condIsLocal ? condLocalReg : CompileExpr(stmt.Condition);
            exitJump = _builder.EmitJump(OpCode.JmpFalse, condReg);
            if (!condIsLocal) _scope.FreeTemp(condReg);
        }

        CompileStmt(stmt.Body);

        // Continue target is before the increment — patch any pending continue jumps here
        loopCtx.ContinueTarget = _builder.CurrentOffset;
        foreach (int j in loopCtx.ContinueJumps)
            _builder.PatchJump(j);
        loopCtx.ContinueJumps.Clear();

        if (stmt.Increment != null)
        {
            byte incReg = CompileExpr(stmt.Increment);
            _scope.FreeTemp(incReg);
        }

        _builder.EmitLoop(0, loopStart);

        if (exitJump >= 0)
            _builder.PatchJump(exitJump);

        PatchBreakJumps();
        EndScope();
        return null;
    }

    /// <inheritdoc />
    public object? VisitForInStmt(ForInStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);
        _scope.BeginScope();

        // Declare loop variables FIRST — they get stable local registers
        byte valueReg = _scope.DeclareLocal(stmt.VariableName.Lexeme);
        byte indexReg = 0;
        bool hasIndex = stmt.IndexName != null;
        if (hasIndex)
            indexReg = _scope.DeclareLocal(stmt.IndexName!.Lexeme);

        // Allocate temp for iterable + 2 scratch regs for IterLoop writeback
        // Layout: [iterableReg] [scratch1 = iterableReg+1] [scratch2 = iterableReg+2]
        byte iterableReg = _scope.DeclareLocal("<iter>");
        _scope.MarkInitialized();
        byte scratch1 = _scope.DeclareLocal("<iter_val>");
        _scope.MarkInitialized();
        byte scratch2 = _scope.DeclareLocal("<iter_idx>");
        _scope.MarkInitialized();

        CompileExprTo(stmt.Iterable, iterableReg);

        // IterPrep converts the value in-place into an iterator object
        _builder.EmitAB(OpCode.IterPrep, iterableReg, hasIndex ? (byte)1 : (byte)0);

        int loopStart = _builder.CurrentOffset;

        var loopCtx = new LoopContext
        {
            LoopStart = loopStart,
            ContinueTarget = loopStart,
            ScopeDepth = _scope.ScopeDepth,
        };
        (_loops ??= new()).Push(loopCtx);

        // IterLoop advances the iterator; if exhausted, jumps forward past the loop body
        int iterCheck = _builder.EmitJump(OpCode.IterLoop, iterableReg);

        // IterLoop writes value to R(iterableReg+1) and index to R(iterableReg+2);
        // move them into the declared local registers
        _builder.EmitAB(OpCode.Move, valueReg, scratch1);
        if (hasIndex)
            _builder.EmitAB(OpCode.Move, indexReg, scratch2);

        // Inner scope for the loop body
        _scope.BeginScope();
        foreach (Stmt s in stmt.Body.Statements)
            CompileStmt(s);

        // Close upvalues captured during this iteration before looping back so that
        // each iteration's closures capture an independent copy of the loop variable
        if (_builder.MayHaveCapturedLocals)
            _builder.EmitA(OpCode.CloseUpval, valueReg);

        EndScope();

        _builder.EmitLoop(0, loopStart);
        _builder.PatchJump(iterCheck);

        PatchBreakJumps();
        EndScope();
        return null;
    }

    /// <inheritdoc />
    public object? VisitBreakStmt(BreakStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);
        if (_loops == null || _loops.Count == 0)
            throw new CompileError("'break' outside of loop.", stmt.Span);

        LoopContext loop = _loops.Peek();

        // Inline any finally bodies that are inside this loop (innermost first)
        InlineFinallyForJump(loop.ScopeDepth);

        loop.BreakJumps.Add(_builder.EmitJump(OpCode.Jmp));
        return null;
    }

    /// <inheritdoc />
    public object? VisitContinueStmt(ContinueStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);
        if (_loops == null || _loops.Count == 0)
            throw new CompileError("'continue' outside of loop.", stmt.Span);

        LoopContext loop = _loops.Peek();

        // Inline any finally bodies that are inside this loop (innermost first)
        InlineFinallyForJump(loop.ScopeDepth);

        if (loop.ContinueTarget >= 0)
        {
            // Target is known — emit backward jump directly
            _builder.EmitLoop(0, loop.ContinueTarget);
        }
        else
        {
            // Target not yet known — emit forward jump to be patched later
            loop.ContinueJumps.Add(_builder.EmitJump(OpCode.Jmp));
        }

        return null;
    }

    /// <summary>
    /// Attempts to compile a for-loop as an optimized numeric for using ForPrep/ForLoop opcodes.
    /// Returns true if the pattern was recognized and optimized code was emitted.
    /// </summary>
    private bool TryCompileNumericFor(ForStmt stmt)
    {
        // 1. Init must be a VarDeclStmt with a non-null initializer
        if (stmt.Initializer is not VarDeclStmt varDecl || varDecl.Initializer == null)
            return false;

        // 2. Condition must be BinaryExpr: <ident> <cmp> <limit>
        if (stmt.Condition is not BinaryExpr cond)
            return false;
        if (cond.Left is not IdentifierExpr condIdent || condIdent.Name.Lexeme != varDecl.Name.Lexeme)
            return false;

        TokenType cmpOp = cond.Operator.Type;
        if (cmpOp != TokenType.Less && cmpOp != TokenType.LessEqual &&
            cmpOp != TokenType.Greater && cmpOp != TokenType.GreaterEqual)
            return false;

        // 3. Increment must be UpdateExpr (++ or --) on the same variable
        if (stmt.Increment is not UpdateExpr update)
            return false;
        if (update.Operand is not IdentifierExpr incIdent || incIdent.Name.Lexeme != varDecl.Name.Lexeme)
            return false;

        TokenType incOp = update.Operator.Type;
        if (incOp != TokenType.PlusPlus && incOp != TokenType.MinusMinus)
            return false;

        // 4. Step/comparison consistency
        bool isPositiveStep = incOp == TokenType.PlusPlus;
        bool isUpperBound = cmpOp == TokenType.Less || cmpOp == TokenType.LessEqual;
        if (isPositiveStep != isUpperBound)
            return false;

        int stepValue = isPositiveStep ? 1 : -1;

        // --- Emit optimized ForPrep/ForLoop code ---

        _scope.BeginScope();

        // Allocate 4 consecutive registers: counter, limit, step, loop variable
        byte counterReg = _scope.DeclareLocal("<for_counter>");
        _scope.MarkInitialized();
        byte limitReg = _scope.DeclareLocal("<for_limit>");
        _scope.MarkInitialized();
        byte stepReg = _scope.DeclareLocal("<for_step>");
        _scope.MarkInitialized();
        byte varReg = _scope.DeclareLocal(varDecl.Name.Lexeme);
        _scope.MarkInitialized();

        // Load initial value into counter register
        CompileExprTo(varDecl.Initializer, counterReg);

        // Load limit and adjust for strict comparisons so ForLoop uses <=/>= semantics
        CompileExprTo(cond.Right, limitReg);
        if (cmpOp == TokenType.Less)
            _builder.EmitAsBx(OpCode.AddI, limitReg, -1);
        else if (cmpOp == TokenType.Greater)
            _builder.EmitAsBx(OpCode.AddI, limitReg, 1);

        // Load step constant
        ushort stepIdx = _builder.AddConstant((long)stepValue);
        _builder.EmitABx(OpCode.LoadK, stepReg, stepIdx);

        // Determine if we can use integer-specialized for-loop.
        // Step is always an int literal (±1). Init must also be provably int.
        bool useIntSpec = varDecl.Initializer is LiteralExpr { Value: int or long };

        // ForPrep: initialize and jump to ForLoop for the initial bounds check
        int forPrepJump = _builder.EmitJump(useIntSpec ? OpCode.ForPrepII : OpCode.ForPrep, counterReg);

        int bodyStart = _builder.CurrentOffset;

        var loopCtx = new LoopContext
        {
            LoopStart = bodyStart,
            ScopeDepth = _scope.ScopeDepth,
        };
        (_loops ??= new()).Push(loopCtx);

        // Inner scope for the loop body
        _scope.BeginScope();
        foreach (Stmt s in stmt.Body.Statements)
            CompileStmt(s);

        if (_builder.MayHaveCapturedLocals)
            _builder.EmitA(OpCode.CloseUpval, varReg);

        EndScope();

        // Patch continue jumps to the ForLoop instruction position
        loopCtx.ContinueTarget = _builder.CurrentOffset;
        foreach (int j in loopCtx.ContinueJumps)
            _builder.PatchJump(j);
        loopCtx.ContinueJumps.Clear();

        // Patch ForPrep to jump here (the ForLoop instruction)
        _builder.PatchJump(forPrepJump);

        // ForLoop: increment counter, check bounds, jump back to body if in range
        _builder.EmitAsBx(useIntSpec ? OpCode.ForLoopII : OpCode.ForLoop, counterReg, bodyStart - _builder.CurrentOffset - 1);

        PatchBreakJumps();
        EndScope();

        return true;
    }

    /// <inheritdoc />
    public object? VisitReturnStmt(ReturnStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);

        byte reg;
        bool isLocal = false;
        if (stmt.Value != null)
        {
            // OPT-3: If returning a local variable, use its register directly.
            // Disable when active finally blocks exist — they could modify the local
            // between the finally body and the Return opcode.
            if (_activeFinally is not { Count: > 0 } && TryGetLocalReg(stmt.Value, out byte localReg))
            {
                reg = localReg;
                isLocal = true;
            }
            else
            {
                reg = CompileExpr(stmt.Value);
            }
        }
        else
        {
            reg = _scope.AllocTemp();
            _builder.EmitA(OpCode.LoadNull, reg);
        }

        // Inline all active finally bodies before returning (innermost to outermost)
        if (_activeFinally != null)
        {
            for (int i = _activeFinally.Count - 1; i >= 0; i--)
            {
                FinallyInfo fi = _activeFinally[i];
                if (fi.Body != null)
                {
                    _builder.EmitAx(OpCode.TryEnd, 0);
                    CompileStmt(fi.Body);
                }
            }
        }

        _builder.EmitABC(OpCode.Return, reg, 1, 0);
        if (!isLocal) _scope.FreeTemp(reg);

        return null;
    }

    /// <inheritdoc />
    public object? VisitSwitchStmt(SwitchStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);
        byte subjectReg = CompileExpr(stmt.Subject);
        var endJumps = new List<int>();

        foreach (SwitchCase @case in stmt.Cases)
        {
            if (@case.IsDefault)
            {
                CompileStmt(@case.Body);
                endJumps.Add(_builder.EmitJump(OpCode.Jmp));
            }
            else if (@case.Patterns.Count == 1)
            {
                // Single pattern: compare and jump past body if no match
                byte patReg = CompileExpr(@case.Patterns[0]);
                byte cmpReg = _scope.AllocTemp();
                _builder.EmitABC(OpCode.Eq, cmpReg, subjectReg, patReg);
                int nextCase = _builder.EmitJump(OpCode.JmpFalse, cmpReg);
                _scope.FreeTemp(cmpReg);
                _scope.FreeTemp(patReg);

                CompileStmt(@case.Body);
                endJumps.Add(_builder.EmitJump(OpCode.Jmp));
                _builder.PatchJump(nextCase);
            }
            else
            {
                // Multiple patterns: try each in order; jump to body on first match
                var bodyJumps = new List<int>();
                int nextCaseJump = -1;

                for (int i = 0; i < @case.Patterns.Count; i++)
                {
                    byte patReg = CompileExpr(@case.Patterns[i]);
                    byte cmpReg = _scope.AllocTemp();
                    _builder.EmitABC(OpCode.Eq, cmpReg, subjectReg, patReg);

                    if (i < @case.Patterns.Count - 1)
                    {
                        // Not the last pattern — if no match, try next; if match, jump to body
                        int tryNext = _builder.EmitJump(OpCode.JmpFalse, cmpReg);
                        _scope.FreeTemp(cmpReg);
                        _scope.FreeTemp(patReg);
                        bodyJumps.Add(_builder.EmitJump(OpCode.Jmp)); // matched — go to body
                        _builder.PatchJump(tryNext);
                    }
                    else
                    {
                        // Last pattern — if no match, skip to next case
                        nextCaseJump = _builder.EmitJump(OpCode.JmpFalse, cmpReg);
                        _scope.FreeTemp(cmpReg);
                        _scope.FreeTemp(patReg);
                    }
                }

                // Patch all "matched" jumps from earlier patterns to land here (body start)
                foreach (int j in bodyJumps)
                    _builder.PatchJump(j);

                CompileStmt(@case.Body);
                endJumps.Add(_builder.EmitJump(OpCode.Jmp));

                if (nextCaseJump >= 0)
                    _builder.PatchJump(nextCaseJump);
            }
        }

        foreach (int j in endJumps)
            _builder.PatchJump(j);

        _scope.FreeTemp(subjectReg);
        return null;
    }

    /// <inheritdoc />
    public object? VisitDeferStmt(DeferStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);

        if (stmt.Body is BlockStmt block)
        {
            CompileDeferBlock(block);
        }
        else if (stmt.Body is ExprStmt exprStmt && exprStmt.Expression is CallExpr callExpr)
        {
            CompileDeferCall(callExpr, exprStmt, stmt.HasAwait);
        }
        else
        {
            // Non-call expression: wrap in a closure like block defer
            CompileDeferBodyAsBlock(stmt.Body);
        }

        return null;
    }

    private void CompileDeferCall(CallExpr call, ExprStmt fallbackStmt, bool hasAwait)
    {
        // Check for spread args first — fall back to block-style closure (no eager eval)
        foreach (Expr arg in call.Arguments)
        {
            if (arg is SpreadExpr)
            {
                CompileDeferBodyAsBlock(fallbackStmt);
                return;
            }
        }

        // Eager evaluation: evaluate callee and args into hidden locals at function scope level.
        // These locals are NOT scoped — they live until the function returns so the defer
        // closure can access them as upvalues while the frame is still alive.
        byte calleeLocal = _scope.DeclareLocal("<defer_callee>");
        CompileExprTo(call.Callee, calleeLocal);
        _scope.MarkInitialized();

        int argCount = call.Arguments.Count;
        for (int i = 0; i < argCount; i++)
        {
            byte argLocal = _scope.DeclareLocal($"<defer_arg_{i}>");
            CompileExprTo(call.Arguments[i], argLocal);
            _scope.MarkInitialized();
        }

        // Build a zero-param closure that reads the captured values via upvalues
        // and performs the call at defer-run time.
        var child = new Compiler(this, "<defer>", _globalSlots);

        byte childCalleeReg = child._scope.AllocTemp();
        byte calleeUv = child.ResolveUpvalue("<defer_callee>", 1);
        child._builder.EmitAB(OpCode.GetUpval, childCalleeReg, calleeUv);

        for (int i = 0; i < argCount; i++)
        {
            byte childArgReg = child._scope.AllocTemp();
            byte argUv = child.ResolveUpvalue($"<defer_arg_{i}>", 1);
            child._builder.EmitAB(OpCode.GetUpval, childArgReg, argUv);
        }

        // Callee at childCalleeReg, args at childCalleeReg+1 .. childCalleeReg+argCount
        // Call encoding: A=callee, B=0, C=argc (same as normal call sites)
        child._builder.EmitABC(OpCode.Call, childCalleeReg, 0, (byte)argCount);

        if (hasAwait)
            child._builder.EmitABC(OpCode.Await, childCalleeReg, childCalleeReg, 0);

        child._builder.EmitABC(OpCode.Return, childCalleeReg, 1, 0);

        // Free child temps LIFO
        for (int i = argCount - 1; i >= 0; i--)
            child._scope.FreeTemp((byte)(childCalleeReg + 1 + i));
        child._scope.FreeTemp(childCalleeReg);

        EmitDeferClosure(child);
    }

    private void CompileDeferBlock(BlockStmt block)
    {
        var child = new Compiler(this, "<defer>", _globalSlots);

        foreach (Stmt s in block.Statements)
            child.CompileStmt(s);

        // Implicit null return
        byte retReg = child._scope.AllocTemp();
        child._builder.EmitA(OpCode.LoadNull, retReg);
        child._builder.EmitABC(OpCode.Return, retReg, 1, 0);
        child._scope.FreeTemp(retReg);

        EmitDeferClosure(child);
    }

    private void CompileDeferBodyAsBlock(Stmt body)
    {
        var child = new Compiler(this, "<defer>", _globalSlots);

        child.CompileStmt(body);

        // Implicit null return
        byte retReg = child._scope.AllocTemp();
        child._builder.EmitA(OpCode.LoadNull, retReg);
        child._builder.EmitABC(OpCode.Return, retReg, 1, 0);
        child._scope.FreeTemp(retReg);

        EmitDeferClosure(child);
    }

    /// <summary>
    /// Finalises a child compiler's chunk and emits Closure + upvalue descriptors + Defer
    /// into the current (parent) builder.
    /// </summary>
    private void EmitDeferClosure(Compiler child)
    {
        // Defer requires the slow return path — force MayHaveCapturedLocals so the
        // ultra-fast OpReturn in the dispatch loop falls through to ExecuteReturn.
        _builder.MayHaveCapturedLocals = true;

        child._builder.MaxRegs = child._scope.MaxRegs;
        child._builder.LocalNames = child._scope.GetLocalNames();
        child._builder.LocalIsConst = child._scope.GetLocalIsConst();
        child._builder.UpvalueNames = child._upvalueNames?.ToArray();

        Chunk deferChunk = child._builder.Build();
        ushort chunkIdx = _builder.AddConstant(StashValue.FromObj(deferChunk));

        byte closureReg = _scope.AllocTemp();
        _builder.EmitABx(OpCode.Closure, closureReg, chunkIdx);
        foreach (UpvalueDescriptor uv in deferChunk.Upvalues)
            _builder.EmitRaw((uint)(uv.IsLocal ? 1 : 0) | ((uint)uv.Index << 8));

        _builder.EmitA(OpCode.Defer, closureReg);
        _scope.FreeTemp(closureReg);
    }
}
