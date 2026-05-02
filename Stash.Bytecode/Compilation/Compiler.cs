using System;
using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;
using Stash.Parsing.AST;
using Stash.Runtime;

namespace Stash.Bytecode;

/// <summary>
/// Compiles AST nodes into register-based bytecode.
/// Each function/lambda gets its own Compiler instance with a private CompilerScope.
/// </summary>
public sealed partial class Compiler : IExprVisitor<object?>, IStmtVisitor<object?>
{
    private readonly ChunkBuilder _builder;
    private readonly CompilerScope _scope;
    private readonly Compiler? _enclosing;
    private readonly GlobalSlotAllocator _globalSlots;

    /// <summary>Target register for the current expression being compiled.</summary>
    private byte _destReg;

    /// <summary>When true, the current expression's result is unused (expression statement context).</summary>
    private bool _voidContext;

    /// <summary>Loop context for break/continue patching.</summary>
    private sealed class LoopContext
    {
        public int LoopStart;
        public int ContinueTarget = -1;
        public readonly List<int> BreakJumps = new();
        public readonly List<int> ContinueJumps = new();
        public int ScopeDepth;
    }

    private Stack<LoopContext>? _loops;
    private List<FinallyInfo>? _activeFinally;
    private List<string>? _upvalueNames;
    private byte _activeCatchErrReg;  // register of the nearest enclosing catch handler's error, for bare rethrow

    /// <summary>Info about an active try-finally block for break/continue/return handling.</summary>
    private sealed class FinallyInfo
    {
        public int FinallyStart;
        public int ScopeDepth;
        public BlockStmt? Body;
    }

    private Compiler(Compiler? enclosing, string? functionName, GlobalSlotAllocator globalSlots)
    {
        _enclosing = enclosing;
        _globalSlots = globalSlots;
        _builder = new ChunkBuilder();
        _scope = new CompilerScope();
        _builder.Name = functionName;
        _builder.SetGlobalSlots(globalSlots);

        // Propagate DCE flag from parent so child chunks honour the same setting
        if (enclosing != null)
            _builder.EnableDce = enclosing._builder.EnableDce;
    }

    // ==================================================================
    // Public Entry Points
    // ==================================================================

    /// <summary>Compile a list of statements (script body) into a Chunk.</summary>
    public static Chunk Compile(List<Stmt> statements, bool enableDce = true)
    {
        var globalSlots = new GlobalSlotAllocator();
        return Compile(statements, globalSlots, enableDce);
    }

    /// <summary>
    /// Compile a list of statements using a provided (potentially persistent) global slot allocator.
    /// Used by the REPL to share slot assignments across successive inputs, ensuring that lambdas
    /// compiled in an earlier REPL chunk read the correct global slots when invoked later.
    /// </summary>
    public static Chunk Compile(List<Stmt> statements, GlobalSlotAllocator globalSlots, bool enableDce = true)
    {
        var compiler = new Compiler(null, null, globalSlots);
        compiler._builder.EnableDce = enableDce;

        foreach (Stmt stmt in statements)
            compiler.CompileStmt(stmt);

        // Implicit return null at end of script
        byte reg = compiler._scope.AllocTemp();
        compiler._builder.EmitA(OpCode.LoadNull, reg);
        compiler._builder.EmitABC(OpCode.Return, reg, 1, 0);
        compiler._scope.FreeTemp(reg);

        compiler._builder.MaxRegs = compiler._scope.MaxRegs;
        compiler._builder.LocalNames = compiler._scope.GetLocalNames();
        compiler._builder.LocalIsConst = compiler._scope.GetLocalIsConst();
        return compiler._builder.Build();
    }

    /// <summary>Compile a single expression into a Chunk (for eval/REPL).</summary>
    public static Chunk CompileExpression(Expr expression)
    {
        var globalSlots = new GlobalSlotAllocator();
        var compiler = new Compiler(null, null, globalSlots);

        byte result = compiler.CompileExpr(expression);
        compiler._builder.EmitABC(OpCode.Return, result, 1, 0);
        compiler._scope.FreeTemp(result);

        compiler._builder.MaxRegs = compiler._scope.MaxRegs;
        compiler._builder.LocalNames = compiler._scope.GetLocalNames();
        compiler._builder.LocalIsConst = compiler._scope.GetLocalIsConst();
        return compiler._builder.Build();
    }

    // ==================================================================
    // Core Compilation Methods
    // ==================================================================

    /// <summary>Compile a statement.</summary>
    private void CompileStmt(Stmt stmt) => stmt.Accept(this);

    /// <summary>
    /// Compile an expression, allocating a temporary register for the result.
    /// Returns the register containing the result.
    /// The caller is responsible for freeing the temp when done.
    /// </summary>
    private byte CompileExpr(Expr expr)
    {
        byte dest = _scope.AllocTemp();
        CompileExprTo(expr, dest);
        return dest;
    }

    /// <summary>
    /// Compile an expression into a specific destination register.
    /// Does not allocate or free any registers.
    /// </summary>
    private void CompileExprTo(Expr expr, byte dest)
    {
        _destReg = dest;
        expr.Accept(this);
    }

    /// <summary>Emit a constant load for a folded compile-time value.</summary>
    private void EmitFoldedConstant(object? value, byte dest)
    {
        StashValue sv = StashValue.FromObject(value);
        if (sv.IsNull)
        {
            _builder.EmitA(OpCode.LoadNull, dest);
        }
        else if (sv.IsBool)
        {
            _builder.EmitABC(OpCode.LoadBool, dest, sv.AsBool ? (byte)1 : (byte)0, 0);
        }
        else
        {
            ushort idx = _builder.AddConstant(sv);
            _builder.EmitABx(OpCode.LoadK, dest, idx);
        }
    }

    /// <summary>Consumes and returns the current void context flag, resetting it to false.</summary>
    private bool ConsumeVoidContext()
    {
        bool isVoid = _voidContext;
        _voidContext = false;
        return isVoid;
    }
}
