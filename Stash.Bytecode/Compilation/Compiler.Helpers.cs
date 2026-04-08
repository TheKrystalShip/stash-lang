using System;
using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;
using Stash.Parsing.AST;
using Stash.Runtime.Types;

namespace Stash.Bytecode;

/// <summary>
/// Compiler helper methods for variable emission, upvalue resolution, and scope management.
/// </summary>
public sealed partial class Compiler
{
    /// <summary>
    /// Emit the appropriate load or store instruction for a variable, based on
    /// <paramref name="resolvedDistance"/>: <c>-1</c> = global, <c>0</c> = local,
    /// <c>&gt;0</c> = captured upvalue.
    /// </summary>
    private void EmitVariable(string name, int resolvedDistance, int resolvedSlot, bool isLoad)
    {
        if (resolvedDistance == -1)
        {
            // Global variable
            ushort slot = _globalSlots.GetOrAllocate(name);
            _builder.Emit(isLoad ? OpCode.LoadGlobal : OpCode.StoreGlobal, slot);
        }
        else
        {
            // Try current function's locals first — the scope chain covers all block depths
            int slot = _scope.ResolveLocal(name);
            if (slot >= 0)
            {
                if (!isLoad && _scope.IsLocalConst(slot))
                {
                    // Const reassignment detected at compile time — emit a runtime throw.
                    // VisitAssignExpr emits Dup before calling EmitVariable, so there are
                    // two copies of the value on the stack: pop both before throwing.
                    _builder.Emit(OpCode.Pop);
                    _builder.Emit(OpCode.Pop);
                    ushort msgIdx = _builder.AddConstant("Assignment to constant variable.");
                    _builder.Emit(OpCode.Const, msgIdx);
                    _builder.Emit(OpCode.Throw);
                }
                else
                {
                    _builder.Emit(isLoad ? OpCode.LoadLocal : OpCode.StoreLocal, (byte)slot);
                }
            }
            else if (resolvedDistance > 0)
            {
                // Not a local in this function — must be an upvalue from an enclosing function
                byte upvalueIdx = ResolveUpvalue(name, resolvedDistance);
                _builder.Emit(isLoad ? OpCode.LoadUpvalue : OpCode.StoreUpvalue, upvalueIdx);
            }
            else
            {
                // Resolver said distance=0 but scope doesn't have it — fall back to global
                ushort globalSlot = _globalSlots.GetOrAllocate(name);
                _builder.Emit(isLoad ? OpCode.LoadGlobal : OpCode.StoreGlobal, globalSlot);
            }
        }
    }

    /// <summary>
    /// Recursively walks up the compiler chain to locate and register an upvalue descriptor.
    /// </summary>
    private byte ResolveUpvalue(string name, int distance)
    {
        if (_enclosing == null)
        {
            return 0; // Shouldn't happen for a properly resolved upvalue
        }

        int localSlot = _enclosing._scope.ResolveLocal(name);
        if (localSlot >= 0)
        {
            // Captured directly from the enclosing function's locals
            byte idx = _builder.AddUpvalue((byte)localSlot, isLocal: true);
            if (idx == _upvalueNames.Count)
            {
                _upvalueNames.Add(name);
            }

            return idx;
        }

        if (distance > 1 && _enclosing._enclosing != null)
        {
            // Transitively captured through the enclosing function's own upvalue chain
            byte enclosingUpvalue = _enclosing.ResolveUpvalue(name, distance - 1);
            byte idx = _builder.AddUpvalue(enclosingUpvalue, isLocal: false);
            if (idx == _upvalueNames.Count)
            {
                _upvalueNames.Add(name);
            }

            return idx;
        }

        return 0;
    }

    /// <summary>
    /// Emit <c>OP_POP</c> instructions to clean up locals that are
    /// <em>deeper</em> than <paramref name="targetDepth"/> without ending the scope
    /// in the <see cref="CompilerScope"/>. Used by <c>break</c> and <c>continue</c>.
    /// </summary>
    private void EmitScopeCleanup(int targetDepth)
    {
        for (int i = _scope.LocalCount - 1; i >= 0; i--)
        {
            Local local = _scope.GetLocal(i);
            if (local.Depth <= targetDepth)
            {
                break;
            }

            _builder.Emit(OpCode.Pop);
        }
    }

    /// <summary>
    /// Ends the current scope and emits one <c>OP_POP</c> per local that was in it.
    /// </summary>
    private void EmitScopePops()
    {
        int popCount = _scope.EndScope();
        for (int i = 0; i < popCount; i++)
        {
            _builder.Emit(OpCode.Pop);
        }
    }

    /// <summary>
    /// Inline-emits all active finally blocks (innermost first) before a
    /// non-local control transfer (break, continue, return).
    /// </summary>
    private void EmitPendingFinally()
    {
        for (int i = _activeFinally.Count - 1; i >= 0; i--)
        {
            FinallyInfo fi = _activeFinally[i];
            for (int h = 0; h < fi.HandlerCount; h++)
            {
                _builder.Emit(OpCode.TryEnd);
            }
            foreach (Stmt s in fi.Body)
            {
                CompileStmt(s);
            }
        }
    }

    /// <summary>
    /// Pops the innermost <see cref="LoopContext"/> and patches all pending break jumps
    /// to the current bytecode offset.
    /// </summary>
    private void PatchBreakJumps()
    {
        LoopContext loop = _loops.Pop();
        foreach (int jump in loop.BreakJumps)
        {
            _builder.PatchJump(jump);
        }
    }

    /// <summary>
    /// Patches all pending continue jumps in <paramref name="ctx"/> to the current
    /// bytecode offset and clears the list.
    /// </summary>
    private static void PatchContinueJumps(LoopContext ctx, ChunkBuilder builder)
    {
        foreach (int jump in ctx.ContinueJumps)
        {
            builder.PatchJump(jump);
        }

        ctx.ContinueJumps.Clear();
    }

    /// <summary>
    /// Emits bytecode that checks each parameter with a default value against the
    /// <see cref="VirtualMachine.NotProvided"/> sentinel, and evaluates the default
    /// expression if the argument was not provided by the caller.
    /// </summary>
    private void EmitDefaultPrologue(List<Token> parameters, List<Expr?> defaultValues, bool hasRestParam)
    {
        // Only process non-rest parameters
        int count = hasRestParam ? parameters.Count - 1 : parameters.Count;

        bool hasAnyDefault = false;
        for (int i = 0; i < count && i < defaultValues.Count; i++)
        {
            if (defaultValues[i] != null)
            {
                hasAnyDefault = true;
                break;
            }
        }
        if (!hasAnyDefault)
        {
            return;
        }

        // Add the NotProvided sentinel to the constant pool once
        ushort notProvidedIdx = _builder.AddConstant(VirtualMachine.NotProvided);

        for (int i = 0; i < count && i < defaultValues.Count; i++)
        {
            Expr? defaultExpr = defaultValues[i];
            if (defaultExpr == null)
            {
                continue;
            }

            // Load the parameter's current value
            _builder.Emit(OpCode.LoadLocal, (byte)i);
            // Load the NotProvided sentinel
            _builder.Emit(OpCode.Const, notProvidedIdx);
            // Compare: true if arg was provided (not equal to sentinel)
            _builder.Emit(OpCode.NotEqual);
            // If arg was provided, skip the default expression
            int skipJump = _builder.EmitJump(OpCode.JumpTrue);

            // Evaluate the default expression
            CompileExpr(defaultExpr);
            // Store the result to the parameter slot
            _builder.Emit(OpCode.StoreLocal, (byte)i);

            // Patch the skip jump
            _builder.PatchJump(skipJump);
        }
    }

    /// <summary>
    /// Compile a function or lambda, emit <c>OP_CLOSURE</c>, and inline upvalue descriptors.
    /// </summary>
    private void CompileFunction(
        string? name,
        List<Token> parameters,
        List<Expr?> defaultValues,
        BlockStmt body,
        bool isAsync,
        bool hasRestParam,
        bool firstParamIsConst = false)
    {
        var fnCompiler = new Compiler(this, name, _globalSlots, _optimize);
        fnCompiler._builder.Arity = parameters.Count;
        fnCompiler._builder.IsAsync = isAsync;
        fnCompiler._builder.HasRestParam = hasRestParam;

        // MinArity = params without defaults (trailing defaults only)
        int paramCount = hasRestParam ? parameters.Count - 1 : parameters.Count;
        int minArity = paramCount;
        for (int i = paramCount - 1; i >= 0; i--)
        {
            if (i < defaultValues.Count && defaultValues[i] != null)
            {
                minArity--;
            }
            else
            {
                break;
            }
        }
        fnCompiler._builder.MinArity = minArity;

        fnCompiler._scope.BeginScope();

        for (int j = 0; j < parameters.Count; j++)
        {
            bool isConst = j == 0 && firstParamIsConst;
            int paramSlot = fnCompiler._scope.DeclareLocal(parameters[j].Lexeme, isConst: isConst);
            fnCompiler._scope.MarkInitialized(paramSlot);
        }

        // Emit default parameter prologue: for each param with a default,
        // check if the arg was provided (not NotProvided sentinel) and
        // evaluate the default expression if missing.
        fnCompiler.EmitDefaultPrologue(parameters, defaultValues, hasRestParam);

        foreach (Stmt s in body.Statements)
        {
            fnCompiler.CompileStmt(s);
        }

        // Implicit return null
        fnCompiler._builder.Emit(OpCode.Null);
        fnCompiler._builder.Emit(OpCode.Return);

        fnCompiler._builder.LocalCount = fnCompiler._scope.PeakLocalCount;
        fnCompiler._builder.LocalNames = fnCompiler._scope.GetPeakLocalNames();
        fnCompiler._builder.LocalIsConst = fnCompiler._scope.GetPeakLocalIsConst();
        fnCompiler._builder.UpvalueNames = fnCompiler._upvalueNames.Count > 0 ? fnCompiler._upvalueNames.ToArray() : null;
        Chunk fnChunk = fnCompiler._builder.Build();

        // Add chunk to constant pool and emit OP_CLOSURE
        ushort chunkIdx = _builder.AddConstant(fnChunk);
        _builder.Emit(OpCode.Closure, chunkIdx);

        // Inline upvalue descriptors immediately after OP_CLOSURE
        foreach (UpvalueDescriptor uv in fnChunk.Upvalues)
        {
            _builder.EmitByte(uv.IsLocal ? (byte)1 : (byte)0);
            _builder.EmitByte(uv.Index);
        }
    }

}
