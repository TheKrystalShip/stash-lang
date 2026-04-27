using Stash.Parsing.AST;
using Stash.Runtime;

namespace Stash.Bytecode;

public sealed partial class Compiler
{
    /// <inheritdoc />
    public object? VisitLockStmt(LockStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);
        _scope.BeginScope();

        // Allocate consecutive slots for path/wait/stale (VM reads B, B+1, B+2)
        byte pathReg  = _scope.AllocTemp();
        byte waitReg  = _scope.AllocTemp();
        byte staleReg = _scope.AllocTemp();

        // Error scratch register — declared as a named local so it survives scope tracking
        byte errReg = _scope.DeclareLocal("<lock_err>");
        _scope.MarkInitialized();
        _builder.EmitA(OpCode.LoadNull, errReg);

        // Compile path expression → move into pathReg
        byte pathResult = CompileExpr(stmt.Path);
        _builder.EmitAB(OpCode.Move, pathReg, pathResult);
        _scope.FreeTemp(pathResult);

        // Compile wait option → move into waitReg (or LoadNull)
        if (stmt.WaitOption != null)
        {
            byte waitResult = CompileExpr(stmt.WaitOption);
            _builder.EmitAB(OpCode.Move, waitReg, waitResult);
            _scope.FreeTemp(waitResult);
        }
        else
        {
            _builder.EmitA(OpCode.LoadNull, waitReg);
        }

        // Compile stale option → move into staleReg (or LoadNull)
        if (stmt.StaleOption != null)
        {
            byte staleResult = CompileExpr(stmt.StaleOption);
            _builder.EmitAB(OpCode.Move, staleReg, staleResult);
            _scope.FreeTemp(staleResult);
        }
        else
        {
            _builder.EmitA(OpCode.LoadNull, staleReg);
        }

        // Add LockMetadata to constant pool
        int optionCount = (stmt.WaitOption != null ? 1 : 0) + (stmt.StaleOption != null ? 1 : 0);
        byte metaIdx = (byte)_builder.AddConstant(StashValue.FromObj(
            new LockMetadata(optionCount, stmt.WaitOption != null, stmt.StaleOption != null)));

        // Emit LockBegin first — if acquisition fails the error propagates directly without
        // running LockEnd, which would otherwise pop an outer lock from the stack.
        _builder.EmitABC(OpCode.LockBegin, errReg, pathReg, metaIdx);

        // TryBegin protects only the body (lock is already held at this point)
        int errorJump = _builder.EmitJump(OpCode.TryBegin, errReg);

        // Compile the lock body
        CompileStmt(stmt.Body);

        // Success path: end try, release lock, jump past error path
        _builder.EmitAx(OpCode.TryEnd, 0);
        _builder.EmitAx(OpCode.LockEnd, 0);
        int endJump = _builder.EmitJump(OpCode.Jmp);

        // Error path: release lock (if acquired), rethrow
        _builder.PatchJump(errorJump);
        _builder.EmitAx(OpCode.LockEnd, 0);
        _builder.EmitA(OpCode.Throw, errReg);

        _builder.PatchJump(endJump);

        // Free temps in reverse order
        _scope.FreeTemp(staleReg);
        _scope.FreeTemp(waitReg);
        _scope.FreeTemp(pathReg);

        EndScope();
        return null;
    }
}
