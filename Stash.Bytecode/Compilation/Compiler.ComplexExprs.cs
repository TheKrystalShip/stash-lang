using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;
using Stash.Parsing.AST;
using Stash.Runtime;

namespace Stash.Bytecode;

/// <summary>
/// Complex expression visitors: update, lambda, and retry.
/// </summary>
public sealed partial class Compiler
{
    /// <inheritdoc />
    public object? VisitUpdateExpr(UpdateExpr expr)
    {
        byte dest = _destReg;
        _builder.AddSourceMapping(expr.Span);

        int sign = expr.Operator.Type == TokenType.PlusPlus ? 1 : -1;

        // ── Identifier operand: x++ / ++x ──────────────────────────────
        if (expr.Operand is IdentifierExpr id)
        {
            if (expr.IsPrefix)
            {
                // OPT-4: For local variables, operate directly on the local's register
                int localReg = (id.ResolvedDistance >= 0) ? _scope.ResolveLocal(id.Name.Lexeme) : -1;
                if (localReg >= 0 && !_scope.IsLocalConst(localReg))
                {
                    if (!_scope.IsKnownNumeric(localReg))
                        _builder.EmitA(OpCode.CheckNumeric, (byte)localReg);
                    _builder.EmitAsBx(OpCode.AddI, (byte)localReg, sign);
                    _scope.MarkNumeric(localReg);
                    if ((byte)localReg != dest)
                        _builder.EmitAB(OpCode.Move, dest, (byte)localReg);
                }
                else
                {
                    // Fallback for upvalues, globals, consts
                    EmitVariable(id.Name.Lexeme, id.ResolvedDistance, id.ResolvedSlot, isLoad: true, dest);
                    _builder.EmitA(OpCode.CheckNumeric, dest);
                    _builder.EmitAsBx(OpCode.AddI, dest, sign);
                    EmitVariable(id.Name.Lexeme, id.ResolvedDistance, id.ResolvedSlot, isLoad: false, dest);
                }
            }
            else
            {
                int localReg = (id.ResolvedDistance >= 0) ? _scope.ResolveLocal(id.Name.Lexeme) : -1;
                if (localReg >= 0 && !_scope.IsLocalConst(localReg) && (byte)localReg != dest)
                {
                    // OPT-4: Local postfix, dest != local: copy old value to dest, then increment in-place
                    if (!_scope.IsKnownNumeric(localReg))
                        _builder.EmitA(OpCode.CheckNumeric, (byte)localReg);
                    _builder.EmitAB(OpCode.Move, dest, (byte)localReg); // dest = old value
                    _builder.EmitAsBx(OpCode.AddI, (byte)localReg, sign); // local = new value
                    _scope.MarkNumeric(localReg);
                }
                else
                {
                    // Fallback: upvalues, globals, consts, or dest==local (postfix aliasing)
                    EmitVariable(id.Name.Lexeme, id.ResolvedDistance, id.ResolvedSlot, isLoad: true, dest);
                    _builder.EmitA(OpCode.CheckNumeric, dest);
                    byte temp = _scope.AllocTemp();
                    _builder.EmitAB(OpCode.Move, temp, dest);    // temp = old value
                    _builder.EmitAsBx(OpCode.AddI, temp, sign);  // temp = new value
                    EmitVariable(id.Name.Lexeme, id.ResolvedDistance, id.ResolvedSlot, isLoad: false, temp);
                    _scope.FreeTemp(temp);
                    // dest still holds old value — postfix result
                }
            }
            return null;
        }

        // ── Dot access: obj.field++ / ++obj.field ───────────────────────
        if (expr.Operand is DotExpr dot)
        {
            ushort nameIdx = _builder.AddConstant(dot.Name.Lexeme);
            byte objReg = CompileExpr(dot.Object);

            if (expr.IsPrefix)
            {
                EmitGetField(dest, objReg, nameIdx);          // dest = obj.field (current)
                _builder.EmitA(OpCode.CheckNumeric, dest);
                _builder.EmitAsBx(OpCode.AddI, dest, sign);  // dest = new value
                EmitSetField(objReg, nameIdx, dest);          // obj.field = new value
                // dest = new value — prefix result
            }
            else
            {
                EmitGetField(dest, objReg, nameIdx);          // dest = old value
                _builder.EmitA(OpCode.CheckNumeric, dest);
                byte temp = _scope.AllocTemp();
                _builder.EmitAB(OpCode.Move, temp, dest);    // temp = old value
                _builder.EmitAsBx(OpCode.AddI, temp, sign);  // temp = new value
                EmitSetField(objReg, nameIdx, temp);          // obj.field = new value
                _scope.FreeTemp(temp);
                // dest = old value — postfix result
            }

            _scope.FreeTemp(objReg);
            return null;
        }

        // ── Index access: obj[idx]++ / ++obj[idx] ───────────────────────
        if (expr.Operand is IndexExpr idx)
        {
            byte objReg = CompileExpr(idx.Object);
            byte idxReg = CompileExpr(idx.Index);

            if (expr.IsPrefix)
            {
                _builder.EmitABC(OpCode.GetTable, dest, objReg, idxReg); // dest = obj[idx]
                _builder.EmitA(OpCode.CheckNumeric, dest);
                _builder.EmitAsBx(OpCode.AddI, dest, sign);               // dest = new value
                _builder.EmitABC(OpCode.SetTable, objReg, idxReg, dest);  // obj[idx] = new value
                // dest = new value — prefix result
            }
            else
            {
                _builder.EmitABC(OpCode.GetTable, dest, objReg, idxReg); // dest = old value
                _builder.EmitA(OpCode.CheckNumeric, dest);
                byte temp = _scope.AllocTemp();
                _builder.EmitAB(OpCode.Move, temp, dest);                 // temp = old value
                _builder.EmitAsBx(OpCode.AddI, temp, sign);               // temp = new value
                _builder.EmitABC(OpCode.SetTable, objReg, idxReg, temp);  // obj[idx] = new value
                _scope.FreeTemp(temp);
                // dest = old value — postfix result
            }

            _scope.FreeTemp(idxReg);
            _scope.FreeTemp(objReg);
            return null;
        }

        throw new CompileError("Invalid update expression operand.", expr.Span);
    }

    /// <inheritdoc />
    public object? VisitLambdaExpr(LambdaExpr expr)
    {
        byte dest = _destReg;
        _builder.AddSourceMapping(expr.Span);

        var child = new Compiler(this, null, _globalSlots);
        int paramCount = expr.Parameters.Count;

        // Declare parameters as locals (registers 0..paramCount-1)
        foreach (Token param in expr.Parameters)
            child._scope.DeclareLocal(param.Lexeme);

        // Compute MinArity: scan trailing defaults
        int minArity = paramCount;
        bool hasDefaultParams = false;
        if (expr.DefaultValues != null && expr.DefaultValues.Count > 0)
        {
            int countForDefaults = expr.HasRestParam ? paramCount - 1 : paramCount;
            for (int i = countForDefaults - 1; i >= 0; i--)
            {
                if (expr.DefaultValues.Count > i && expr.DefaultValues[i] != null)
                {
                    minArity = i;
                    hasDefaultParams = true;
                }
                else
                    break;
            }
        }

        if (hasDefaultParams && expr.DefaultValues != null)
        {
            for (int i = 0; i < expr.DefaultValues.Count && i < paramCount; i++)
            {
                if (expr.DefaultValues[i] == null) continue;

                byte paramReg = (byte)i;
                byte tempSentinel = child._scope.AllocTemp();
                ushort sentinelIdx = child._builder.AddConstant(VirtualMachine.NotProvided);
                child._builder.EmitABx(OpCode.LoadK, tempSentinel, sentinelIdx);

                byte cmpReg = child._scope.AllocTemp();
                child._builder.EmitABC(OpCode.Ne, cmpReg, paramReg, tempSentinel);
                int skipDefault = child._builder.EmitJump(OpCode.JmpTrue, cmpReg);
                child._scope.FreeTemp(cmpReg);
                child._scope.FreeTemp(tempSentinel);

                child.CompileExprTo(expr.DefaultValues[i]!, paramReg);
                child._builder.PatchJump(skipDefault);
            }
        }

        // Compile body
        if (expr.ExpressionBody != null)
        {
            byte resultReg = child._scope.AllocTemp();
            child.CompileExprTo(expr.ExpressionBody, resultReg);
            child._builder.EmitABC(OpCode.Return, resultReg, 1, 0);
            child._scope.FreeTemp(resultReg);
        }
        else if (expr.BlockBody != null)
        {
            foreach (Stmt s in expr.BlockBody.Statements)
                child.CompileStmt(s);
        }

        // Implicit return null at function end (unreachable for expression bodies)
        byte retReg = child._scope.AllocTemp();
        child._builder.EmitA(OpCode.LoadNull, retReg);
        child._builder.EmitABC(OpCode.Return, retReg, 1, 0);
        child._scope.FreeTemp(retReg);

        // Finalize chunk metadata
        child._builder.Arity = paramCount;
        child._builder.MinArity = minArity;
        child._builder.MaxRegs = child._scope.MaxRegs;
        child._builder.IsAsync = expr.IsAsync;
        child._builder.HasRestParam = expr.HasRestParam;
        child._builder.LocalNames = child._scope.GetLocalNames();
        child._builder.LocalIsConst = child._scope.GetLocalIsConst();
        child._builder.UpvalueNames = child._upvalueNames?.ToArray();

        Chunk lambdaChunk = child._builder.Build();

        ushort chunkIdx = _builder.AddConstant(StashValue.FromObj(lambdaChunk));
        _builder.EmitABx(OpCode.Closure, dest, chunkIdx);
        foreach (UpvalueDescriptor uv in lambdaChunk.Upvalues)
            _builder.EmitRaw((uint)(uv.IsLocal ? 1 : 0) | ((uint)uv.Index << 8));

        return null;
    }

    /// <inheritdoc />
    public object? VisitRetryExpr(RetryExpr expr)
    {
        byte dest = _destReg;
        _builder.AddSourceMapping(expr.Span);

        bool hasUntil = expr.UntilClause != null;
        bool hasOnRetry = expr.OnRetryClause != null;
        bool onRetryIsReference = hasOnRetry && expr.OnRetryClause!.IsReference;

        // All retry operands occupy consecutive temp registers starting at maxAttemptsReg.
        // Layout: [maxAttempts] [options...] [body] [until?] [onRetry?]
        // Retry ABx: R(A)=maxAttempts is overwritten with the result; K(Bx)=metadata.

        // ── maxAttempts ──────────────────────────────────────────────────
        byte maxAttemptsReg = _scope.AllocTemp();
        CompileExprTo(expr.MaxAttempts, maxAttemptsReg);

        // ── options ──────────────────────────────────────────────────────
        int optionCount = 0;
        if (expr.NamedOptions != null && expr.NamedOptions.Count > 0)
        {
            // Named options: (nameReg, valueReg) pairs in consecutive registers
            foreach (var (name, value) in expr.NamedOptions)
            {
                byte nameReg = _scope.AllocTemp();
                ushort nameIdx = _builder.AddConstant(name.Lexeme);
                _builder.EmitABx(OpCode.LoadK, nameReg, nameIdx);

                byte valReg = _scope.AllocTemp();
                CompileExprTo(value, valReg);

                optionCount++;
            }
        }
        else if (expr.OptionsExpr != null)
        {
            // Struct/dict options: one register holding the options object
            byte optReg = _scope.AllocTemp();
            CompileExprTo(expr.OptionsExpr, optReg);
            optionCount = -1;
        }

        // ── body closure ─────────────────────────────────────────────────
        byte bodyReg = _scope.AllocTemp();
        {
            var bodyChild = new Compiler(this, "<retry_body>", _globalSlots);

            // Single `attempt` parameter in register 0
            bodyChild._scope.DeclareLocal("attempt");
            bodyChild._builder.Arity = 1;
            bodyChild._builder.MinArity = 1;

            var bodyStmts = expr.Body.Statements;
            for (int i = 0; i < bodyStmts.Count; i++)
            {
                bool isLast = i == bodyStmts.Count - 1;
                if (isLast && bodyStmts[i] is ExprStmt lastExprStmt)
                {
                    // Return the value of the final expression statement
                    byte resultReg = bodyChild._scope.AllocTemp();
                    bodyChild.CompileExprTo(lastExprStmt.Expression, resultReg);
                    bodyChild._builder.EmitABC(OpCode.Return, resultReg, 1, 0);
                    bodyChild._scope.FreeTemp(resultReg);
                }
                else
                {
                    bodyChild.CompileStmt(bodyStmts[i]);
                }
            }

            // Implicit null return (fallthrough / empty body)
            byte bodyRetReg = bodyChild._scope.AllocTemp();
            bodyChild._builder.EmitA(OpCode.LoadNull, bodyRetReg);
            bodyChild._builder.EmitABC(OpCode.Return, bodyRetReg, 1, 0);
            bodyChild._scope.FreeTemp(bodyRetReg);

            bodyChild._builder.MaxRegs = bodyChild._scope.MaxRegs;
            bodyChild._builder.LocalNames = bodyChild._scope.GetLocalNames();
            bodyChild._builder.LocalIsConst = bodyChild._scope.GetLocalIsConst();
            bodyChild._builder.UpvalueNames = bodyChild._upvalueNames?.ToArray();

            Chunk bodyChunk = bodyChild._builder.Build();
            ushort bodyChunkIdx = _builder.AddConstant(StashValue.FromObj(bodyChunk));
            _builder.EmitABx(OpCode.Closure, bodyReg, bodyChunkIdx);
            foreach (UpvalueDescriptor uv in bodyChunk.Upvalues)
                _builder.EmitRaw((uint)(uv.IsLocal ? 1 : 0) | ((uint)uv.Index << 8));
        }

        // ── until clause ─────────────────────────────────────────────────
        if (hasUntil)
        {
            byte untilReg = _scope.AllocTemp();
            CompileExprTo(expr.UntilClause!, untilReg);
        }

        // ── onRetry clause ───────────────────────────────────────────────
        if (hasOnRetry)
        {
            byte onRetryReg = _scope.AllocTemp();
            OnRetryNode onRetry = expr.OnRetryClause!;

            if (onRetry.IsReference)
            {
                CompileExprTo(onRetry.Reference!, onRetryReg);
            }
            else
            {
                // Inline block: compile as closure with (attempt, error) parameters
                var onRetryChild = new Compiler(this, "<on_retry>", _globalSlots);
                onRetryChild._scope.DeclareLocal(onRetry.ParamAttempt?.Lexeme ?? "<attempt>");
                onRetryChild._scope.DeclareLocal(onRetry.ParamError?.Lexeme ?? "<error>");
                onRetryChild._builder.Arity = 2;
                onRetryChild._builder.MinArity = 2;

                foreach (Stmt s in onRetry.Body!.Statements)
                    onRetryChild.CompileStmt(s);

                byte onRetryRetReg = onRetryChild._scope.AllocTemp();
                onRetryChild._builder.EmitA(OpCode.LoadNull, onRetryRetReg);
                onRetryChild._builder.EmitABC(OpCode.Return, onRetryRetReg, 1, 0);
                onRetryChild._scope.FreeTemp(onRetryRetReg);

                onRetryChild._builder.MaxRegs = onRetryChild._scope.MaxRegs;
                onRetryChild._builder.LocalNames = onRetryChild._scope.GetLocalNames();
                onRetryChild._builder.LocalIsConst = onRetryChild._scope.GetLocalIsConst();
                onRetryChild._builder.UpvalueNames = onRetryChild._upvalueNames?.ToArray();

                Chunk onRetryChunk = onRetryChild._builder.Build();
                ushort onRetryChunkIdx = _builder.AddConstant(StashValue.FromObj(onRetryChunk));
                _builder.EmitABx(OpCode.Closure, onRetryReg, onRetryChunkIdx);
                foreach (UpvalueDescriptor uv in onRetryChunk.Upvalues)
                    _builder.EmitRaw((uint)(uv.IsLocal ? 1 : 0) | ((uint)uv.Index << 8));
            }
        }

        // ── emit Retry instruction ────────────────────────────────────────
        var metadata = new RetryMetadata(optionCount, hasUntil, hasOnRetry, onRetryIsReference);
        ushort metaIdx = _builder.AddConstant(metadata);
        _builder.EmitABx(OpCode.Retry, maxAttemptsReg, metaIdx);

        // VM writes the retry result back to R(maxAttemptsReg)
        if (maxAttemptsReg != dest)
            _builder.EmitAB(OpCode.Move, dest, maxAttemptsReg);
        _scope.FreeTempFrom(maxAttemptsReg);

        return null;
    }

    public object? VisitTimeoutExpr(TimeoutExpr expr)
    {
        byte dest = _destReg;
        _builder.AddSourceMapping(expr.Span);

        // ── duration ─────────────────────────────────────────────────────
        byte durationReg = _scope.AllocTemp();
        CompileExprTo(expr.Duration, durationReg);

        // ── body closure ─────────────────────────────────────────────────
        byte bodyReg = _scope.AllocTemp();
        {
            var bodyChild = new Compiler(this, "<timeout_body>", _globalSlots);

            // No parameters for timeout body
            bodyChild._builder.Arity = 0;
            bodyChild._builder.MinArity = 0;

            var bodyStmts = expr.Body.Statements;
            for (int i = 0; i < bodyStmts.Count; i++)
            {
                bool isLast = i == bodyStmts.Count - 1;
                if (isLast && bodyStmts[i] is ExprStmt lastExprStmt)
                {
                    byte resultReg = bodyChild._scope.AllocTemp();
                    bodyChild.CompileExprTo(lastExprStmt.Expression, resultReg);
                    bodyChild._builder.EmitABC(OpCode.Return, resultReg, 1, 0);
                    bodyChild._scope.FreeTemp(resultReg);
                }
                else
                {
                    bodyChild.CompileStmt(bodyStmts[i]);
                }
            }

            // Implicit null return
            byte bodyRetReg = bodyChild._scope.AllocTemp();
            bodyChild._builder.EmitA(OpCode.LoadNull, bodyRetReg);
            bodyChild._builder.EmitABC(OpCode.Return, bodyRetReg, 1, 0);
            bodyChild._scope.FreeTemp(bodyRetReg);

            bodyChild._builder.MaxRegs = bodyChild._scope.MaxRegs;
            bodyChild._builder.LocalNames = bodyChild._scope.GetLocalNames();
            bodyChild._builder.LocalIsConst = bodyChild._scope.GetLocalIsConst();
            bodyChild._builder.UpvalueNames = bodyChild._upvalueNames?.ToArray();

            Chunk bodyChunk = bodyChild._builder.Build();
            ushort bodyChunkIdx = _builder.AddConstant(StashValue.FromObj(bodyChunk));
            _builder.EmitABx(OpCode.Closure, bodyReg, bodyChunkIdx);
            foreach (UpvalueDescriptor uv in bodyChunk.Upvalues)
                _builder.EmitRaw((uint)(uv.IsLocal ? 1 : 0) | ((uint)uv.Index << 8));
        }

        // ── emit Timeout instruction ──────────────────────────────────────
        _builder.EmitABx(OpCode.Timeout, durationReg, 0);

        // VM writes the result to R(durationReg)
        if (durationReg != dest)
            _builder.EmitAB(OpCode.Move, dest, durationReg);
        _scope.FreeTempFrom(durationReg);

        return null;
    }
}
