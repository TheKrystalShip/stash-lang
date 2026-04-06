using System;
using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;
using Stash.Parsing.AST;
using Stash.Runtime.Types;

namespace Stash.Bytecode;

/// <summary>
/// Exception handling and elevation statement visitor implementations.
/// </summary>
public sealed partial class Compiler
{
    /// <inheritdoc />
    public object? VisitElevateStmt(ElevateStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);

        // Push elevator expression or null for platform default
        if (stmt.Elevator != null)
        {
            CompileExpr(stmt.Elevator);
        }
        else
        {
            _builder.Emit(OpCode.Null);
        }

        _builder.Emit(OpCode.ElevateBegin);

        // Wrap body in try-finally so ElevateEnd always runs
        int finallyErrSlot = _scope.DeclareLocal("<elevate_err>", isConst: false);
        _scope.MarkInitialized(finallyErrSlot);
        _builder.Emit(OpCode.Null); // placeholder for error slot

        int errorJump = _builder.EmitJump(OpCode.TryBegin);

        // --- Body ---
        foreach (Stmt s in stmt.Body.Statements)
        {
            CompileStmt(s);
        }

        _builder.Emit(OpCode.TryEnd);

        // --- Success path: ElevateEnd ---
        _builder.Emit(OpCode.ElevateEnd);
        int endJump = _builder.EmitJump(OpCode.Jump);

        // --- Error path: ElevateEnd then re-throw ---
        _builder.PatchJump(errorJump);
        _builder.Emit(OpCode.StoreLocal, (byte)finallyErrSlot);
        _builder.Emit(OpCode.ElevateEnd);
        _builder.Emit(OpCode.LoadLocal, (byte)finallyErrSlot);
        _builder.Emit(OpCode.Throw);

        _builder.PatchJump(endJump);

        return null;
    }

    /// <inheritdoc />
    public object? VisitTryCatchStmt(TryCatchStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);

        bool hasCatch = stmt.CatchBody != null;
        bool hasFinally = stmt.FinallyBody != null;

        // Synthetic local for storing error during finally's error path.
        // Wrapped in BeginScope so it doesn't pollute the enclosing scope's slot count,
        // which would cause outer catch-variable slot misalignment.
        int finallyErrSlot = -1;
        if (hasFinally)
        {
            _scope.BeginScope();
            finallyErrSlot = _scope.DeclareLocal("<finally_err>", isConst: false);
            _scope.MarkInitialized(finallyErrSlot);
            _builder.Emit(OpCode.Null); // placeholder value for the local
        }

        if (hasCatch && hasFinally)
        {
            // Outer handler: catches errors from try body (when catch fails) or catch body itself
            // Ensures finally always runs
            int outerCatchJump = _builder.EmitJump(OpCode.TryBegin);

            // Inner handler: catches errors from try body, routes to catch
            int innerCatchJump = _builder.EmitJump(OpCode.TryBegin);

            // --- Try body ---
            _activeFinally.Add(new FinallyInfo { Body = stmt.FinallyBody!.Statements, SaveSlot = finallyErrSlot, HandlerCount = 2 });
            foreach (Stmt s in stmt.TryBody.Statements)
            {
                CompileStmt(s);
            }

            _builder.Emit(OpCode.TryEnd); // pop inner handler
            int afterCatchJump = _builder.EmitJump(OpCode.Jump); // skip catch

            // --- Catch label (inner handler target) ---
            _builder.PatchJump(innerCatchJump);
            if (stmt.CatchVariable != null)
            {
                _scope.BeginScope();
                int catchVarSlot = _scope.DeclareLocal(stmt.CatchVariable.Lexeme, isConst: false);
                _scope.MarkInitialized(catchVarSlot);
                // Error value is already on stack at the right position for this local
            }
            else
            {
                _builder.Emit(OpCode.Pop); // discard error
            }
            foreach (Stmt s in stmt.CatchBody!.Statements)
            {
                CompileStmt(s);
            }

            if (stmt.CatchVariable != null)
            {
                EmitScopePops();
            }

            _activeFinally.RemoveAt(_activeFinally.Count - 1);

            // --- After catch ---
            _builder.PatchJump(afterCatchJump);
            _builder.Emit(OpCode.TryEnd); // pop outer handler

            // --- Finally (success path) ---
            foreach (Stmt s in stmt.FinallyBody!.Statements)
            {
                CompileStmt(s);
            }

            // Pop <finally_err> slot from the stack on the success path and end its scope
            // so the enclosing scope's subsequent DeclareLocal calls use the correct slot.
            EmitScopePops();
            int endJump = _builder.EmitJump(OpCode.Jump);

            // --- Outer catch label (finally error path) ---
            _builder.PatchJump(outerCatchJump);
            // Error is on stack, store it
            _builder.Emit(OpCode.StoreLocal, (byte)finallyErrSlot);
            // Run finally body
            foreach (Stmt s in stmt.FinallyBody!.Statements)
            {
                CompileStmt(s);
            }
            // Re-throw saved error
            _builder.Emit(OpCode.LoadLocal, (byte)finallyErrSlot);
            _builder.Emit(OpCode.Throw);

            _builder.PatchJump(endJump);
        }
        else if (hasCatch) // catch only, no finally
        {
            int catchJump = _builder.EmitJump(OpCode.TryBegin);

            foreach (Stmt s in stmt.TryBody.Statements)
            {
                CompileStmt(s);
            }

            _builder.Emit(OpCode.TryEnd);
            int endJump = _builder.EmitJump(OpCode.Jump);

            _builder.PatchJump(catchJump);
            if (stmt.CatchVariable != null)
            {
                _scope.BeginScope();
                int catchVarSlot = _scope.DeclareLocal(stmt.CatchVariable.Lexeme, isConst: false);
                _scope.MarkInitialized(catchVarSlot);
            }
            else
            {
                _builder.Emit(OpCode.Pop);
            }
            foreach (Stmt s in stmt.CatchBody!.Statements)
            {
                CompileStmt(s);
            }

            if (stmt.CatchVariable != null)
            {
                EmitScopePops();
            }

            _builder.PatchJump(endJump);
        }
        else if (hasFinally) // finally only, no catch
        {
            int errorJump = _builder.EmitJump(OpCode.TryBegin);

            _activeFinally.Add(new FinallyInfo { Body = stmt.FinallyBody!.Statements, SaveSlot = finallyErrSlot, HandlerCount = 1 });
            foreach (Stmt s in stmt.TryBody.Statements)
            {
                CompileStmt(s);
            }
            _activeFinally.RemoveAt(_activeFinally.Count - 1);

            _builder.Emit(OpCode.TryEnd);

            // Success path: run finally
            foreach (Stmt s in stmt.FinallyBody!.Statements)
            {
                CompileStmt(s);
            }

            // Pop <finally_err> slot from the stack on the success path and end its scope.
            EmitScopePops();
            int endJump = _builder.EmitJump(OpCode.Jump);

            // Error path: store error, run finally, re-throw
            _builder.PatchJump(errorJump);
            _builder.Emit(OpCode.StoreLocal, (byte)finallyErrSlot);
            foreach (Stmt s in stmt.FinallyBody!.Statements)
            {
                CompileStmt(s);
            }

            _builder.Emit(OpCode.LoadLocal, (byte)finallyErrSlot);
            _builder.Emit(OpCode.Throw);

            _builder.PatchJump(endJump);
        }
        else // try only (error suppression)
        {
            int catchJump = _builder.EmitJump(OpCode.TryBegin);

            foreach (Stmt s in stmt.TryBody.Statements)
            {
                CompileStmt(s);
            }

            _builder.Emit(OpCode.TryEnd);
            int endJump = _builder.EmitJump(OpCode.Jump);

            _builder.PatchJump(catchJump);
            _builder.Emit(OpCode.Pop); // discard error

            _builder.PatchJump(endJump);
        }

        return null;
    }

}
