using System;
using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;
using Stash.Parsing.AST;
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
            {
                CompileStmt(stmt.ThenBranch);
            }
            else if (stmt.ElseBranch != null)
            {
                CompileStmt(stmt.ElseBranch);
            }
            return null;
        }

        _builder.AddSourceMapping(stmt.Span);
        CompileExpr(stmt.Condition);
        int elseJump = _builder.EmitJump(OpCode.JumpFalse);

        CompileStmt(stmt.ThenBranch);

        if (stmt.ElseBranch != null)
        {
            int endJump = _builder.EmitJump(OpCode.Jump);
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

        CompileExpr(stmt.Condition);
        int exitJump = _builder.EmitJump(OpCode.JumpFalse);

        CompileStmt(stmt.Body);

        _builder.EmitLoop(loopStart);
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
            ContinueTarget = -1,  // determined after body, before condition
            ScopeDepth = _scope.ScopeDepth,
        };
        (_loops ??= new()).Push(loopCtx);

        CompileStmt(stmt.Body);

        // All continue statements now have their target here (before the condition)
        loopCtx.ContinueTarget = _builder.CurrentOffset;
        PatchContinueJumps(loopCtx, _builder);

        CompileExpr(stmt.Condition);
        int exitJump = _builder.EmitJump(OpCode.JumpFalse);
        _builder.EmitLoop(loopStart);
        _builder.PatchJump(exitJump);

        PatchBreakJumps();
        return null;
    }

    /// <inheritdoc />
    public object? VisitForStmt(ForStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);

        // Wrap the entire for-loop in a scope so initializer variables are cleaned up on exit
        _scope.BeginScope();

        if (stmt.Initializer != null)
        {
            CompileStmt(stmt.Initializer);
        }

        int loopStart = _builder.CurrentOffset;

        var loopCtx = new LoopContext
        {
            LoopStart = loopStart,
            ContinueTarget = -1,  // set after body, before increment
            ScopeDepth = _scope.ScopeDepth,
        };
        (_loops ??= new()).Push(loopCtx);

        int exitJump = -1;
        if (stmt.Condition != null)
        {
            CompileExpr(stmt.Condition);
            exitJump = _builder.EmitJump(OpCode.JumpFalse);
        }

        CompileStmt(stmt.Body);

        // Continue should jump here — before the increment expression
        loopCtx.ContinueTarget = _builder.CurrentOffset;
        PatchContinueJumps(loopCtx, _builder);

        if (stmt.Increment != null)
        {
            CompileExpr(stmt.Increment);
            _builder.Emit(OpCode.Pop);  // discard the increment result
        }

        _builder.EmitLoop(loopStart);

        if (exitJump >= 0)
        {
            _builder.PatchJump(exitJump);
        }

        PatchBreakJumps();

        // Clean up the initializer variable (for-scope)
        EmitScopePops();
        return null;
    }

    /// <inheritdoc />
    public object? VisitForInStmt(ForInStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);
        _scope.BeginScope();

        // Declare a synthetic local for the iterator so it occupies a proper stack slot.
        // Without this, the iterator value on the stack misaligns subsequent local slot indices.
        int iterSlot = _scope.DeclareLocal("<iter>", isConst: false);
        CompileExpr(stmt.Iterable);
        _builder.Emit(OpCode.Iterator);
        _scope.MarkInitialized(iterSlot);

        // Optional index variable — declared as a local (updated by the VM)
        if (stmt.IndexName != null)
        {
            int indexSlot = _scope.DeclareLocal(stmt.IndexName.Lexeme, isConst: false);
            _builder.Emit(OpCode.Null);  // placeholder value for index
            _scope.MarkInitialized(indexSlot);
        }

        // Loop variable
        int varSlot = _scope.DeclareLocal(stmt.VariableName.Lexeme, isConst: false);
        _builder.Emit(OpCode.Null);  // placeholder value for loop variable
        _scope.MarkInitialized(varSlot);

        int loopStart = _builder.CurrentOffset;
        var loopCtx = new LoopContext
        {
            LoopStart = loopStart,
            ContinueTarget = loopStart,
            ScopeDepth = _scope.ScopeDepth,
        };
        (_loops ??= new()).Push(loopCtx);

        // OP_ITERATE: advance iterator; if exhausted, jump to exit
        int exitJump = _builder.EmitJump(OpCode.Iterate);

        // Iterator pushed the next value — store it into the loop variable
        _builder.Emit(OpCode.StoreLocal, (byte)varSlot);

        CompileStmt(stmt.Body);

        // Close any upvalues captured from loop-iteration variables before looping back,
        // ensuring each iteration's closures freeze the current value of the loop variable.
        // Without this, all closures in a loop share a single open upvalue that reads the
        // live (ever-changing) stack slot — causing tasks/closures to see wrong values.
        _builder.Emit(OpCode.CloseUpvalue, (byte)varSlot);
        if (stmt.IndexName != null)
        {
            _builder.Emit(OpCode.CloseUpvalue, (byte)(varSlot - 1)); // index slot is one below varSlot
        }

        _builder.EmitLoop(loopStart);
        _builder.PatchJump(exitJump);

        // Iterator is now a declared local — cleaned up by EmitScopePops below
        PatchBreakJumps();
        EmitScopePops();
        return null;
    }

    /// <inheritdoc />
    public object? VisitBreakStmt(BreakStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);
        if ((_loops?.Count ?? 0) == 0)
        {
            throw new CompileError("'break' outside of loop.", stmt.Span);
        }

        EmitPendingFinally();
        EmitScopeCleanup(_loops!.Peek().ScopeDepth);

        int jump = _builder.EmitJump(OpCode.Jump);
        _loops!.Peek().BreakJumps.Add(jump);
        return null;
    }

    /// <inheritdoc />
    public object? VisitContinueStmt(ContinueStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);
        if ((_loops?.Count ?? 0) == 0)
        {
            throw new CompileError("'continue' outside of loop.", stmt.Span);
        }

        EmitPendingFinally();

        LoopContext loop = _loops!.Peek();
        EmitScopeCleanup(loop.ScopeDepth);

        if (loop.ContinueTarget >= 0)
        {
            _builder.EmitLoop(loop.ContinueTarget);
        }
        else
        {
            int jump = _builder.EmitJump(OpCode.Jump);
            loop.ContinueJumps.Add(jump);
        }

        return null;
    }

    /// <inheritdoc />
    public object? VisitReturnStmt(ReturnStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);
        if (stmt.Value != null)
        {
            CompileExpr(stmt.Value);
        }
        else
        {
            _builder.Emit(OpCode.Null);
        }

        if (_activeFinally is { Count: > 0 })
        {
            // Save return value, run finally blocks, then return
            int saveSlot = _activeFinally![^1].SaveSlot;
            _builder.Emit(OpCode.StoreLocal, (byte)saveSlot);
            EmitPendingFinally();
            _builder.Emit(OpCode.LoadLocal, (byte)saveSlot);
        }

        _builder.Emit(OpCode.Return);
        return null;
    }

}
