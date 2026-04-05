using System;
using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;
using Stash.Parsing.AST;
using Stash.Runtime.Types;

namespace Stash.Bytecode;

/// <summary>
/// Visits a resolved AST and emits bytecode instructions into a <see cref="Chunk"/> via
/// <see cref="ChunkBuilder"/>. A new <see cref="Compiler"/> instance is created for each
/// function (or the top-level script), with <see cref="_enclosing"/> pointing to the compiler
/// of the immediately enclosing function for upvalue resolution.
/// </summary>
public sealed class Compiler : IExprVisitor<object?>, IStmtVisitor<object?>
{
    private readonly ChunkBuilder _builder;
    private readonly CompilerScope _scope;
    private readonly Compiler? _enclosing;

    /// <summary>
    /// Tracks loop state for break/continue jump patching.
    /// A <c>class</c> rather than a <c>struct</c> so that mutations via
    /// <see cref="Stack{T}.Peek"/> are visible without re-pushing.
    /// </summary>
    private sealed class LoopContext
    {
        /// <summary>Bytecode offset to loop back to (used by <c>EmitLoop</c>).</summary>
        public int LoopStart;

        /// <summary>
        /// Offset that <c>continue</c> should jump to, or <c>-1</c> if the target is not yet
        /// known (do-while condition / for-loop increment — resolved before the loop ends via
        /// <c>ContinueJumps</c>).
        /// </summary>
        public int ContinueTarget;

        /// <summary>Forward jump offsets emitted by <c>break</c> statements, patched on loop exit.</summary>
        public readonly List<int> BreakJumps = new();

        /// <summary>
        /// Forward jump offsets emitted by <c>continue</c> statements when
        /// <see cref="ContinueTarget"/> is not yet known (do-while, for-loop).
        /// Patched when the continue target is determined.
        /// </summary>
        public readonly List<int> ContinueJumps = new();

        /// <summary>Scope depth at loop entry, used by <c>EmitScopeCleanup</c>.</summary>
        public int ScopeDepth;
    }

    private readonly Stack<LoopContext> _loops = new();

    private readonly List<FinallyInfo> _activeFinally = new();

    private struct FinallyInfo
    {
        public List<Stmt> Body;
        public int SaveSlot;
        public int HandlerCount;
    }

    /// <summary>Names of captured upvalues, in capture order, for debugger closure scope display.</summary>
    private readonly List<string> _upvalueNames = new();

    // ---- Construction ----

    private Compiler(Compiler? enclosing, string? name)
    {
        _builder = new ChunkBuilder { Name = name };
        _scope = new CompilerScope();
        _enclosing = enclosing;
    }

    // ---- Public API ----

    /// <summary>
    /// Compile a list of resolved statements into a top-level script <see cref="Chunk"/>.
    /// </summary>
    /// <param name="statements">The resolved program statements to compile.</param>
    /// <returns>The compiled script chunk, ready for execution by the VM.</returns>
    public static Chunk Compile(List<Stmt> statements)
    {
        var compiler = new Compiler(null, null);
        foreach (Stmt stmt in statements)
        {
            compiler.CompileStmt(stmt);
        }
        // Implicit return null at end of script
        compiler._builder.Emit(OpCode.Null);
        compiler._builder.Emit(OpCode.Return);
        compiler._builder.LocalCount = compiler._scope.PeakLocalCount;
        compiler._builder.LocalNames = compiler._scope.GetPeakLocalNames();
        compiler._builder.LocalIsConst = compiler._scope.GetPeakLocalIsConst();
        return compiler._builder.Build();
    }

    /// <summary>
    /// Compiles a single expression into a Chunk that returns the expression's value.
    /// Used by StashEngine.Evaluate() for the bytecode backend.
    /// </summary>
    public static Chunk CompileExpression(Expr expression)
    {
        var compiler = new Compiler(null, null);
        compiler.CompileExpr(expression);
        compiler._builder.Emit(OpCode.Return);
        compiler._builder.LocalCount = compiler._scope.PeakLocalCount;
        compiler._builder.LocalNames = compiler._scope.GetPeakLocalNames();
        compiler._builder.LocalIsConst = compiler._scope.GetPeakLocalIsConst();
        return compiler._builder.Build();
    }

    // ---- Core Helpers ----

    private void CompileStmt(Stmt stmt) => stmt.Accept(this);
    private void CompileExpr(Expr expr) => expr.Accept(this);

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
            ushort nameIdx = _builder.AddConstant(name);
            _builder.Emit(isLoad ? OpCode.LoadGlobal : OpCode.StoreGlobal, nameIdx);
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
                ushort nameIdx = _builder.AddConstant(name);
                _builder.Emit(isLoad ? OpCode.LoadGlobal : OpCode.StoreGlobal, nameIdx);
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
        var fnCompiler = new Compiler(this, name);
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

    // =========================================================================
    // Statement Visitors
    // =========================================================================

    /// <inheritdoc />
    public object? VisitExprStmt(ExprStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);
        CompileExpr(stmt.Expression);
        _builder.Emit(OpCode.Pop);
        return null;
    }

    /// <inheritdoc />
    public object? VisitVarDeclStmt(VarDeclStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);
        int slot = _scope.DeclareLocal(stmt.Name.Lexeme, isConst: false);

        if (stmt.Initializer != null)
        {
            CompileExpr(stmt.Initializer);
        }
        else
        {
            _builder.Emit(OpCode.Null);
        }

        _scope.MarkInitialized(slot);
        // The initializer value on the stack IS the local — no separate store needed.

        // At top-level, also seed globals so that OP_LOAD_GLOBAL (emitted because
        // the Resolver leaves top-level variables unresolved at distance=-1) can
        // find the initial value.
        if (_enclosing is null && _scope.ScopeDepth == 0)
        {
            int resolvedSlot = _scope.ResolveLocal(stmt.Name.Lexeme);
            if (resolvedSlot >= 0)
            {
                _builder.Emit(OpCode.LoadLocal, (byte)resolvedSlot);
                ushort nameIdx = _builder.AddConstant(stmt.Name.Lexeme);
                _builder.Emit(OpCode.StoreGlobal, nameIdx);
            }
        }

        return null;
    }

    /// <inheritdoc />
    public object? VisitConstDeclStmt(ConstDeclStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);
        int slot = _scope.DeclareLocal(stmt.Name.Lexeme, isConst: true);
        CompileExpr(stmt.Initializer);
        _scope.MarkInitialized(slot);

        // At top-level, also seed globals so that OP_LOAD_GLOBAL (emitted because
        // the Resolver leaves top-level variables unresolved at distance=-1) can
        // find the initial value. Use InitConstGlobal so the VM marks the name as
        // immutable and throws on any subsequent StoreGlobal to the same name.
        if (_enclosing is null)
        {
            int resolvedSlot = _scope.ResolveLocal(stmt.Name.Lexeme);
            if (resolvedSlot >= 0)
            {
                _builder.Emit(OpCode.LoadLocal, (byte)resolvedSlot);
                ushort nameIdx = _builder.AddConstant(stmt.Name.Lexeme);
                _builder.Emit(OpCode.InitConstGlobal, nameIdx);
            }
        }

        return null;
    }

    /// <inheritdoc />
    public object? VisitBlockStmt(BlockStmt stmt)
    {
        _scope.BeginScope();
        foreach (Stmt s in stmt.Statements)
        {
            CompileStmt(s);
        }

        EmitScopePops();
        return null;
    }

    /// <inheritdoc />
    public object? VisitIfStmt(IfStmt stmt)
    {
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
        _loops.Push(loopCtx);

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
        _loops.Push(loopCtx);

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
        _loops.Push(loopCtx);

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
        _loops.Push(loopCtx);

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
        if (_loops.Count == 0)
        {
            throw new CompileError("'break' outside of loop.", stmt.Span);
        }

        EmitPendingFinally();
        EmitScopeCleanup(_loops.Peek().ScopeDepth);

        int jump = _builder.EmitJump(OpCode.Jump);
        _loops.Peek().BreakJumps.Add(jump);
        return null;
    }

    /// <inheritdoc />
    public object? VisitContinueStmt(ContinueStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);
        if (_loops.Count == 0)
        {
            throw new CompileError("'continue' outside of loop.", stmt.Span);
        }

        EmitPendingFinally();

        LoopContext loop = _loops.Peek();
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

        if (_activeFinally.Count > 0)
        {
            // Save return value, run finally blocks, then return
            int saveSlot = _activeFinally[^1].SaveSlot;
            _builder.Emit(OpCode.StoreLocal, (byte)saveSlot);
            EmitPendingFinally();
            _builder.Emit(OpCode.LoadLocal, (byte)saveSlot);
        }

        _builder.Emit(OpCode.Return);
        return null;
    }

    /// <inheritdoc />
    public object? VisitFnDeclStmt(FnDeclStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);

        // Declare the function name as a local before compiling its body (enables recursion)
        int slot = _scope.DeclareLocal(stmt.Name.Lexeme, isConst: false);
        _scope.MarkInitialized(slot);

        CompileFunction(
            stmt.Name.Lexeme,
            stmt.Parameters,
            stmt.DefaultValues,
            stmt.Body,
            stmt.IsAsync,
            stmt.HasRestParam);

        // Dup + StoreGlobal: needed because references use LoadGlobal (distance=-1 at top level)
        // and harmless in local scopes where LoadLocal is used instead
        _builder.Emit(OpCode.Dup);
        {
            ushort nameIdx = _builder.AddConstant(stmt.Name.Lexeme);
            _builder.Emit(OpCode.StoreGlobal, nameIdx);
        }

        return null;
    }

    /// <inheritdoc />
    public object? VisitThrowStmt(ThrowStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);
        CompileExpr(stmt.Value);
        _builder.Emit(OpCode.Throw);
        return null;
    }

    // --- Deferred statement visitors ---

    /// <inheritdoc />
    public object? VisitStructDeclStmt(StructDeclStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);

        int slot = _scope.DeclareLocal(stmt.Name.Lexeme, isConst: false);
        _scope.MarkInitialized(slot);

        // Compile each method as a closure (pushes VMFunction onto stack)
        var methodNames = new string[stmt.Methods.Count];
        for (int i = 0; i < stmt.Methods.Count; i++)
        {
            methodNames[i] = stmt.Methods[i].Name.Lexeme;

            // Prepend a synthetic 'self' parameter so the VM's arity check passes.
            // CallValue for VMBoundMethod does argc+1 (inserting self), so expected must match.
            // Only add if not already explicitly declared by the user.
            List<Token> methodParams;
            List<Expr?> methodDefaults;
            bool hasSelfParam = stmt.Methods[i].Parameters.Count > 0
                && stmt.Methods[i].Parameters[0].Lexeme == "self";

            if (hasSelfParam)
            {
                methodParams = stmt.Methods[i].Parameters;
                methodDefaults = stmt.Methods[i].DefaultValues;
            }
            else
            {
                methodParams = new List<Token>();
                methodParams.Add(new Token(TokenType.Identifier, "self", null, stmt.Methods[i].Name.Span));
                methodParams.AddRange(stmt.Methods[i].Parameters);

                methodDefaults = new List<Expr?>();
                methodDefaults.Add(null); // self has no default
                methodDefaults.AddRange(stmt.Methods[i].DefaultValues);
            }

            CompileFunction(
                stmt.Methods[i].Name.Lexeme,
                methodParams,
                methodDefaults,
                stmt.Methods[i].Body,
                stmt.Methods[i].IsAsync,
                stmt.Methods[i].HasRestParam);
        }

        string[] fields = new string[stmt.Fields.Count];
        for (int i = 0; i < stmt.Fields.Count; i++)
        {
            fields[i] = stmt.Fields[i].Lexeme;
        }

        string[] interfaceNames = new string[stmt.Interfaces.Count];
        for (int i = 0; i < stmt.Interfaces.Count; i++)
        {
            interfaceNames[i] = stmt.Interfaces[i].Lexeme;
        }

        var metadata = new StructMetadata(stmt.Name.Lexeme, fields, methodNames, interfaceNames);
        ushort metaIdx = _builder.AddConstant(metadata);
        _builder.Emit(OpCode.StructDecl, metaIdx);

        // Dup + StoreGlobal: needed because references use LoadGlobal (distance=-1 at top level)
        // and harmless in local scopes where LoadLocal is used instead
        _builder.Emit(OpCode.Dup);
        {
            ushort nameIdx = _builder.AddConstant(stmt.Name.Lexeme);
            _builder.Emit(OpCode.StoreGlobal, nameIdx);
        }

        return null;
    }

    /// <inheritdoc />
    public object? VisitEnumDeclStmt(EnumDeclStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);

        int slot = _scope.DeclareLocal(stmt.Name.Lexeme, isConst: false);
        _scope.MarkInitialized(slot);

        string[] members = new string[stmt.Members.Count];
        for (int i = 0; i < stmt.Members.Count; i++)
        {
            members[i] = stmt.Members[i].Lexeme;
        }

        var metadata = new EnumMetadata(stmt.Name.Lexeme, members);
        ushort metaIdx = _builder.AddConstant(metadata);
        _builder.Emit(OpCode.EnumDecl, metaIdx);

        _builder.Emit(OpCode.Dup);
        {
            ushort nameIdx = _builder.AddConstant(stmt.Name.Lexeme);
            _builder.Emit(OpCode.StoreGlobal, nameIdx);
        }

        return null;
    }

    /// <inheritdoc />
    public object? VisitInterfaceDeclStmt(InterfaceDeclStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);

        int slot = _scope.DeclareLocal(stmt.Name.Lexeme, isConst: false);
        _scope.MarkInitialized(slot);

        var fields = new InterfaceField[stmt.Fields.Count];
        for (int i = 0; i < stmt.Fields.Count; i++)
        {
            string fieldName = stmt.Fields[i].Lexeme;
            string? typeHint = i < stmt.FieldTypes.Count ? stmt.FieldTypes[i]?.Lexeme : null;
            fields[i] = new InterfaceField(fieldName, typeHint);
        }

        var methods = new InterfaceMethod[stmt.Methods.Count];
        for (int i = 0; i < stmt.Methods.Count; i++)
        {
            InterfaceMethodSignature sig = stmt.Methods[i];
            var paramNames = new List<string>();
            foreach (Token p in sig.Parameters)
            {
                paramNames.Add(p.Lexeme);
            }

            var paramTypes = new List<string?>();
            for (int j = 0; j < sig.Parameters.Count; j++)
            {
                string? paramType = j < sig.ParameterTypes.Count ? sig.ParameterTypes[j]?.Lexeme : null;
                paramTypes.Add(paramType);
            }

            string? returnType = sig.ReturnType?.Lexeme;
            methods[i] = new InterfaceMethod(sig.Name.Lexeme, sig.Parameters.Count, paramNames, paramTypes, returnType);
        }

        var metadata = new InterfaceMetadata(stmt.Name.Lexeme, fields, methods);
        ushort metaIdx = _builder.AddConstant(metadata);
        _builder.Emit(OpCode.InterfaceDecl, metaIdx);

        _builder.Emit(OpCode.Dup);
        {
            ushort nameIdx = _builder.AddConstant(stmt.Name.Lexeme);
            _builder.Emit(OpCode.StoreGlobal, nameIdx);
        }

        return null;
    }

    /// <inheritdoc />
    public object? VisitExtendStmt(ExtendStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);

        if (!(_enclosing is null && _scope.ScopeDepth == 0))
        {
            ushort msgIdx = _builder.AddConstant("'extend' blocks must be declared at the top level.");
            _builder.Emit(OpCode.Const, msgIdx);
            _builder.Emit(OpCode.Throw);
            return null;
        }

        string typeName = stmt.TypeName.Lexeme;
        bool isBuiltIn = typeName is "string" or "array" or "dict" or "int" or "float";

        var methodNames = new string[stmt.Methods.Count];
        for (int i = 0; i < stmt.Methods.Count; i++)
        {
            methodNames[i] = stmt.Methods[i].Name.Lexeme;

            List<Token> methodParams;
            List<Expr?> methodDefaults;
            bool hasSelfParam = stmt.Methods[i].Parameters.Count > 0
                && stmt.Methods[i].Parameters[0].Lexeme == "self";

            if (!hasSelfParam)
            {
                methodParams = new List<Token>();
                methodParams.Add(new Token(TokenType.Identifier, "self", null, stmt.Methods[i].Name.Span));
                methodParams.AddRange(stmt.Methods[i].Parameters);

                methodDefaults = new List<Expr?>();
                methodDefaults.Add(null); // self has no default
                methodDefaults.AddRange(stmt.Methods[i].DefaultValues);
            }
            else
            {
                methodParams = stmt.Methods[i].Parameters;
                methodDefaults = stmt.Methods[i].DefaultValues;
            }

            CompileFunction(
                stmt.Methods[i].Name.Lexeme,
                methodParams,
                methodDefaults,
                stmt.Methods[i].Body,
                stmt.Methods[i].IsAsync,
                stmt.Methods[i].HasRestParam,
                firstParamIsConst: !hasSelfParam);
        }

        var metadata = new ExtendMetadata(typeName, methodNames, isBuiltIn);
        ushort metaIdx = _builder.AddConstant(metadata);
        _builder.Emit(OpCode.Extend, metaIdx);

        return null;
    }

    /// <inheritdoc />
    public object? VisitImportStmt(ImportStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);

        // Compile path expression (pushes module path string onto stack)
        CompileExpr(stmt.Path);

        string[] names = new string[stmt.Names.Count];
        for (int i = 0; i < stmt.Names.Count; i++)
        {
            names[i] = stmt.Names[i].Lexeme;
        }

        var metadata = new ImportMetadata(names);
        ushort metaIdx = _builder.AddConstant(metadata);
        _builder.Emit(OpCode.Import, metaIdx);
        // VM pops path, loads module, pushes N values onto stack (one per name)

        // Declare each imported name as a local
        foreach (Token name in stmt.Names)
        {
            int slot = _scope.DeclareLocal(name.Lexeme, isConst: false);
            _scope.MarkInitialized(slot);
            if (_enclosing is null && _scope.ScopeDepth == 0)
            {
                _builder.Emit(OpCode.LoadLocal, (byte)slot);
                ushort nameIdx = _builder.AddConstant(name.Lexeme);
                _builder.Emit(OpCode.StoreGlobal, nameIdx);
            }
        }

        return null;
    }

    /// <inheritdoc />
    public object? VisitImportAsStmt(ImportAsStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);

        CompileExpr(stmt.Path);

        var metadata = new ImportAsMetadata(stmt.Alias.Lexeme);
        ushort metaIdx = _builder.AddConstant(metadata);
        _builder.Emit(OpCode.ImportAs, metaIdx);
        // VM pops path, loads module, wraps as StashNamespace, pushes 1 value

        int slot = _scope.DeclareLocal(stmt.Alias.Lexeme, isConst: false);
        _scope.MarkInitialized(slot);
        if (_enclosing is null && _scope.ScopeDepth == 0)
        {
            _builder.Emit(OpCode.LoadLocal, (byte)slot);
            ushort nameIdx = _builder.AddConstant(stmt.Alias.Lexeme);
            _builder.Emit(OpCode.StoreGlobal, nameIdx);
        }

        return null;
    }

    /// <inheritdoc />
    public object? VisitDestructureStmt(DestructureStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);

        // Compile the initializer expression (pushes array/dict onto stack)
        CompileExpr(stmt.Initializer);

        string kind = stmt.Kind == DestructureStmt.PatternKind.Array ? "array" : "object";
        string[] names = new string[stmt.Names.Count];
        for (int i = 0; i < stmt.Names.Count; i++)
        {
            names[i] = stmt.Names[i].Lexeme;
        }

        var metadata = new DestructureMetadata(kind, names, stmt.RestName?.Lexeme, stmt.IsConst);
        ushort metaIdx = _builder.AddConstant(metadata);
        _builder.Emit(OpCode.Destructure, metaIdx);
        // VM pops initializer, pushes N values (one per name + optional rest)

        // Declare each name as a local
        foreach (Token name in stmt.Names)
        {
            int slot = _scope.DeclareLocal(name.Lexeme, isConst: stmt.IsConst);
            _scope.MarkInitialized(slot);

            if (_enclosing is null && _scope.ScopeDepth == 0)
            {
                _builder.Emit(OpCode.LoadLocal, (byte)slot);
                ushort nameIdx = _builder.AddConstant(name.Lexeme);
                _builder.Emit(stmt.IsConst ? OpCode.InitConstGlobal : OpCode.StoreGlobal, nameIdx);
            }
        }

        // Declare rest name if present
        if (stmt.RestName != null)
        {
            int restSlot = _scope.DeclareLocal(stmt.RestName.Lexeme, isConst: stmt.IsConst);
            _scope.MarkInitialized(restSlot);

            if (_enclosing is null && _scope.ScopeDepth == 0)
            {
                _builder.Emit(OpCode.LoadLocal, (byte)restSlot);
                ushort restNameIdx = _builder.AddConstant(stmt.RestName.Lexeme);
                _builder.Emit(stmt.IsConst ? OpCode.InitConstGlobal : OpCode.StoreGlobal, restNameIdx);
            }
        }

        return null;
    }

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
    public object? VisitArrayExpr(ArrayExpr expr)
    {
        foreach (Expr element in expr.Elements)
        {
            CompileExpr(element);
        }

        _builder.Emit(OpCode.Array, (ushort)expr.Elements.Count);
        return null;
    }

    /// <inheritdoc />
    public object? VisitIndexExpr(IndexExpr expr)
    {
        _builder.AddSourceMapping(expr.BracketSpan);
        CompileExpr(expr.Object);
        CompileExpr(expr.Index);
        _builder.Emit(OpCode.GetIndex);
        return null;
    }

    /// <inheritdoc />
    public object? VisitIndexAssignExpr(IndexAssignExpr expr)
    {
        _builder.AddSourceMapping(expr.BracketSpan);
        CompileExpr(expr.Object);
        CompileExpr(expr.Index);
        CompileExpr(expr.Value);
        _builder.Emit(OpCode.SetIndex);
        return null;
    }

    /// <inheritdoc />
    public object? VisitStructInitExpr(StructInitExpr expr)
    {
        _builder.AddSourceMapping(expr.Span);

        // Load the struct type
        if (expr.Target != null)
        {
            CompileExpr(expr.Target);
        }
        else
        {
            ushort nameIdx = _builder.AddConstant(expr.Name.Lexeme);
            _builder.Emit(OpCode.LoadGlobal, nameIdx);
        }

        // Push each field name string followed by its value
        foreach ((Token field, Expr value) in expr.FieldValues)
        {
            ushort fieldIdx = _builder.AddConstant(field.Lexeme);
            _builder.Emit(OpCode.Const, fieldIdx);
            CompileExpr(value);
        }

        _builder.Emit(OpCode.StructInit, (ushort)expr.FieldValues.Count);
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
    public object? VisitInterpolatedStringExpr(InterpolatedStringExpr expr)
    {
        foreach (Expr part in expr.Parts)
        {
            CompileExpr(part);
        }

        _builder.Emit(OpCode.Interpolate, (ushort)expr.Parts.Count);
        return null;
    }

    /// <inheritdoc />
    public object? VisitCommandExpr(CommandExpr expr)
    {
        _builder.AddSourceMapping(expr.Span);
        foreach (Expr part in expr.Parts)
        {
            CompileExpr(part);
        }

        var metadata = new CommandMetadata(expr.Parts.Count, expr.IsPassthrough, expr.IsStrict);
        ushort metaIdx = _builder.AddConstant(metadata);
        _builder.Emit(OpCode.Command, metaIdx);
        return null;
    }

    /// <inheritdoc />
    public object? VisitPipeExpr(PipeExpr expr)
    {
        _builder.AddSourceMapping(expr.Span);
        CompileExpr(expr.Left);
        CompileExpr(expr.Right);
        _builder.Emit(OpCode.Pipe);
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
    public object? VisitUpdateExpr(UpdateExpr expr)
    {
        _builder.AddSourceMapping(expr.Span);

        if (expr.Operand is IdentifierExpr id)
        {
            // Load the current value
            EmitVariable(id.Name.Lexeme, id.ResolvedDistance, id.ResolvedSlot, isLoad: true);
            _builder.Emit(OpCode.CheckNumeric);

            if (!expr.IsPrefix)
            {
                _builder.Emit(OpCode.Dup);  // postfix: preserve old value as expression result
            }

            ushort oneIdx = _builder.AddConstant(1L);
            _builder.Emit(OpCode.Const, oneIdx);
            _builder.Emit(expr.Operator.Type == TokenType.PlusPlus ? OpCode.Add : OpCode.Subtract);

            if (expr.IsPrefix)
            {
                _builder.Emit(OpCode.Dup);  // prefix: preserve new value as expression result
            }

            EmitVariable(id.Name.Lexeme, id.ResolvedDistance, id.ResolvedSlot, isLoad: false);
            return null;
        }

        if (expr.Operand is DotExpr dot)
        {
            ushort nameIdx = _builder.AddConstant(dot.Name.Lexeme);

            if (expr.IsPrefix)
            {
                CompileExpr(dot.Object);                          // [obj]
                _builder.Emit(OpCode.Dup);                        // [obj, obj]
                _builder.Emit(OpCode.GetField, nameIdx);          // [obj, oldVal]
                _builder.Emit(OpCode.CheckNumeric);
                ushort oneIdx = _builder.AddConstant(1L);
                _builder.Emit(OpCode.Const, oneIdx);
                _builder.Emit(expr.Operator.Type == TokenType.PlusPlus ? OpCode.Add : OpCode.Subtract);
                // [obj, newVal]
                _builder.Emit(OpCode.SetField, nameIdx);          // [newVal]
            }
            else
            {
                // Postfix: perform mutation first, then derive oldVal from newVal
                CompileExpr(dot.Object);                                // [..., obj]
                _builder.Emit(OpCode.Dup);                              // [..., obj, obj]
                _builder.Emit(OpCode.GetField, nameIdx);                // [..., obj, oldVal]
                _builder.Emit(OpCode.CheckNumeric);
                ushort oneIdx = _builder.AddConstant(1L);
                _builder.Emit(OpCode.Const, oneIdx);                    // [..., obj, oldVal, 1]
                _builder.Emit(expr.Operator.Type == TokenType.PlusPlus ? OpCode.Add : OpCode.Subtract);
                // [..., obj, newVal]
                _builder.Emit(OpCode.SetField, nameIdx);                // [..., newVal]
                // Derive oldVal: reverse the operation
                _builder.Emit(OpCode.Const, oneIdx);                    // [..., newVal, 1]
                _builder.Emit(expr.Operator.Type == TokenType.PlusPlus ? OpCode.Subtract : OpCode.Add);
                // [..., oldVal]
            }
            return null;
        }

        if (expr.Operand is IndexExpr idx)
        {
            ushort oneIdx = _builder.AddConstant(1L);

            if (expr.IsPrefix)
            {
                // Prefix: ++arr[i] — stack: [obj, index, oldVal] → SetIndex → [newVal]
                CompileExpr(idx.Object);                          // [obj]
                CompileExpr(idx.Index);                           // [obj, index]
                CompileExpr(idx.Object);                          // [obj, index, obj2]
                CompileExpr(idx.Index);                           // [obj, index, obj2, index2]
                _builder.Emit(OpCode.GetIndex);                   // [obj, index, oldVal]
                _builder.Emit(OpCode.CheckNumeric);
                _builder.Emit(OpCode.Const, oneIdx);
                _builder.Emit(expr.Operator.Type == TokenType.PlusPlus ? OpCode.Add : OpCode.Subtract);
                // [obj, index, newVal]
                _builder.Emit(OpCode.SetIndex);                   // [newVal]  (SetIndex pops 3, pushes 1)
            }
            else
            {
                // Postfix: arr[i]++ — read old value first, then mutate
                CompileExpr(idx.Object);                          // [obj]
                CompileExpr(idx.Index);                           // [obj, index]
                _builder.Emit(OpCode.GetIndex);                   // [oldVal] — this is the result

                // Now perform the mutation by re-evaluating
                CompileExpr(idx.Object);                          // [oldVal, obj]
                CompileExpr(idx.Index);                           // [oldVal, obj, index]
                CompileExpr(idx.Object);                          // [oldVal, obj, index, obj2]
                CompileExpr(idx.Index);                           // [oldVal, obj, index, obj2, index2]
                _builder.Emit(OpCode.GetIndex);                   // [oldVal, obj, index, currVal]
                _builder.Emit(OpCode.CheckNumeric);
                _builder.Emit(OpCode.Const, oneIdx);
                _builder.Emit(expr.Operator.Type == TokenType.PlusPlus ? OpCode.Add : OpCode.Subtract);
                // [oldVal, obj, index, newVal]
                _builder.Emit(OpCode.SetIndex);                   // [oldVal, newVal]  (SetIndex pops 3, pushes 1)
                _builder.Emit(OpCode.Pop);                        // [oldVal] — discard newVal, keep result
            }
            return null;
        }

        throw new CompileError("Invalid update expression operand.", expr.Span);
    }

    /// <inheritdoc />
    public object? VisitLambdaExpr(LambdaExpr expr)
    {
        _builder.AddSourceMapping(expr.Span);

        var fnCompiler = new Compiler(this, null);
        fnCompiler._builder.Arity = expr.Parameters.Count;
        fnCompiler._builder.IsAsync = expr.IsAsync;
        fnCompiler._builder.HasRestParam = expr.HasRestParam;

        int paramCount = expr.HasRestParam ? expr.Parameters.Count - 1 : expr.Parameters.Count;
        int minArity = paramCount;
        for (int i = paramCount - 1; i >= 0; i--)
        {
            if (i < expr.DefaultValues.Count && expr.DefaultValues[i] != null)
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
        foreach (Token param in expr.Parameters)
        {
            int slot = fnCompiler._scope.DeclareLocal(param.Lexeme, isConst: false);
            fnCompiler._scope.MarkInitialized(slot);
        }

        // Emit default parameter prologue
        fnCompiler.EmitDefaultPrologue(expr.Parameters, expr.DefaultValues, expr.HasRestParam);

        if (expr.ExpressionBody != null)
        {
            fnCompiler.CompileExpr(expr.ExpressionBody);
            fnCompiler._builder.Emit(OpCode.Return);
        }
        else if (expr.BlockBody != null)
        {
            foreach (Stmt s in expr.BlockBody.Statements)
            {
                fnCompiler.CompileStmt(s);
            }

            fnCompiler._builder.Emit(OpCode.Null);
            fnCompiler._builder.Emit(OpCode.Return);
        }

        fnCompiler._builder.LocalCount = fnCompiler._scope.PeakLocalCount;
        fnCompiler._builder.LocalNames = fnCompiler._scope.GetPeakLocalNames();
        fnCompiler._builder.LocalIsConst = fnCompiler._scope.GetPeakLocalIsConst();
        fnCompiler._builder.UpvalueNames = fnCompiler._upvalueNames.Count > 0 ? fnCompiler._upvalueNames.ToArray() : null;
        Chunk lambdaChunk = fnCompiler._builder.Build();

        ushort chunkIdx = _builder.AddConstant(lambdaChunk);
        _builder.Emit(OpCode.Closure, chunkIdx);

        foreach (UpvalueDescriptor uv in lambdaChunk.Upvalues)
        {
            _builder.EmitByte(uv.IsLocal ? (byte)1 : (byte)0);
            _builder.EmitByte(uv.Index);
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
    public object? VisitRangeExpr(RangeExpr expr)
    {
        CompileExpr(expr.Start);
        CompileExpr(expr.End);
        if (expr.Step != null)
        {
            CompileExpr(expr.Step);
        }
        else
        {
            _builder.Emit(OpCode.Null);  // null step = VM uses default (1)
        }

        _builder.Emit(OpCode.Range);
        return null;
    }

    /// <inheritdoc />
    public object? VisitDictLiteralExpr(DictLiteralExpr expr)
    {
        foreach ((Token? key, Expr value) in expr.Entries)
        {
            if (key != null)
            {
                ushort keyIdx = _builder.AddConstant(key.Lexeme);
                _builder.Emit(OpCode.Const, keyIdx);
            }
            else
            {
                // Spread entry: push a null marker as the "key" so count*2 stays consistent
                _builder.Emit(OpCode.Null);
            }
            CompileExpr(value);
        }
        _builder.Emit(OpCode.Dict, (ushort)expr.Entries.Count);
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

    /// <inheritdoc />
    public object? VisitRetryExpr(RetryExpr expr)
    {
        _builder.AddSourceMapping(expr.Span);

        // --- Compile max attempts ---
        CompileExpr(expr.MaxAttempts); // [maxAttempts] on stack

        // --- Compile options ---
        int optionCount = 0;
        if (expr.NamedOptions != null)
        {
            foreach (var (name, value) in expr.NamedOptions)
            {
                ushort nameIdx = _builder.AddConstant(name.Lexeme);
                _builder.Emit(OpCode.Const, nameIdx);
                CompileExpr(value);
                optionCount++;
            }
        }
        else if (expr.OptionsExpr != null)
        {
            CompileExpr(expr.OptionsExpr);
            optionCount = -1; // Special: entire options struct on stack
        }

        // --- Compile body as closure ---
        var bodyCompiler = new Compiler(this, "<retry_body>");
        bodyCompiler._scope.BeginScope();
        bodyCompiler._builder.Arity = 1;
        bodyCompiler._builder.MinArity = 1;
        int bodyAttemptSlot = bodyCompiler._scope.DeclareLocal("attempt", isConst: true);
        bodyCompiler._scope.MarkInitialized(bodyAttemptSlot);
        List<Stmt> bodyStmts = expr.Body.Statements;
        for (int i = 0; i < bodyStmts.Count; i++)
        {
            if (i == bodyStmts.Count - 1 && bodyStmts[i] is ExprStmt lastExpr)
            {
                bodyCompiler.CompileExpr(lastExpr.Expression);
            }
            else
            {
                bodyCompiler.CompileStmt(bodyStmts[i]);
            }
        }

        if (bodyStmts.Count == 0 || bodyStmts[^1] is not ExprStmt)
        {
            bodyCompiler._builder.Emit(OpCode.Null);
        }

        bodyCompiler._builder.Emit(OpCode.Return);
        bodyCompiler._builder.LocalCount = bodyCompiler._scope.PeakLocalCount;
        bodyCompiler._builder.LocalNames = bodyCompiler._scope.GetPeakLocalNames();
        bodyCompiler._builder.LocalIsConst = bodyCompiler._scope.GetPeakLocalIsConst();
        bodyCompiler._builder.UpvalueNames = bodyCompiler._upvalueNames.Count > 0 ? bodyCompiler._upvalueNames.ToArray() : null;
        Chunk bodyChunk = bodyCompiler._builder.Build();
        ushort bodyIdx = _builder.AddConstant(bodyChunk);
        _builder.Emit(OpCode.Closure, bodyIdx);
        foreach (UpvalueDescriptor uv in bodyChunk.Upvalues)
        {
            _builder.EmitByte(uv.IsLocal ? (byte)1 : (byte)0);
            _builder.EmitByte(uv.Index);
        }

        // --- Compile until clause (if present) ---
        bool hasUntil = expr.UntilClause != null;
        if (hasUntil)
        {
            CompileExpr(expr.UntilClause!);
        }

        // --- Compile onRetry clause (if present) ---
        bool hasOnRetry = expr.OnRetryClause != null;
        bool onRetryIsReference = false;
        if (hasOnRetry)
        {
            OnRetryNode onRetry = expr.OnRetryClause!;
            onRetryIsReference = onRetry.IsReference;
            if (onRetry.IsReference)
            {
                CompileExpr(onRetry.Reference!);
            }
            else
            {
                // Inline block: compile as a closure with (attempt, error) parameters
                var onRetryCompiler = new Compiler(this, "<on_retry>");
                onRetryCompiler._builder.Arity = 2;
                onRetryCompiler._builder.MinArity = 2;
                onRetryCompiler._scope.BeginScope();

                string attemptName = onRetry.ParamAttempt?.Lexeme ?? "<attempt>";
                int attemptSlot = onRetryCompiler._scope.DeclareLocal(attemptName, isConst: false);
                onRetryCompiler._scope.MarkInitialized(attemptSlot);

                string errorName = onRetry.ParamError?.Lexeme ?? "<error>";
                int errorSlot = onRetryCompiler._scope.DeclareLocal(errorName, isConst: false);
                onRetryCompiler._scope.MarkInitialized(errorSlot);

                foreach (Stmt s in onRetry.Body!.Statements)
                {
                    onRetryCompiler.CompileStmt(s);
                }

                onRetryCompiler._builder.Emit(OpCode.Null);
                onRetryCompiler._builder.Emit(OpCode.Return);
                onRetryCompiler._builder.LocalCount = onRetryCompiler._scope.PeakLocalCount;
                onRetryCompiler._builder.LocalNames = onRetryCompiler._scope.GetPeakLocalNames();
                onRetryCompiler._builder.LocalIsConst = onRetryCompiler._scope.GetPeakLocalIsConst();
                onRetryCompiler._builder.UpvalueNames = onRetryCompiler._upvalueNames.Count > 0 ? onRetryCompiler._upvalueNames.ToArray() : null;
                Chunk onRetryChunk = onRetryCompiler._builder.Build();
                ushort onRetryIdx = _builder.AddConstant(onRetryChunk);
                _builder.Emit(OpCode.Closure, onRetryIdx);
                foreach (UpvalueDescriptor uv in onRetryChunk.Upvalues)
                {
                    _builder.EmitByte(uv.IsLocal ? (byte)1 : (byte)0);
                    _builder.EmitByte(uv.Index);
                }
            }
        }

        // --- Emit OP_RETRY ---
        var metadata = new RetryMetadata(optionCount, hasUntil, hasOnRetry, onRetryIsReference);
        ushort metaIdx = _builder.AddConstant(metadata);
        _builder.Emit(OpCode.Retry, metaIdx);

        return null;
    }
}
