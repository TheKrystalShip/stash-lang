using System;
using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;
using Stash.Parsing.AST;
using Stash.Runtime.Types;

namespace Stash.Bytecode;

/// <summary>
/// Core fields, constructor, nested types, and static compile entry points.
/// </summary>
public sealed partial class Compiler : IExprVisitor<object?>, IStmtVisitor<object?>
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
}
