using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;
using Stash.Parsing.AST;

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

        // Declare the error-save slot first so it occupies the lowest available
        // register and cannot conflict with DeclareLocal calls in the body.
        _scope.BeginScope();
        byte savedErrReg = _scope.DeclareLocal("<elevate_err>");
        _scope.MarkInitialized();
        _builder.EmitA(OpCode.LoadNull, savedErrReg);

        // Compile the elevator expression into a temp above savedErrReg,
        // or load null if no elevator was specified.
        byte elevatorReg;
        if (stmt.Elevator != null)
        {
            elevatorReg = CompileExpr(stmt.Elevator);
        }
        else
        {
            elevatorReg = _scope.AllocTemp();
            _builder.EmitA(OpCode.LoadNull, elevatorReg);
        }

        _builder.EmitABC(OpCode.ElevateBegin, 0, elevatorReg, 0);
        _scope.FreeTemp(elevatorReg);

        // Wrap the body in a try-finally so ElevateEnd always runs.
        int errorJump = _builder.EmitJump(OpCode.TryBegin, savedErrReg);

        CompileStmt(stmt.Body);

        _builder.EmitAx(OpCode.TryEnd, 0);

        // Success path: end elevation then jump to end.
        _builder.EmitAx(OpCode.ElevateEnd, 0);
        int endJump = _builder.EmitJump(OpCode.Jmp);

        // Error path: end elevation then re-throw.
        _builder.PatchJump(errorJump);
        _builder.EmitAx(OpCode.ElevateEnd, 0);
        _builder.EmitA(OpCode.Throw, savedErrReg);

        _builder.PatchJump(endJump);
        EndScope();

        return null;
    }

    /// <inheritdoc />
    public object? VisitTryCatchStmt(TryCatchStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);

        bool hasCatch = stmt.CatchClauses.Count > 0;
        bool hasFinally = stmt.FinallyBody != null;

        if (hasCatch && hasFinally)
            CompileTryCatchFinally(stmt);
        else if (hasCatch)
            CompileTryCatch(stmt);
        else if (hasFinally)
            CompileTryFinally(stmt);
        else
            CompileTryOnly(stmt);

        return null;
    }

    // ── try { } catch [(e)] { }  — no finally ──────────────────────────────
    private void CompileTryCatch(TryCatchStmt stmt)
    {
        IReadOnlyList<CatchClause> clauses = stmt.CatchClauses;

        // Declare the shared error register as a hidden local so try body registers
        // start above it and cannot alias it.
        _scope.BeginScope();
        byte errReg = _scope.DeclareLocal("<catch_err>");
        _scope.MarkInitialized();

        int tryBeginIdx = _builder.EmitJump(OpCode.TryBegin, errReg);

        // CompileStmt delegates to VisitBlockStmt, which opens its own nested scope.
        CompileStmt(stmt.TryBody);

        _builder.EmitAx(OpCode.TryEnd, 0);
        int endJump = _builder.EmitJump(OpCode.Jmp);

        // Catch dispatch point: TryBegin jumps here with errReg holding the caught exception.
        _builder.PatchJump(tryBeginIdx);

        var clauseEndJumps = new List<int>(clauses.Count);

        for (int i = 0; i < clauses.Count; i++)
        {
            CatchClause clause = clauses[i];
            bool isCatchAll = clause.IsCatchAll;

            // Emit CatchMatch with the type names constant.
            // CatchMatch: on match, skips the following Jmp (falls through to clause body).
            //             on no match, the following Jmp executes to route to the next clause.
            string[] typeNames = isCatchAll
                ? System.Array.Empty<string>()
                : GetTypeNames(clause);
            ushort constIdx = _builder.AddConstant((object)typeNames);
            _builder.EmitABx(OpCode.CatchMatch, errReg, constIdx);
            int noMatchJump = _builder.EmitJump(OpCode.Jmp); // jumped over by CatchMatch on match

            // Clause body: open a new scope with the user's variable bound to errReg.
            _scope.BeginScope();
            byte clauseVar = _scope.DeclareLocal(clause.Variable.Lexeme);
            _scope.MarkInitialized();
            if (clauseVar != errReg)
                _builder.EmitABC(OpCode.Move, clauseVar, errReg, 0);

            byte savedCatchReg = _activeCatchErrReg;
            _activeCatchErrReg = errReg;
            CompileStmt(clause.Body);
            _activeCatchErrReg = savedCatchReg;

            EndScope(); // releases clauseVar
            clauseEndJumps.Add(_builder.EmitJump(OpCode.Jmp)); // jump past all catches

            // Patch the no-match jump to HERE (start of next clause or rethrow).
            _builder.PatchJump(noMatchJump);

            if (isCatchAll) break; // no rethrow needed after a catch-all
        }

        // If the last clause is not a catch-all, rethrow for unmatched errors.
        if (!clauses[^1].IsCatchAll)
            _builder.EmitA(OpCode.Rethrow, errReg);

        EndScope(); // releases errReg

        // Patch the try-body end jump and all clause-end jumps to here.
        _builder.PatchJump(endJump);
        foreach (int j in clauseEndJumps)
            _builder.PatchJump(j);
    }

    private static string[] GetTypeNames(CatchClause clause)
    {
        var names = new string[clause.TypeTokens.Count];
        for (int i = 0; i < clause.TypeTokens.Count; i++)
            names[i] = clause.TypeTokens[i].Lexeme;
        return names;
    }

    // ── try { } finally { }  — no catch ────────────────────────────────────
    private void CompileTryFinally(TryCatchStmt stmt)
    {
        _scope.BeginScope();
        byte savedErrReg = _scope.DeclareLocal("<finally_err>");
        _scope.MarkInitialized();
        _builder.EmitA(OpCode.LoadNull, savedErrReg);

        int errorJump = _builder.EmitJump(OpCode.TryBegin, savedErrReg);

        // Track this finally block so break/continue/return can inline finally code.
        var finallyInfo = new FinallyInfo { FinallyStart = -1, ScopeDepth = _scope.ScopeDepth, Body = stmt.FinallyBody };
        (_activeFinally ??= new()).Add(finallyInfo);

        CompileStmt(stmt.TryBody);

        _builder.EmitAx(OpCode.TryEnd, 0);

        // FinallyStart is now known: the first instruction of the success finally body.
        finallyInfo.FinallyStart = _builder.CurrentOffset;
        _activeFinally.RemoveAt(_activeFinally.Count - 1);

        // Success path: run finally body, then jump to end.
        CompileStmt(stmt.FinallyBody!);
        int endJump = _builder.EmitJump(OpCode.Jmp);

        // Error path: savedErrReg holds the caught error; run finally then re-throw.
        _builder.PatchJump(errorJump);
        CompileStmt(stmt.FinallyBody!);
        _builder.EmitA(OpCode.Throw, savedErrReg);

        // EndScope is called after both paths are emitted so that savedErrReg
        // remains live (and its register unaliased) during the error-path finally body.
        _builder.PatchJump(endJump);
        EndScope();
    }

    // ── try { } catch [(e)] { } finally { } ────────────────────────────────
    private void CompileTryCatchFinally(TryCatchStmt stmt)
    {
        IReadOnlyList<CatchClause> clauses = stmt.CatchClauses;

        // Outer handler: catches any error that escapes (or is re-thrown by)
        // the catch body, ensuring finally always runs on the error path.
        _scope.BeginScope();
        byte savedErrReg = _scope.DeclareLocal("<finally_err>");
        _scope.MarkInitialized();
        _builder.EmitA(OpCode.LoadNull, savedErrReg);

        int outerTryBeginIdx = _builder.EmitJump(OpCode.TryBegin, savedErrReg);

        // Inner handler: catches errors from the try body and routes them to
        // the catch dispatch. Declared as a hidden local for the same reason as CompileTryCatch.
        _scope.BeginScope();
        byte errReg = _scope.DeclareLocal("<catch_err>");
        _scope.MarkInitialized();

        int innerTryBeginIdx = _builder.EmitJump(OpCode.TryBegin, errReg);

        var finallyInfo = new FinallyInfo { FinallyStart = -1, ScopeDepth = _scope.ScopeDepth, Body = stmt.FinallyBody };
        (_activeFinally ??= new()).Add(finallyInfo);

        CompileStmt(stmt.TryBody);

        _builder.EmitAx(OpCode.TryEnd, 0); // pop inner handler
        int afterCatchJump = _builder.EmitJump(OpCode.Jmp); // skip catch dispatch

        // Inner catch dispatch point: entered when an exception is caught from try body.
        _builder.PatchJump(innerTryBeginIdx);

        var clauseEndJumps = new List<int>(clauses.Count);

        for (int i = 0; i < clauses.Count; i++)
        {
            CatchClause clause = clauses[i];
            bool isCatchAll = clause.IsCatchAll;

            string[] typeNames = isCatchAll
                ? System.Array.Empty<string>()
                : GetTypeNames(clause);
            ushort constIdx = _builder.AddConstant((object)typeNames);
            _builder.EmitABx(OpCode.CatchMatch, errReg, constIdx);
            int noMatchJump = _builder.EmitJump(OpCode.Jmp);

            _scope.BeginScope();
            byte clauseVar = _scope.DeclareLocal(clause.Variable.Lexeme);
            _scope.MarkInitialized();
            if (clauseVar != errReg)
                _builder.EmitABC(OpCode.Move, clauseVar, errReg, 0);

            byte savedCatchReg = _activeCatchErrReg;
            _activeCatchErrReg = errReg;
            CompileStmt(clause.Body);
            _activeCatchErrReg = savedCatchReg;

            EndScope();
            clauseEndJumps.Add(_builder.EmitJump(OpCode.Jmp));

            _builder.PatchJump(noMatchJump);

            if (isCatchAll) break;
        }

        // If the last clause is not a catch-all, rethrow for unmatched errors.
        if (!clauses[^1].IsCatchAll)
            _builder.EmitA(OpCode.Rethrow, errReg);

        EndScope(); // releases errReg (inner scope)

        _activeFinally.RemoveAt(_activeFinally.Count - 1);

        // Rejoin point: try-body (no exception) and catch-body flows merge here.
        _builder.PatchJump(afterCatchJump);
        foreach (int j in clauseEndJumps)
            _builder.PatchJump(j);

        _builder.EmitAx(OpCode.TryEnd, 0); // pop outer handler

        // FinallyStart is now known: the first instruction of the success finally body.
        finallyInfo.FinallyStart = _builder.CurrentOffset;

        // Success path: run finally body, then jump to end.
        CompileStmt(stmt.FinallyBody!);
        int endJump = _builder.EmitJump(OpCode.Jmp);

        // Outer catch handler: savedErrReg holds the error; run finally then re-throw.
        _builder.PatchJump(outerTryBeginIdx);
        CompileStmt(stmt.FinallyBody!);
        _builder.EmitA(OpCode.Throw, savedErrReg);

        // EndScope for savedErrReg after both paths, so it stays live during
        // the error-path finally body compilation.
        _builder.PatchJump(endJump);
        EndScope(); // releases savedErrReg (outer scope)
    }

    // ── try { }  — error suppression, no catch/finally ─────────────────────
    private void CompileTryOnly(TryCatchStmt stmt)
    {
        // Declare as a local so it doesn't alias any DeclareLocal calls in the try body.
        _scope.BeginScope();
        byte errReg = _scope.DeclareLocal("<try_err>");
        _scope.MarkInitialized();

        int tryBeginIdx = _builder.EmitJump(OpCode.TryBegin, errReg);

        CompileStmt(stmt.TryBody);

        _builder.EmitAx(OpCode.TryEnd, 0);
        int endJump = _builder.EmitJump(OpCode.Jmp);

        // Error suppression: the caught exception in errReg is silently discarded.
        _builder.PatchJump(tryBeginIdx);

        _builder.PatchJump(endJump);
        EndScope(); // releases errReg
    }
}
