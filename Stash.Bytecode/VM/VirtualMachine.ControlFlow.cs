using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Stash.Common;
using Stash.Runtime;
using Stash.Runtime.Types;

namespace Stash.Bytecode;

/// <summary>
/// Control flow, exception handling, and retry opcode handlers.
/// </summary>
public sealed partial class VirtualMachine
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteAnd(ref CallFrame frame)
    {
        // Short-circuit AND: if top is falsy, keep it and jump (skip right); else pop and eval right
        short offset = ReadI16(ref frame);
        if (RuntimeOps.IsFalsy(Peek()))
        {
            frame.IP += offset;
        }
        else
        {
            _sp--; // pop truthy left, continue to right operand
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteOr(ref CallFrame frame)
    {
        // Short-circuit OR: if top is truthy, keep it and jump (skip right); else pop and eval right
        short offset = ReadI16(ref frame);
        if (!RuntimeOps.IsFalsy(Peek()))
        {
            frame.IP += offset;
        }
        else
        {
            _sp--; // pop falsy left, continue to right operand
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteNullCoalesce(ref CallFrame frame)
    {
        // If top is non-null, keep it and jump; else pop null and eval right
        short offset = ReadI16(ref frame);
        if (!Peek().IsNull && Peek().ToObject() is not StashError)
        {
            frame.IP += offset;
        }
        else
        {
            _sp--; // pop null, continue to right operand
        }
    }

    private void ExecuteThrow(ref CallFrame frame)
    {
        object? errorVal = Pop().ToObject();
        SourceSpan? span = GetCurrentSpan(ref frame);
        if (errorVal is StashError se)
        {
            throw new RuntimeError(se.Message, span, se.Type) { Properties = se.Properties };
        }

        if (errorVal is string msg)
        {
            throw new RuntimeError(msg, span);
        }

        if (errorVal is StashDictionary throwDict)
        {
            string errMsg = throwDict.Has("message")
                ? throwDict.Get("message").ToObject()?.ToString() ?? ""
                : RuntimeValues.Stringify(errorVal);
            string errType = throwDict.Has("type")
                ? throwDict.Get("type").ToObject()?.ToString() ?? "RuntimeError"
                : "RuntimeError";
            var props = new Dictionary<string, object?>();
            foreach (var kv in throwDict.GetAllEntries())
            {
                if (kv.Key is string k)
                {
                    props[k] = kv.Value.ToObject();
                }
            }

            throw new RuntimeError(errMsg, span, errType) { Properties = props };
        }
        throw new RuntimeError(RuntimeValues.Stringify(errorVal), span);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteTryBegin(ref CallFrame frame)
    {
        short catchOffset = ReadI16(ref frame);
        _exceptionHandlers.Add(new ExceptionHandler
        {
            CatchIP = frame.IP + catchOffset,
            StackLevel = _sp,
            FrameIndex = _frameCount - 1,
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteSwitch(ref CallFrame frame)
    {
        ReadU16(ref frame); // skip operand — switch is compiled to basic opcodes
    }

    private void ExecuteElevateBegin(ref CallFrame frame)
    {
        if (_context.EmbeddedMode)
        {
            throw new RuntimeError("Elevate blocks are not supported in embedded mode.", GetCurrentSpan(ref frame));
        }

        object? elevator = Pop().ToObject();
        _context.ElevationActive = true;
        if (elevator is string elevStr)
        {
            _context.ElevationCommand = elevStr;
        }
        else if (elevator != null)
        {
            _context.ElevationCommand = RuntimeOps.Stringify(StashValue.FromObject(elevator));
        }
        else
        {
            _context.ElevationCommand = OperatingSystem.IsWindows() ? "gsudo" : "sudo";
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteElevateEnd(ref CallFrame frame)
    {
        _context.ElevationActive = false;
        _context.ElevationCommand = null;
    }

    private StashValue ExecuteRetry(ref CallFrame frame)
    {
        ushort metaRetryIdx = ReadU16(ref frame);
        SourceSpan? span = GetCurrentSpan(ref frame);
        var retryMeta = (RetryMetadata)frame.Chunk.Constants[metaRetryIdx].AsObj!;

        // Pop closures in reverse order of emission (onRetry first, then until, then body)
        VMFunction? onRetryVmFn = null;
        IStashCallable? onRetryCb = null;
        if (retryMeta.HasOnRetryClause)
        {
            object? obj = Pop().ToObject();
            if (obj is VMFunction f1)
            {
                onRetryVmFn = f1;
            }
            else if (obj is IStashCallable c1)
            {
                onRetryCb = c1;
            }
        }

        VMFunction? untilVmFn = null;
        IStashCallable? untilCb = null;
        if (retryMeta.HasUntilClause)
        {
            object? obj = Pop().ToObject();
            if (obj is VMFunction f2)
            {
                untilVmFn = f2;
            }
            else if (obj is IStashCallable c2)
            {
                untilCb = c2;
            }
        }

        object? bodyObj = Pop().ToObject();
        VMFunction bodyVmFn = bodyObj as VMFunction
            ?? throw new RuntimeError("Retry body must be a function.", span);

        // Extract options from the stack (below body)
        long retryDelayMs = 0;
        if (retryMeta.OptionCount == -1)
        {
            object? optStruct = Pop().ToObject();
            if (optStruct is StashInstance oi)
            {
                if (oi.GetFields().TryGetValue("delay", out StashValue dv))
                {
                    object? dvObj = dv.ToObject();
                    if (dvObj is long dl)
                    {
                        retryDelayMs = dl;
                    }
                    else if (dvObj is StashDuration dd)
                    {
                        retryDelayMs = (long)dd.TotalMilliseconds;
                    }
                }
            }
        }
        else if (retryMeta.OptionCount > 0)
        {
            int pairStart = _sp - retryMeta.OptionCount * 2;
            for (int oi = 0; oi < retryMeta.OptionCount; oi++)
            {
                string optKey = (string)_stack[pairStart + oi * 2].AsObj!;
                object? optVal = _stack[pairStart + oi * 2 + 1].ToObject();
                if (optKey == "delay")
                {
                    if (optVal is long dl)
                    {
                        retryDelayMs = dl;
                    }
                    else if (optVal is StashDuration dd)
                    {
                        retryDelayMs = (long)dd.TotalMilliseconds;
                    }
                }
            }
            _sp = pairStart;
        }

        object? maxAttemptsObj = Pop().ToObject();
        if (maxAttemptsObj is not long maxAttempts)
        {
            throw new RuntimeError("Retry max attempts must be an integer.", span);
        }
        if (maxAttempts < 0)
        {
            throw new RuntimeError("Retry max attempts must be non-negative.", span);
        }
        if (maxAttempts == 0)
        {
            throw new RuntimeError(
                "All 0 retry attempts exhausted — predicate not satisfied.",
                span, "RetryExhaustedError");
        }

        // Execute retry loop
        StashValue retryLastResult = StashValue.Null;
        RuntimeError? retryLastError = null;

        var retryErrors = new List<StashValue>();

        for (long attempt = 1; attempt <= maxAttempts; attempt++)
        {
            bool bodyThrew = false;
            try
            {
                // Build attempt context dict
                var attemptCtx = new StashDictionary();
                attemptCtx.Set("current", StashValue.FromInt(attempt));
                attemptCtx.Set("max", StashValue.FromInt(maxAttempts));
                attemptCtx.Set("remaining", StashValue.FromInt(maxAttempts - attempt));
                attemptCtx.Set("errors", StashValue.FromObj(new List<StashValue>(retryErrors)));
                retryLastResult = ExecuteVMFunctionInlineDirect(bodyVmFn, new StashValue[] { StashValue.FromObj(attemptCtx) }, span);
            }
            catch (RuntimeError rex)
            {
                bodyThrew = true;
                retryLastError = rex;

                var retryErr = new StashError(rex.Message, rex.ErrorType ?? "RuntimeError", null, rex.Properties);
                retryErrors.Add(StashValue.FromObj(retryErr));

                // Only call onRetry if this is NOT the last attempt
                if (attempt < maxAttempts)
                {
                    if (onRetryVmFn != null)
                    {
                        ExecuteVMFunctionInlineDirect(onRetryVmFn,
                            new StashValue[] { StashValue.FromInt(attempt), StashValue.FromObj(retryErr) }, span);
                    }
                    else if (onRetryCb != null)
                    {
                        onRetryCb.CallDirect(_context, new StashValue[] { StashValue.FromInt(attempt), StashValue.FromObj(retryErr) });
                    }
                }
            }

            if (!bodyThrew)
            {
                bool success = true;
                if (untilVmFn != null)
                {
                    StashValue[] untilArgs = untilVmFn.Chunk.Arity >= 2
                        ? new StashValue[] { retryLastResult, StashValue.FromInt(attempt) }
                        : new StashValue[] { retryLastResult };
                    StashValue pred = ExecuteVMFunctionInlineDirect(untilVmFn, untilArgs, span);
                    if (RuntimeOps.IsFalsy(pred))
                    {
                        success = false;
                    }
                }
                else if (untilCb != null)
                {
                    StashValue[] untilArgs2 = untilCb is VMFunction cbFn && cbFn.Chunk.Arity >= 2
                        ? new StashValue[] { retryLastResult, StashValue.FromInt(attempt) }
                        : new StashValue[] { retryLastResult };
                    StashValue pred2 = untilCb.CallDirect(_context, untilArgs2);
                    if (RuntimeOps.IsFalsy(pred2))
                    {
                        success = false;
                    }
                }

                if (success)
                {
                    return retryLastResult;
                }
                // Until predicate failed — treat as a retry-worthy failure
                if (attempt < maxAttempts)
                {
                    if (onRetryVmFn != null)
                    {
                        ExecuteVMFunctionInlineDirect(onRetryVmFn,
                            new StashValue[] { StashValue.FromInt(attempt), StashValue.FromObj(new StashError("Predicate not satisfied", "RetryError")) }, span);
                    }
                    else if (onRetryCb != null)
                    {
                        onRetryCb.CallDirect(_context, new StashValue[] { StashValue.FromInt(attempt), StashValue.FromObj(new StashError("Predicate not satisfied", "RetryError")) });
                    }
                }
                bodyThrew = true;
            }

            if (retryDelayMs > 0 && attempt < maxAttempts)
            {
                Thread.Sleep((int)retryDelayMs);
            }

            if (attempt == maxAttempts)
            {
                if (retryLastError != null)
                {
                    throw new RuntimeError(retryLastError.Message, span,
                        retryLastError.ErrorType ?? "RuntimeError");
                }

                throw new RuntimeError(
                    $"All {maxAttempts} retry attempts exhausted — predicate not satisfied.",
                    span, "RetryExhaustedError");
            }
        }

        // Unreachable — loop always returns or throws on the last attempt
        throw new RuntimeError("Internal error: retry exhausted without result.", span);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private void ExecuteJump(ref CallFrame frame)
    {
        short offset = ReadI16(ref frame);
        frame.IP += offset;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private void ExecuteJumpTrue(ref CallFrame frame)
    {
        short offset = ReadI16(ref frame);
        if (!RuntimeOps.IsFalsy(Pop()))
        {
            frame.IP += offset;
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private void ExecuteJumpFalse(ref CallFrame frame)
    {
        short offset = ReadI16(ref frame);
        if (RuntimeOps.IsFalsy(Pop()))
        {
            frame.IP += offset;
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private void ExecuteLoop(ref CallFrame frame)
    {
        ushort offset = ReadU16(ref frame);
        frame.IP -= offset;
        if ((++_loopCheckCounter & 0xFF) == 0)
        {
            if (_ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(_ct);
            }

            if (StepLimit > 0)
            {
                StepCount += 256;
                if (StepCount >= StepLimit)
                {
                    throw new Stash.Runtime.StepLimitExceededException(StepLimit);
                }
            }
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private void ExecuteTryEnd()
    {
        if (_exceptionHandlers.Count > 0)
        {
            _exceptionHandlers.RemoveAt(_exceptionHandlers.Count - 1);
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private void ExecuteCloseUpvalue(ref CallFrame frame)
    {
        byte localSlot = ReadByte(ref frame);
        CloseUpvalues(frame.BaseSlot + localSlot);
    }
}
