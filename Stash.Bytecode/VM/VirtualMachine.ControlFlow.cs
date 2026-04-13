using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Stash.Common;
using Stash.Runtime;
using Stash.Runtime.Types;

namespace Stash.Bytecode;

/// <summary>
/// Control flow, exception handling, elevation, retry, and iterator opcode handlers (register-based).
/// </summary>
public sealed partial class VirtualMachine
{
    // ══════════════════════════ Exception Handling ══════════════════════════

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ExecuteThrow(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        object? errorVal = _stack[frame.BaseSlot + a].ToObject();
        SourceSpan? span = GetCurrentSpan(ref frame);

        if (errorVal is StashError se)
            throw new RuntimeError(se.Message, span, se.Type) { Properties = se.Properties };

        if (errorVal is string msg)
            throw new RuntimeError(msg, span);

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
                    props[k] = kv.Value.ToObject();
            }
            throw new RuntimeError(errMsg, span, errType) { Properties = props };
        }

        throw new RuntimeError(RuntimeValues.Stringify(errorVal), span);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteTryEnd()
    {
        if (_exceptionHandlers.Count > 0)
            _exceptionHandlers.RemoveAt(_exceptionHandlers.Count - 1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteTryExpr(ref CallFrame frame, uint inst)
    {
        // ABx: R(A) = R(B) — value already computed; any exception was already caught by the handler.
        byte a = Instruction.GetA(inst);
        byte b = Instruction.GetB(inst);
        _stack[frame.BaseSlot + a] = _stack[frame.BaseSlot + b];
    }

    // ══════════════════════════ Switch ══════════════════════════

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ExecuteSwitch(ref CallFrame frame, uint inst)
    {
        // Switch is compiled to basic comparison + jump opcodes; this opcode is a no-op.
        _ = Instruction.GetBx(inst);
    }

    // ══════════════════════════ Elevation ══════════════════════════

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ExecuteElevateBegin(ref CallFrame frame, uint inst)
    {
        if (_context.EmbeddedMode)
            throw new RuntimeError("Elevate blocks are not supported in embedded mode.", GetCurrentSpan(ref frame));

        byte b = Instruction.GetB(inst);
        object? elevator = _stack[frame.BaseSlot + b].ToObject();
        _context.ElevationActive = true;

        if (elevator is string elevStr)
            _context.ElevationCommand = elevStr;
        else if (elevator != null)
            _context.ElevationCommand = RuntimeOps.Stringify(StashValue.FromObject(elevator));
        else
            _context.ElevationCommand = OperatingSystem.IsWindows() ? "gsudo" : "sudo";
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteElevateEnd(ref CallFrame frame, uint inst)
    {
        _context.ElevationActive = false;
        _context.ElevationCommand = null;
    }

    // ══════════════════════════ Retry ══════════════════════════

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ExecuteRetry(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        int @base = frame.BaseSlot;
        SourceSpan? span = GetCurrentSpan(ref frame);
        var retryMeta = (RetryMetadata)frame.Chunk.Constants[Instruction.GetBx(inst)].AsObj!;

        // Read maxAttempts from R(A).
        StashValue maxAttemptsVal = _stack[@base + a];
        if (!maxAttemptsVal.IsInt)
            throw new RuntimeError("Retry max attempts must be an integer.", span);
        long maxAttempts = maxAttemptsVal.AsInt;
        if (maxAttempts < 0)
            throw new RuntimeError("Retry max attempts must be non-negative.", span);
        if (maxAttempts == 0)
            throw new RuntimeError(
                "All 0 retry attempts exhausted — predicate not satisfied.",
                span, "RetryExhaustedError");

        // Read options from registers at R(A+1).
        int nextReg = a + 1;
        long retryDelayMs = 0;

        if (retryMeta.OptionCount == -1)
        {
            // Single options struct at R(A+1).
            object? optStruct = _stack[@base + nextReg].ToObject();
            if (optStruct is StashInstance oi)
            {
                if (oi.GetFields().TryGetValue("delay", out StashValue dv))
                {
                    object? dvObj = dv.ToObject();
                    if (dvObj is long dl)
                        retryDelayMs = dl;
                    else if (dvObj is StashDuration dd)
                        retryDelayMs = (long)dd.TotalMilliseconds;
                }
            }
            nextReg++;
        }
        else if (retryMeta.OptionCount > 0)
        {
            // Named option pairs at R(A+1)..R(A+2*N).
            for (int i = 0; i < retryMeta.OptionCount; i++)
            {
                string optKey = (string)_stack[@base + nextReg + i * 2].AsObj!;
                object? optVal = _stack[@base + nextReg + i * 2 + 1].ToObject();
                if (optKey == "delay")
                {
                    if (optVal is long dl)
                        retryDelayMs = dl;
                    else if (optVal is StashDuration dd)
                        retryDelayMs = (long)dd.TotalMilliseconds;
                }
            }
            nextReg += retryMeta.OptionCount * 2;
        }

        // Body closure at R(nextReg).
        object? bodyObj = _stack[@base + nextReg].ToObject();
        VMFunction bodyVmFn = bodyObj as VMFunction
            ?? throw new RuntimeError("Retry body must be a function.", span);
        nextReg++;

        // Until closure at R(nextReg) if present.
        VMFunction? untilVmFn = null;
        IStashCallable? untilCb = null;
        if (retryMeta.HasUntilClause)
        {
            object? obj = _stack[@base + nextReg].ToObject();
            if (obj is VMFunction f) untilVmFn = f;
            else if (obj is IStashCallable c) untilCb = c;
            nextReg++;
        }

        // OnRetry closure at R(nextReg) if present.
        VMFunction? onRetryVmFn = null;
        IStashCallable? onRetryCb = null;
        if (retryMeta.HasOnRetryClause)
        {
            object? obj = _stack[@base + nextReg].ToObject();
            if (obj is VMFunction f) onRetryVmFn = f;
            else if (obj is IStashCallable c) onRetryCb = c;
        }

        // Execute retry loop.
        StashValue retryLastResult = StashValue.Null;
        RuntimeError? retryLastError = null;
        var retryErrors = new List<StashValue>();

        for (long attempt = 1; attempt <= maxAttempts; attempt++)
        {
            bool bodyThrew = false;
            try
            {
                var attemptCtx = new StashDictionary();
                attemptCtx.Set("current", StashValue.FromInt(attempt));
                attemptCtx.Set("max", StashValue.FromInt(maxAttempts));
                attemptCtx.Set("remaining", StashValue.FromInt(maxAttempts - attempt));
                attemptCtx.Set("errors", StashValue.FromObj(new List<StashValue>(retryErrors)));
                retryLastResult = ExecuteVMFunctionInlineDirect(
                    bodyVmFn, new StashValue[] { StashValue.FromObj(attemptCtx) }, span);
            }
            catch (RuntimeError rex)
            {
                bodyThrew = true;
                retryLastError = rex;
                var retryErr = new StashError(rex.Message, rex.ErrorType ?? "RuntimeError", null, rex.Properties);
                retryErrors.Add(StashValue.FromObj(retryErr));

                // Call onRetry only when this is not the last attempt.
                if (attempt < maxAttempts)
                {
                    if (onRetryVmFn != null)
                        ExecuteVMFunctionInlineDirect(onRetryVmFn,
                            new StashValue[] { StashValue.FromInt(attempt), StashValue.FromObj(retryErr) }, span);
                    else if (onRetryCb != null)
                        onRetryCb.CallDirect(_context,
                            new StashValue[] { StashValue.FromInt(attempt), StashValue.FromObj(retryErr) });
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
                    if (RuntimeOps.IsFalsy(pred)) success = false;
                }
                else if (untilCb != null)
                {
                    StashValue[] untilArgs2 = untilCb is VMFunction cbFn && cbFn.Chunk.Arity >= 2
                        ? new StashValue[] { retryLastResult, StashValue.FromInt(attempt) }
                        : new StashValue[] { retryLastResult };
                    StashValue pred2 = untilCb.CallDirect(_context, untilArgs2);
                    if (RuntimeOps.IsFalsy(pred2)) success = false;
                }

                if (success)
                {
                    _stack[@base + a] = retryLastResult;
                    return;
                }

                // Until predicate failed — treat as a retry-worthy failure.
                if (attempt < maxAttempts)
                {
                    var predicateErr = new StashError("Predicate not satisfied", "RetryError");
                    if (onRetryVmFn != null)
                        ExecuteVMFunctionInlineDirect(onRetryVmFn,
                            new StashValue[] { StashValue.FromInt(attempt), StashValue.FromObj(predicateErr) }, span);
                    else if (onRetryCb != null)
                        onRetryCb.CallDirect(_context,
                            new StashValue[] { StashValue.FromInt(attempt), StashValue.FromObj(predicateErr) });
                }
                bodyThrew = true;
            }

            if (retryDelayMs > 0 && attempt < maxAttempts)
            {
                if (_ct.CanBeCanceled)
                {
                    _ct.WaitHandle.WaitOne((int)retryDelayMs);
                    _ct.ThrowIfCancellationRequested();
                }
                else
                {
                    Thread.Sleep((int)retryDelayMs);
                }
            }

            if (attempt == maxAttempts)
            {
                if (retryLastError != null)
                    throw new RuntimeError(retryLastError.Message, span,
                        retryLastError.ErrorType ?? "RuntimeError");
                throw new RuntimeError(
                    $"All {maxAttempts} retry attempts exhausted — predicate not satisfied.",
                    span, "RetryExhaustedError");
            }
        }

        // Unreachable — loop always returns or throws on the last attempt.
        throw new RuntimeError("Internal error: retry exhausted without result.", span);
    }

    // ══════════════════════════ Timeout ══════════════════════════

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ExecuteTimeout(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        int @base = frame.BaseSlot;
        SourceSpan? span = GetCurrentSpan(ref frame);

        // Read duration from R(A)
        object? durationObj = _stack[@base + a].ToObject();
        long timeoutMs;
        if (durationObj is StashDuration sd)
            timeoutMs = (long)sd.TotalMilliseconds;
        else if (durationObj is long ms)
            timeoutMs = ms;
        else if (durationObj is double dms)
            timeoutMs = (long)dms;
        else
            throw new RuntimeError("Timeout duration must be a duration or number of milliseconds.", span);

        if (timeoutMs <= 0)
            throw new RuntimeError("Timeout duration must be positive.", span);

        // Body closure at R(A+1)
        object? bodyObj = _stack[@base + a + 1].ToObject();
        VMFunction bodyFn = bodyObj as VMFunction
            ?? throw new RuntimeError("Timeout body must be a function.", span);

        // Create a CancellationTokenSource linked to the outer _ct with the timeout applied
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(_ct);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));

        // Swap in the timeout-aware token for the duration of the body
        CancellationToken oldCt = _ct;
        _ct = timeoutCts.Token;
        _context.SetCancellationToken(timeoutCts.Token);

        try
        {
            StashValue result = ExecuteVMFunctionInlineDirect(bodyFn, ReadOnlySpan<StashValue>.Empty, span);
            _stack[@base + a] = result;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !oldCt.IsCancellationRequested)
        {
            // The timeout fired — not an external cancellation
            throw new RuntimeError(
                $"Operation timed out after {timeoutMs}ms.",
                span, "TimeoutError");
        }
        finally
        {
            _ct = oldCt;
            _context.SetCancellationToken(oldCt);
        }
    }

    // ══════════════════════════ Numeric For ══════════════════════════

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteForPrep(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        int sBx = Instruction.GetSBx(inst);
        int @base = frame.BaseSlot;

        StashValue counter = _stack[@base + a];
        StashValue step = _stack[@base + a + 2];

        if (counter.IsInt && step.IsInt)
            _stack[@base + a] = StashValue.FromInt(counter.AsInt - step.AsInt);
        else if (counter.IsNumeric && step.IsNumeric)
            _stack[@base + a] = StashValue.FromFloat(
                (counter.IsInt ? (double)counter.AsInt : counter.AsFloat) -
                (step.IsInt ? (double)step.AsInt : step.AsFloat));
        else
            throw new RuntimeError("For loop counter and step must be numbers.", GetCurrentSpan(ref frame));

        frame.IP += sBx;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteForPrepII(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        int @base = frame.BaseSlot;

        if (_stack[@base + a].IsInt && _stack[@base + a + 1].IsInt && _stack[@base + a + 2].IsInt)
        {
            _stack[@base + a] = StashValue.FromInt(_stack[@base + a].AsInt - _stack[@base + a + 2].AsInt);
            frame.IP += Instruction.GetSBx(inst);
            return;
        }

        // Limit or counter is not int — fall back to generic path
        ExecuteForPrep(ref frame, inst);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteForLoopII(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        int @base = frame.BaseSlot;

        if (_stack[@base + a].IsInt && _stack[@base + a + 1].IsInt && _stack[@base + a + 2].IsInt)
        {
            long step = _stack[@base + a + 2].AsInt;
            long newCounter = _stack[@base + a].AsInt + step;
            _stack[@base + a] = StashValue.FromInt(newCounter);

            long limit = _stack[@base + a + 1].AsInt;
            if (step > 0 ? newCounter <= limit : newCounter >= limit)
            {
                frame.IP += Instruction.GetSBx(inst);
                _stack[@base + a + 3] = StashValue.FromInt(newCounter);
            }
            return;
        }

        // Fall back to generic handler (handles float, mixed types)
        ExecuteForLoop(ref frame, inst);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteForLoop(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        int sBx = Instruction.GetSBx(inst);
        int @base = frame.BaseSlot;

        StashValue step = _stack[@base + a + 2];

        if (_stack[@base + a].IsInt && step.IsInt && _stack[@base + a + 1].IsInt)
        {
            long newCounter = _stack[@base + a].AsInt + step.AsInt;
            _stack[@base + a] = StashValue.FromInt(newCounter);
            long limit = _stack[@base + a + 1].AsInt;
            bool inBounds = step.AsInt > 0 ? newCounter <= limit : newCounter >= limit;
            if (inBounds)
            {
                frame.IP += sBx;
                _stack[@base + a + 3] = StashValue.FromInt(newCounter);
            }
        }
        else if (_stack[@base + a].IsNumeric && step.IsNumeric && _stack[@base + a + 1].IsNumeric)
        {
            StashValue counterVal = _stack[@base + a];
            StashValue limitVal = _stack[@base + a + 1];
            double newCounter = (counterVal.IsInt ? (double)counterVal.AsInt : counterVal.AsFloat) +
                                (step.IsInt ? (double)step.AsInt : step.AsFloat);
            _stack[@base + a] = StashValue.FromFloat(newCounter);
            double limit = limitVal.IsInt ? (double)limitVal.AsInt : limitVal.AsFloat;
            double stepVal = step.IsInt ? (double)step.AsInt : step.AsFloat;
            bool inBounds = stepVal > 0 ? newCounter <= limit : newCounter >= limit;
            if (inBounds)
            {
                frame.IP += sBx;
                _stack[@base + a + 3] = StashValue.FromFloat(newCounter);
            }
        }
        else
        {
            throw new RuntimeError("For loop counter and step must be numbers.", GetCurrentSpan(ref frame));
        }
    }

    // ══════════════════════════ Iterator ══════════════════════════

    /// <summary>
    /// Internal iterator state for register-based for-in loops.
    /// Stored in R(A) by IterPrep; advanced by IterLoop.
    /// </summary>
    private sealed class IteratorState
    {
        public object? Collection;
        public int Index;
        public bool Indexed;
        public IEnumerator<KeyValuePair<object, StashValue>>? DictEnumerator;
    }

    private void ExecuteIterPrep(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        byte b = Instruction.GetB(inst);
        int @base = frame.BaseSlot;
        object? val = _stack[@base + a].ToObject();
        SourceSpan? span = GetCurrentSpan(ref frame);

        IteratorState iterState;

        if (val is List<StashValue> list)
        {
            // Snapshot the list so mutations during iteration don't cause infinite loops
            iterState = new IteratorState { Collection = new List<StashValue>(list) };
        }
        else if (val is StashDictionary dict)
        {
            iterState = new IteratorState
            {
                Collection = dict,
                DictEnumerator = dict.GetAllEntries().GetEnumerator(),
            };
        }
        else if (val is string str)
        {
            iterState = new IteratorState { Collection = str };
        }
        else if (val is StashRange range)
        {
            iterState = new IteratorState { Collection = range };
        }
        else if (val is StashEnum enumDef)
        {
            // Build a list of StashEnumValues so IterLoop can use the list path.
            var members = new List<StashValue>(enumDef.Members.Count);
            foreach (string m in enumDef.Members)
            {
                StashEnumValue? ev = enumDef.GetMember(m);
                members.Add(ev != null ? StashValue.FromObj(ev) : StashValue.Null);
            }
            iterState = new IteratorState { Collection = members };
        }
        else
        {
            throw new RuntimeError(
                $"Value is not iterable: {RuntimeValues.Stringify(val)}.", span);
        }

        iterState.Indexed = b != 0;
        _stack[@base + a] = StashValue.FromObj(iterState);
    }

    private void ExecuteIterLoop(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        int @base = frame.BaseSlot;
        var iter = (IteratorState)_stack[@base + a].AsObj!;

        if (iter.Collection is List<StashValue> list)
        {
            if (iter.Index >= list.Count)
            {
                frame.IP += Instruction.GetSBx(inst);
                return;
            }
            _stack[@base + a + 1] = list[iter.Index];
            _stack[@base + a + 2] = StashValue.FromInt(iter.Index);
            iter.Index++;
        }
        else if (iter.Collection is StashDictionary)
        {
            if (!iter.DictEnumerator!.MoveNext())
            {
                frame.IP += Instruction.GetSBx(inst);
                return;
            }
            var kv = iter.DictEnumerator.Current;
            if (iter.Indexed)
            {
                _stack[@base + a + 1] = kv.Value;                     // value slot → dict value (VariableName)
                _stack[@base + a + 2] = StashValue.FromObj(kv.Key);  // index slot → dict key (IndexName)
            }
            else
            {
                _stack[@base + a + 1] = StashValue.FromObj(kv.Key);  // single-var: key goes to VariableName
                _stack[@base + a + 2] = kv.Value;                     // unused in single-var mode
            }
            iter.Index++;
        }
        else if (iter.Collection is string str)
        {
            if (iter.Index >= str.Length)
            {
                frame.IP += Instruction.GetSBx(inst);
                return;
            }
            _stack[@base + a + 1] = StashValue.FromObj(str[iter.Index].ToString());
            _stack[@base + a + 2] = StashValue.FromInt(iter.Index);
            iter.Index++;
        }
        else if (iter.Collection is StashRange range)
        {
            long step = range.Step;
            long current = range.Start + step * iter.Index;
            bool inBounds = step > 0 ? current < range.End : current > range.End;
            if (!inBounds)
            {
                frame.IP += Instruction.GetSBx(inst);
                return;
            }
            _stack[@base + a + 1] = StashValue.FromInt(current);
            _stack[@base + a + 2] = StashValue.FromInt(iter.Index);
            iter.Index++;
        }
        else
        {
            // Exhausted or unknown — jump past the loop body.
            frame.IP += Instruction.GetSBx(inst);
        }
    }
}
