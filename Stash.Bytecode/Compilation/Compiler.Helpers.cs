using System;
using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;
using Stash.Parsing.AST;
using Stash.Runtime;

namespace Stash.Bytecode;

partial class Compiler
{
    // ==================================================================
    // Variable Emission
    // ==================================================================

    /// <summary>
    /// Emit code to load a variable into the destination register, or store from a source register.
    /// </summary>
    /// <param name="name">Variable name.</param>
    /// <param name="resolvedDistance">-1=global, 0=local/global fallback, >0=upvalue.</param>
    /// <param name="resolvedSlot">Unused (register allocation is done by CompilerScope).</param>
    /// <param name="isLoad">True to load into dest, false to store from source.</param>
    /// <param name="reg">For load: destination register. For store: source register.</param>
    private void EmitVariable(string name, int resolvedDistance, int resolvedSlot, bool isLoad, byte reg)
    {
        if (resolvedDistance == -1)
        {
            // ---- GLOBAL ----
            if (isLoad && _globalSlots.TryGetConstValue(name, out object? constValue))
            {
                EmitFoldedConstant(constValue, reg);
            }
            else
            {
                ushort slot = _globalSlots.GetOrAllocate(name);
                if (isLoad)
                    _builder.EmitABx(OpCode.GetGlobal, reg, slot);
                else
                    _builder.EmitABx(OpCode.SetGlobal, reg, slot);
            }
        }
        else
        {
            // ---- LOCAL or UPVALUE ----
            int localReg = _scope.ResolveLocal(name);
            if (localReg >= 0)
            {
                if (!isLoad && _scope.IsLocalConst(localReg))
                {
                    // Const reassignment — emit throw
                    byte errReg = _scope.AllocTemp();
                    ushort msgIdx = _builder.AddConstant("Assignment to constant variable.");
                    _builder.EmitABx(OpCode.LoadK, errReg, msgIdx);
                    _builder.EmitA(OpCode.Throw, errReg);
                    _scope.FreeTemp(errReg);
                }
                else if (isLoad)
                {
                    if ((byte)localReg != reg)
                        _builder.EmitAB(OpCode.Move, reg, (byte)localReg);
                    // If localReg == reg, no instruction needed
                }
                else
                {
                    // Store: move source reg into the local's register
                    if ((byte)localReg != reg)
                        _builder.EmitAB(OpCode.Move, (byte)localReg, reg);
                }
            }
            else if (resolvedDistance > 0)
            {
                byte upvalueIdx = ResolveUpvalue(name, resolvedDistance);
                if (isLoad)
                    _builder.EmitAB(OpCode.GetUpval, reg, upvalueIdx);
                else
                    _builder.EmitAB(OpCode.SetUpval, reg, upvalueIdx);
            }
            else
            {
                // Fallback to global
                ushort globalSlot = _globalSlots.GetOrAllocate(name);
                if (isLoad)
                    _builder.EmitABx(OpCode.GetGlobal, reg, globalSlot);
                else
                    _builder.EmitABx(OpCode.SetGlobal, reg, globalSlot);
            }
        }
    }

    // ==================================================================
    // OPT-1: Local Register Fast Path
    // ==================================================================

    /// <summary>
    /// If the expression is a simple local variable read, return its register directly.
    /// This avoids allocating a temp and emitting a Move for read-only operands.
    /// </summary>
    private bool TryGetLocalReg(Expr expr, out byte reg)
    {
        if (expr is IdentifierExpr id && id.ResolvedDistance >= 0)
        {
            int localReg = _scope.ResolveLocal(id.Name.Lexeme);
            if (localReg >= 0)
            {
                reg = (byte)localReg;
                return true;
            }
        }
        reg = 0;
        return false;
    }

    // ==================================================================
    // Upvalue Resolution
    // ==================================================================

    private byte ResolveUpvalue(string name, int distance)
    {
        if (_enclosing == null) return 0;

        int localSlot = _enclosing._scope.ResolveLocal(name);
        if (localSlot >= 0)
        {
            _enclosing._builder.MayHaveCapturedLocals = true;
            byte idx = _builder.AddUpvalue((byte)localSlot, isLocal: true);
            if (idx == (_upvalueNames ??= new()).Count)
                _upvalueNames.Add(name);
            return idx;
        }

        if (distance > 1 && _enclosing._enclosing != null)
        {
            byte enclosingUpvalue = _enclosing.ResolveUpvalue(name, distance - 1);
            byte idx = _builder.AddUpvalue(enclosingUpvalue, isLocal: false);
            if (idx == (_upvalueNames ??= new()).Count)
                _upvalueNames.Add(name);
            return idx;
        }

        return 0;
    }

    // ==================================================================
    // Function Compilation
    // ==================================================================

    /// <summary>
    /// Compile a function/lambda body into a child Chunk.
    /// Returns the Chunk. The caller emits Closure to load it.
    /// </summary>
    private Chunk CompileFunction(
        List<Token> parameters,
        Stmt body,
        string? name,
        bool isAsync,
        bool hasRestParam,
        List<Expr?>? defaultValues,
        bool constFirstParam = false)
    {
        var child = new Compiler(this, name, _globalSlots);

        // Declare parameters as locals (register 0..N-1)
        for (int i = 0; i < parameters.Count; i++)
            child._scope.DeclareLocal(parameters[i].Lexeme, isConst: constFirstParam && i == 0);

        int paramCount = parameters.Count;
        int minArity = paramCount;
        bool hasDefaultParams = false;

        // Compute MinArity (first param without a default)
        if (defaultValues != null)
        {
            int countForDefaults = hasRestParam ? paramCount - 1 : paramCount;
            for (int i = countForDefaults - 1; i >= 0; i--)
            {
                if (defaultValues.Count > i && defaultValues[i] != null)
                {
                    minArity = i;
                    hasDefaultParams = true;
                }
                else
                    break;
            }
        }

        // Emit default parameter prologue
        if (hasDefaultParams && defaultValues != null)
        {
            for (int i = 0; i < defaultValues.Count && i < paramCount; i++)
            {
                if (defaultValues[i] == null) continue;

                byte paramReg = (byte)i;
                // Check if param was not provided (sentinel check)
                byte tempSentinel = child._scope.AllocTemp();
                ushort sentinelIdx = child._builder.AddConstant(VirtualMachine.NotProvided);
                child._builder.EmitABx(OpCode.LoadK, tempSentinel, sentinelIdx);

                byte cmpReg = child._scope.AllocTemp();
                child._builder.EmitABC(OpCode.Ne, cmpReg, paramReg, tempSentinel);
                int skipDefault = child._builder.EmitJump(OpCode.JmpTrue, cmpReg);
                child._scope.FreeTemp(cmpReg);
                child._scope.FreeTemp(tempSentinel);

                // Evaluate default value into the param register
                child.CompileExprTo(defaultValues[i]!, paramReg);

                child._builder.PatchJump(skipDefault);
            }
        }

        // Compile body
        if (body is BlockStmt block)
        {
            foreach (Stmt stmt in block.Statements)
                child.CompileStmt(stmt);
        }
        else
        {
            child.CompileStmt(body);
        }

        // OPT-4: Only emit implicit return null if the body doesn't unconditionally return/throw
        bool alwaysReturns = false;
        if (body is BlockStmt blk)
            alwaysReturns = BodyAlwaysReturns(blk.Statements);
        else
            alwaysReturns = StmtAlwaysReturns(body);

        if (!alwaysReturns)
        {
            byte retReg = child._scope.AllocTemp();
            child._builder.EmitA(OpCode.LoadNull, retReg);
            child._builder.EmitABC(OpCode.Return, retReg, 1, 0);
            child._scope.FreeTemp(retReg);
        }

        child._builder.Arity = paramCount;
        child._builder.MinArity = minArity;
        child._builder.MaxRegs = child._scope.MaxRegs;
        child._builder.IsAsync = isAsync;
        child._builder.HasRestParam = hasRestParam;
        child._builder.LocalNames = child._scope.GetLocalNames();
        child._builder.LocalIsConst = child._scope.GetLocalIsConst();
        child._builder.UpvalueNames = child._upvalueNames?.ToArray();

        return child._builder.Build();
    }

    // ==================================================================
    // Scope Cleanup
    // ==================================================================

    /// <summary>
    /// End a scope, emitting CloseUpvalue for captured locals.
    /// </summary>
    private void EndScope()
    {
        int freed = _scope.EndScope();
        // If any locals might have been captured, close their upvalues
        if (freed > 0 && _builder.MayHaveCapturedLocals)
        {
            // Close upvalues for registers that are about to be freed
            byte firstFreedReg = _scope.NextFreeReg;
            _builder.EmitA(OpCode.CloseUpval, firstFreedReg);
        }
    }

    /// <summary>
    /// Patch all break jumps in the current loop to the current position.
    /// </summary>
    private void PatchBreakJumps()
    {
        if (_loops == null || _loops.Count == 0) return;
        LoopContext loop = _loops.Pop();
        foreach (int jump in loop.BreakJumps)
            _builder.PatchJump(jump);
    }

    // ==================================================================
    // OPT-10: Numeric Expression Detection
    // ==================================================================

    /// <summary>
    /// Check if an expression is guaranteed to produce a numeric value.
    /// Used for CheckNumeric elimination. Conservative: false negatives are safe.
    /// </summary>
    private static bool IsNumericExpr(Expr expr)
    {
        if (expr is LiteralExpr lit)
            return lit.Value is long or double or int;
        if (expr is UnaryExpr un && un.Operator.Type == TokenType.Minus)
            return IsNumericExpr(un.Right);
        if (expr is GroupingExpr g)
            return IsNumericExpr(g.Expression);
        return false;
    }

    // ==================================================================
    // OPT-4: Dead Epilogue Elimination
    // ==================================================================

    /// <summary>
    /// Check if a statement list unconditionally returns or throws.
    /// Used to suppress the implicit return-null epilogue when it's unreachable.
    /// Conservative: false negatives are safe (just emits dead code).
    /// </summary>
    private static bool BodyAlwaysReturns(List<Stmt> statements)
    {
        if (statements.Count == 0) return false;
        return StmtAlwaysReturns(statements[^1]);
    }

    private static bool StmtAlwaysReturns(Stmt stmt) => stmt switch
    {
        ReturnStmt => true,
        ThrowStmt => true,
        BlockStmt block => block.Statements.Count > 0 && StmtAlwaysReturns(block.Statements[^1]),
        IfStmt ifs => ifs.ElseBranch != null
                      && StmtAlwaysReturns(ifs.ThenBranch)
                      && StmtAlwaysReturns(ifs.ElseBranch),
        TryCatchStmt tc => tc.CatchBody != null
                           && StmtAlwaysReturns(tc.TryBody)
                           && StmtAlwaysReturns(tc.CatchBody),
        _ => false,
    };

}
