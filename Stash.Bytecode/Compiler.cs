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
            compiler.CompileStmt(stmt);
        // Implicit return null at end of script
        compiler._builder.Emit(OpCode.Null);
        compiler._builder.Emit(OpCode.Return);
        compiler._builder.LocalCount = compiler._scope.LocalCount;
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
                _builder.Emit(isLoad ? OpCode.LoadLocal : OpCode.StoreLocal, (byte)slot);
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
            return 0; // Shouldn't happen for a properly resolved upvalue

        int localSlot = _enclosing._scope.ResolveLocal(name);
        if (localSlot >= 0)
        {
            // Captured directly from the enclosing function's locals
            return _builder.AddUpvalue((byte)localSlot, isLocal: true);
        }

        if (distance > 1 && _enclosing._enclosing != null)
        {
            // Transitively captured through the enclosing function's own upvalue chain
            byte enclosingUpvalue = _enclosing.ResolveUpvalue(name, distance - 1);
            return _builder.AddUpvalue(enclosingUpvalue, isLocal: false);
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
            if (local.Depth <= targetDepth) break;
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
            _builder.Emit(OpCode.Pop);
    }

    /// <summary>
    /// Pops the innermost <see cref="LoopContext"/> and patches all pending break jumps
    /// to the current bytecode offset.
    /// </summary>
    private void PatchBreakJumps()
    {
        LoopContext loop = _loops.Pop();
        foreach (int jump in loop.BreakJumps)
            _builder.PatchJump(jump);
    }

    /// <summary>
    /// Patches all pending continue jumps in <paramref name="ctx"/> to the current
    /// bytecode offset and clears the list.
    /// </summary>
    private static void PatchContinueJumps(LoopContext ctx, ChunkBuilder builder)
    {
        foreach (int jump in ctx.ContinueJumps)
            builder.PatchJump(jump);
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
        if (!hasAnyDefault) return;

        // Add the NotProvided sentinel to the constant pool once
        ushort notProvidedIdx = _builder.AddConstant(VirtualMachine.NotProvided);

        for (int i = 0; i < count && i < defaultValues.Count; i++)
        {
            Expr? defaultExpr = defaultValues[i];
            if (defaultExpr == null) continue;

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
        bool hasRestParam)
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
            if (i < defaultValues.Count && defaultValues[i] != null) minArity--;
            else break;
        }
        fnCompiler._builder.MinArity = minArity;

        fnCompiler._scope.BeginScope();

        foreach (Token param in parameters)
        {
            int paramSlot = fnCompiler._scope.DeclareLocal(param.Lexeme, isConst: false);
            fnCompiler._scope.MarkInitialized(paramSlot);
        }

        // Emit default parameter prologue: for each param with a default,
        // check if the arg was provided (not NotProvided sentinel) and
        // evaluate the default expression if missing.
        fnCompiler.EmitDefaultPrologue(parameters, defaultValues, hasRestParam);

        foreach (Stmt s in body.Statements)
            fnCompiler.CompileStmt(s);

        // Implicit return null
        fnCompiler._builder.Emit(OpCode.Null);
        fnCompiler._builder.Emit(OpCode.Return);

        fnCompiler._builder.LocalCount = fnCompiler._scope.LocalCount;
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
            CompileExpr(stmt.Initializer);
        else
            _builder.Emit(OpCode.Null);

        _scope.MarkInitialized(slot);
        // The initializer value on the stack IS the local — no separate store needed.
        return null;
    }

    /// <inheritdoc />
    public object? VisitConstDeclStmt(ConstDeclStmt stmt)
    {
        _builder.AddSourceMapping(stmt.Span);
        int slot = _scope.DeclareLocal(stmt.Name.Lexeme, isConst: true);
        CompileExpr(stmt.Initializer);
        _scope.MarkInitialized(slot);
        return null;
    }

    /// <inheritdoc />
    public object? VisitBlockStmt(BlockStmt stmt)
    {
        _scope.BeginScope();
        foreach (Stmt s in stmt.Statements)
            CompileStmt(s);
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
            CompileStmt(stmt.Initializer);

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
            _builder.PatchJump(exitJump);

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
            throw new CompileError("'break' outside of loop.", stmt.Span);

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
            throw new CompileError("'continue' outside of loop.", stmt.Span);

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
            CompileExpr(stmt.Value);
        else
            _builder.Emit(OpCode.Null);
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

        // OP_CLOSURE places the closure on the stack at the correct slot — no store needed
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
            CompileFunction(
                stmt.Methods[i].Name.Lexeme,
                stmt.Methods[i].Parameters,
                stmt.Methods[i].DefaultValues,
                stmt.Methods[i].Body,
                stmt.Methods[i].IsAsync,
                stmt.Methods[i].HasRestParam);
        }

        string[] fields = new string[stmt.Fields.Count];
        for (int i = 0; i < stmt.Fields.Count; i++)
            fields[i] = stmt.Fields[i].Lexeme;

        string[] interfaceNames = new string[stmt.Interfaces.Count];
        for (int i = 0; i < stmt.Interfaces.Count; i++)
            interfaceNames[i] = stmt.Interfaces[i].Lexeme;

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
            members[i] = stmt.Members[i].Lexeme;

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
                paramNames.Add(p.Lexeme);

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

        string typeName = stmt.TypeName.Lexeme;
        bool isBuiltIn = typeName is "string" or "array" or "dict" or "int" or "float";

        var methodNames = new string[stmt.Methods.Count];
        for (int i = 0; i < stmt.Methods.Count; i++)
        {
            methodNames[i] = stmt.Methods[i].Name.Lexeme;
            CompileFunction(
                stmt.Methods[i].Name.Lexeme,
                stmt.Methods[i].Parameters,
                stmt.Methods[i].DefaultValues,
                stmt.Methods[i].Body,
                stmt.Methods[i].IsAsync,
                stmt.Methods[i].HasRestParam);
        }

        var metadata = new ExtendMetadata(typeName, methodNames, isBuiltIn);
        ushort metaIdx = _builder.AddConstant(metadata);
        _builder.Emit(OpCode.Extend, metaIdx);

        return null;
    }

    /// <inheritdoc />
    public object? VisitImportStmt(ImportStmt stmt) =>
        throw new NotSupportedException("Import compilation deferred to Phase 6.");

    /// <inheritdoc />
    public object? VisitImportAsStmt(ImportAsStmt stmt) =>
        throw new NotSupportedException("Import-as compilation deferred to Phase 6.");

    /// <inheritdoc />
    public object? VisitDestructureStmt(DestructureStmt stmt) =>
        throw new NotSupportedException("Destructure compilation deferred to Phase 6.");

    /// <inheritdoc />
    public object? VisitElevateStmt(ElevateStmt stmt) =>
        throw new NotSupportedException("Elevate compilation deferred to Phase 6.");

    /// <inheritdoc />
    public object? VisitTryCatchStmt(TryCatchStmt stmt) =>
        throw new NotSupportedException("Try/catch compilation deferred to Phase 6.");

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
            TokenType.In             => throw new NotSupportedException(
                                            "'in' operator compilation deferred to Phase 6."),
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
            foreach (Expr arg in expr.Arguments)
                CompileExpr(arg);
            _builder.Emit(OpCode.Call, (byte)expr.Arguments.Count);
            int endJump = _builder.EmitJump(OpCode.Jump);

            // Callee was null — pop callee, push null as result
            _builder.PatchJump(nullJump);
            _builder.Emit(OpCode.Pop); // pop the callee (null)
            _builder.Emit(OpCode.Null); // push null as result

            _builder.PatchJump(endJump);
            return null;
        }

        // Non-optional: compile args, emit call
        int spreadCount = 0;
        foreach (Expr arg in expr.Arguments)
        {
            if (arg is SpreadExpr spread)
            {
                // Compile the spread expression — defer full spread support to Phase 5
                CompileExpr(spread.Expression);
                spreadCount++;
            }
            else
            {
                CompileExpr(arg);
            }
        }

        _builder.Emit(OpCode.Call, (byte)expr.Arguments.Count);
        return null;
    }

    /// <inheritdoc />
    public object? VisitArrayExpr(ArrayExpr expr)
    {
        foreach (Expr element in expr.Elements)
            CompileExpr(element);
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
            CompileExpr(part);
        _builder.Emit(OpCode.Interpolate, (ushort)expr.Parts.Count);
        return null;
    }

    /// <inheritdoc />
    public object? VisitCommandExpr(CommandExpr expr)
    {
        _builder.AddSourceMapping(expr.Span);
        foreach (Expr part in expr.Parts)
            CompileExpr(part);
        // u16 operand = number of parts on the stack
        // Passthrough/strict flags are Phase 3 concerns
        _builder.Emit(OpCode.Command, (ushort)expr.Parts.Count);
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
        _builder.Emit(OpCode.Pop);   // discard the caught error
        _builder.Emit(OpCode.Null);  // expression result is null on failure
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

        foreach (SwitchArm arm in expr.Arms)
        {
            if (arm.IsDiscard)
            {
                // Default arm — pop subject, evaluate and leave result
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

        foreach (int endJump in endJumps)
            _builder.PatchJump(endJump);

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

            if (!expr.IsPrefix)
                _builder.Emit(OpCode.Dup);  // postfix: preserve old value as expression result

            ushort oneIdx = _builder.AddConstant(1L);
            _builder.Emit(OpCode.Const, oneIdx);
            _builder.Emit(expr.Operator.Type == TokenType.PlusPlus ? OpCode.Add : OpCode.Subtract);

            if (expr.IsPrefix)
                _builder.Emit(OpCode.Dup);  // prefix: preserve new value as expression result

            EmitVariable(id.Name.Lexeme, id.ResolvedDistance, id.ResolvedSlot, isLoad: false);
            return null;
        }

        if (expr.Operand is DotExpr)
        {
            // Requires a Swap/Rot opcode to position the object reference correctly for SetField.
            // Without stack manipulation primitives, the object gets buried under intermediate values.
            throw new CompileError(
                "Update expressions on field access (e.g. obj.field++) deferred to Phase 3.", expr.Span);
        }

        if (expr.Operand is IndexExpr)
        {
            // Requires stack manipulation opcodes to retain both the object and index for SetIndex.
            throw new CompileError(
                "Update expressions on index access (e.g. arr[i]++) deferred to Phase 3.", expr.Span);
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
            if (i < expr.DefaultValues.Count && expr.DefaultValues[i] != null) minArity--;
            else break;
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
                fnCompiler.CompileStmt(s);
            fnCompiler._builder.Emit(OpCode.Null);
            fnCompiler._builder.Emit(OpCode.Return);
        }

        fnCompiler._builder.LocalCount = fnCompiler._scope.LocalCount;
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
        if (expr.Append) flags |= 0x04;
        _builder.Emit(OpCode.Redirect, flags);
        return null;
    }

    /// <inheritdoc />
    public object? VisitRangeExpr(RangeExpr expr)
    {
        CompileExpr(expr.Start);
        CompileExpr(expr.End);
        if (expr.Step != null)
            CompileExpr(expr.Step);
        else
            _builder.Emit(OpCode.Null);  // null step = VM uses default (1)
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
            // When key is null the entry is a SpreadExpr — CompileExpr handles the spread
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
            ushort typeIdx = _builder.AddConstant(expr.TypeName.Lexeme);
            _builder.Emit(OpCode.Is, typeIdx);
        }
        else if (expr.TypeExpr != null)
        {
            // Dynamic type check — Phase 3 will implement this properly
            CompileExpr(expr.TypeExpr);
            _builder.Emit(OpCode.Is, (ushort)0);
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
    public object? VisitRetryExpr(RetryExpr expr) =>
        throw new NotSupportedException("Retry expression compilation deferred to Phase 6.");
}
