using System;
using System.Collections.Generic;
using Stash.Common;
using Stash.Lexing;
using Stash.Parsing.AST;
using Stash.Runtime.Types;

namespace Stash.Bytecode;

/// <summary>
/// Complex expression visitors: update, lambda, and retry.
/// </summary>
public sealed partial class Compiler
{
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
