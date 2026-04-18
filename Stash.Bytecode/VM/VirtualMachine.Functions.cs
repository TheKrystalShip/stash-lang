using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Stash.Common;
using Stash.Debugging;
using Stash.Runtime;
using Stash.Runtime.Types;
using DebugCallFrame = Stash.Debugging.CallFrame;

namespace Stash.Bytecode;

/// <summary>
/// Function call, return, and closure opcode handlers.
/// </summary>
public sealed partial class VirtualMachine
{
    private void CallValue(object? callee, int argc, SourceSpan? callSpan)
    {
        if (callee is VMFunction fn)
        {
            Chunk fnChunk = fn.Chunk;
            int provided = argc;
            int expected = fnChunk.Arity;
            int minArity = fnChunk.MinArity;

            if (fnChunk.HasRestParam)
            {
                int nonRestCount = expected - 1;
                int minRequired = Math.Min(minArity, nonRestCount);

                if (provided < minRequired)
                {
                    throw new RuntimeError(
                        $"Expected at least {minRequired} arguments but got {provided}.",
                        callSpan);
                }

                // Pad non-rest params that weren't provided (may have defaults)
                if (provided < nonRestCount)
                {
                    for (int i = provided; i < nonRestCount; i++)
                    {
                        Push(StashValue.FromObj(NotProvided));
                    }

                    provided = nonRestCount;
                }

                // Collect rest args into a list
                int restCount = Math.Max(0, provided - nonRestCount);
                var restList = new List<StashValue>(restCount);
                int restStart = _sp - restCount;
                for (int i = restStart; i < _sp; i++)
                {
                    restList.Add(_stack[i]);
                }

                _sp = restStart;
                Push(StashValue.FromObj(restList));
                provided = expected;
            }
            else
            {
                // Arity checking for non-rest functions
                if (provided < minArity || provided > expected)
                {
                    string expectedStr = minArity == expected
                        ? $"{expected}"
                        : $"{minArity} to {expected}";
                    throw new RuntimeError(
                        $"Expected {expectedStr} arguments but got {provided}.",
                        callSpan);
                }

                // Pad missing optional args with NotProvided sentinel
                if (provided < expected)
                {
                    for (int i = provided; i < expected; i++)
                    {
                        Push(StashValue.FromObj(NotProvided));
                    }
                }
            }

            int baseSlot = _sp - expected;

            if (fnChunk.IsAsync)
            {
                Push(StashValue.FromObject(SpawnAsyncFunction(fnChunk, fn.Upvalues, baseSlot, callSpan, fn.ModuleGlobals)));
                return;
            }

            PushFrame(fnChunk, baseSlot, fn.Upvalues, fnChunk.Name, fn.ModuleGlobals);
            return;
        }

        if (callee is VMBoundMethod bound)
        {
            VMFunction boundFn = bound.Function;
            Chunk fnChunk = boundFn.Chunk;

            // Shift existing args right by 1 to make room for self after the callee slot.
            // This preserves the callee slot so OP_RETURN (_sp = baseSlot - 1) correctly
            // discards the frame + callee.
            int shiftStart = _sp - argc; // first arg position
            Push(StashValue.Null);        // grow stack + increment _sp
            for (int i = _sp - 2; i >= shiftStart; i--)
            {
                _stack[i + 1] = _stack[i];
            }

            _stack[shiftStart] = StashValue.FromObj(bound.Instance); // insert self after callee

            int provided = argc + 1; // user args + self
            int expected = fnChunk.Arity;
            int minArity = fnChunk.MinArity;

            if (fnChunk.HasRestParam)
            {
                int nonRestCount = expected - 1;
                int minRequired = Math.Min(minArity, nonRestCount);
                if (provided < minRequired)
                {
                    throw new RuntimeError($"Expected at least {minRequired - 1} arguments but got {argc}.", callSpan ?? _context.CurrentSpan);
                }

                if (provided < nonRestCount)
                {
                    for (int i = provided; i < nonRestCount; i++)
                    {
                        Push(StashValue.FromObj(NotProvided));
                    }

                    provided = nonRestCount;
                }

                int restCount = Math.Max(0, provided - nonRestCount);
                var restList = new List<StashValue>(restCount);
                int restStart = _sp - restCount;
                for (int i = restStart; i < _sp; i++)
                {
                    restList.Add(_stack[i]);
                }

                _sp = restStart;
                Push(StashValue.FromObj(restList));
                provided = expected;
            }
            else
            {
                if (provided < minArity || provided > expected)
                {
                    string expectedStr = minArity == expected
                        ? $"{expected - 1}"
                        : $"{minArity - 1} to {expected - 1}";
                    throw new RuntimeError($"Expected {expectedStr} arguments but got {argc}.", callSpan ?? _context.CurrentSpan);
                }

                if (provided < expected)
                {
                    for (int i = provided; i < expected; i++)
                    {
                        Push(StashValue.FromObj(NotProvided));
                    }
                }
            }

            int baseSlot = _sp - fnChunk.Arity;

            if (fnChunk.IsAsync)
            {
                Push(StashValue.FromObject(SpawnAsyncFunction(fnChunk, boundFn.Upvalues, baseSlot, callSpan ?? _context.CurrentSpan, boundFn.ModuleGlobals)));
                return;
            }

            PushFrame(fnChunk, baseSlot, boundFn.Upvalues, fnChunk.Name, boundFn.ModuleGlobals);
            return;
        }

        if (callee is VMExtensionBoundMethod extBound)
        {
            VMFunction extFn = extBound.Function;
            Chunk fnChunk = extFn.Chunk;

            // Same shift approach as VMBoundMethod — preserve callee slot for OP_RETURN.
            int shiftStart = _sp - argc;
            Push(StashValue.Null);
            for (int i = _sp - 2; i >= shiftStart; i--)
            {
                _stack[i + 1] = _stack[i];
            }

            _stack[shiftStart] = StashValue.FromObject(extBound.Receiver);

            int provided = argc + 1; // user args + self
            int expected = fnChunk.Arity;
            int minArity = fnChunk.MinArity;

            if (fnChunk.HasRestParam)
            {
                int nonRestCount = expected - 1;
                int minRequired = Math.Min(minArity, nonRestCount);
                if (provided < minRequired)
                {
                    throw new RuntimeError($"Expected at least {minRequired - 1} arguments but got {argc}.", callSpan ?? _context.CurrentSpan);
                }

                if (provided < nonRestCount)
                {
                    for (int i = provided; i < nonRestCount; i++)
                    {
                        Push(StashValue.FromObj(NotProvided));
                    }

                    provided = nonRestCount;
                }

                int restCount = Math.Max(0, provided - nonRestCount);
                var restList = new List<StashValue>(restCount);
                int restStart = _sp - restCount;
                for (int i = restStart; i < _sp; i++)
                {
                    restList.Add(_stack[i]);
                }

                _sp = restStart;
                Push(StashValue.FromObj(restList));
                provided = expected;
            }
            else
            {
                if (provided < minArity || provided > expected)
                {
                    string expectedStr = minArity == expected
                        ? $"{expected - 1}"
                        : $"{minArity - 1} to {expected - 1}";
                    throw new RuntimeError($"Expected {expectedStr} arguments but got {argc}.", callSpan ?? _context.CurrentSpan);
                }

                if (provided < expected)
                {
                    for (int i = provided; i < expected; i++)
                    {
                        Push(StashValue.FromObj(NotProvided));
                    }
                }
            }

            int baseSlot = _sp - fnChunk.Arity;

            if (fnChunk.IsAsync)
            {
                Push(StashValue.FromObject(SpawnAsyncFunction(fnChunk, extFn.Upvalues, baseSlot, callSpan ?? _context.CurrentSpan, extFn.ModuleGlobals)));
                return;
            }

            PushFrame(fnChunk, baseSlot, extFn.Upvalues, fnChunk.Name, extFn.ModuleGlobals);
            return;
        }

        if (callee is IStashCallable callable)
        {
            // Arity checking for IStashCallable
            if (callable.Arity != -1)
            {
                int minArity = callable.MinArity;
                if (argc < minArity || argc > callable.Arity)
                {
                    string expectedStr = minArity == callable.Arity
                        ? $"{callable.Arity}"
                        : $"{minArity} to {callable.Arity}";
                    throw new RuntimeError(
                        $"Expected {expectedStr} arguments but got {argc}.",
                        callSpan ?? _context.CurrentSpan);
                }
            }

            int argStart = _sp - argc;

            // Pass a span directly into the VM stack — zero allocation, zero copying
            ReadOnlySpan<StashValue> argSpan = _stack.AsSpan(argStart, argc);

            _sp = argStart - 1; // pop args + callee slot

            StashValue result;
            try
            {
                if (callSpan is not null) _context.CurrentSpan = callSpan;
                result = callable.CallDirect(_context, argSpan);
            }
            catch (Exception ex) when (ex is not RuntimeError and not OperationCanceledException and not Stash.Tpl.TemplateException)
            {
                throw new RuntimeError($"Built-in function error: {ex.Message}",
                    callSpan ?? _context.CurrentSpan);
            }
            Push(result);
            return;
        }

        throw new RuntimeError(
            $"Can only call functions. Got {RuntimeValues.Stringify(callee)}.",
            callSpan ?? _context.CurrentSpan);
    }

    /// <summary>
    /// StashValue-native overload. Calls a <see cref="VMFunction"/> closure inline on the
    /// same VM instance, pushing StashValue args directly without boxing.
    /// </summary>
    internal StashValue ExecuteVMFunctionInlineDirect(VMFunction fn, ReadOnlySpan<StashValue> args, SourceSpan? span)
    {
        int savedSp = _sp;
        int savedFrameCount = _frameCount;
        Push(StashValue.FromObj(fn)); // callee slot
        foreach (StashValue arg in args)
        {
            Push(arg);
        }

        CallValue(fn, args.Length, span);
        try
        {
            object? result = RunUntilFrame(savedFrameCount);
            return StashValue.FromObject(result);
        }
        finally
        {
            if (_frameCount > savedFrameCount)
            {
                CloseUpvalues(savedSp);
                _frameCount = savedFrameCount;
            }
            _sp = savedSp;
        }
    }

    /// <summary>
    /// StashValue-native overload. Calls a <see cref="VMFunction"/> closure on this VM instance
    /// with a fresh execution state. Used for background-thread callbacks.
    /// </summary>
    internal StashValue CallClosureDirect(VMFunction fn, ReadOnlySpan<StashValue> args)
    {
        _sp = 0;
        _frameCount = 0;
        StepCount = 0;
        _exceptionHandlers.Clear();
        _openUpvalues.Clear();
        Push(StashValue.FromObj(fn));
        foreach (StashValue arg in args)
        {
            Push(arg);
        }

        CallValue(fn, args.Length, null);
        object? result = Run();
        return StashValue.FromObject(result);
    }

    /// <summary>
    /// Validates argument count and handles rest parameters and default argument padding
    /// in the register window starting at newBase.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ValidateAndPadArgsRegWindow(
        Chunk fnChunk, int provided, int newBase,
        SourceSpan? callSpan, int arityDisplayAdjust = 0)
    {
        int expected = fnChunk.Arity;
        int minArity = fnChunk.MinArity;

        if (fnChunk.HasRestParam)
        {
            int nonRestCount = expected - 1;
            int minRequired = Math.Min(minArity, nonRestCount);

            if (provided < minRequired)
                throw new RuntimeError(
                    $"Expected at least {minRequired - arityDisplayAdjust} arguments but got {provided - arityDisplayAdjust}.",
                    callSpan);

            if (provided < nonRestCount)
            {
                for (int i = provided; i < nonRestCount; i++)
                    _stack[newBase + i] = StashValue.FromObj(NotProvided);
                provided = nonRestCount;
            }

            int restCount = Math.Max(0, provided - nonRestCount);
            var restList = new List<StashValue>(restCount);
            for (int i = nonRestCount; i < provided; i++)
                restList.Add(_stack[newBase + i]);
            _stack[newBase + nonRestCount] = StashValue.FromObj(restList);
        }
        else
        {
            if (provided < minArity || provided > expected)
            {
                string expectedStr = minArity == expected
                    ? $"{expected - arityDisplayAdjust}"
                    : $"{minArity - arityDisplayAdjust} to {expected - arityDisplayAdjust}";
                throw new RuntimeError(
                    $"Expected {expectedStr} arguments but got {provided - arityDisplayAdjust}.",
                    callSpan);
            }

            if (provided < expected)
            {
                for (int i = provided; i < expected; i++)
                    _stack[newBase + i] = StashValue.FromObj(NotProvided);
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ExecuteCall<TDebugMode>(ref CallFrame frame, uint inst) where TDebugMode : struct
    {
        byte a = Instruction.GetA(inst);
        byte argc = Instruction.GetC(inst);
        int @base = frame.BaseSlot;

        // Save caller context before PushFrame may reallocate _frames.
        SourceMap callerSourceMap = frame.Chunk.SourceMap;
        int callerIP = frame.IP - 1;
        object? callee = _stack[@base + a].AsObj;
        int prevFrameCount = _frameCount;

        if (callee is VMFunction fn)
        {
            Chunk fnChunk = fn.Chunk;
            int newBase = @base + a + 1;

            // Fast path: exact arity, no rest, not async — most common case.
            if (argc == fnChunk.Arity && !fnChunk.HasRestParam && !fnChunk.IsAsync)
            {
                PushFrame(fnChunk, newBase, fn.Upvalues, fnChunk.Name, fn.ModuleGlobals);
            }
            else
            {
                ValidateAndPadArgsRegWindow(fnChunk, argc, newBase, callerSourceMap.GetSpan(callerIP));

                if (fnChunk.IsAsync)
                    _stack[@base + a] = StashValue.FromObject(SpawnAsyncFunction(
                        fnChunk, fn.Upvalues, newBase, callerSourceMap.GetSpan(callerIP), fn.ModuleGlobals));
                else
                    PushFrame(fnChunk, newBase, fn.Upvalues, fnChunk.Name, fn.ModuleGlobals);
            }
        }
        else if (callee is VMBoundMethod bound)
        {
            VMFunction boundFn = bound.Function;
            Chunk fnChunk = boundFn.Chunk;
            int newBase = @base + a + 1;

            // Ensure stack has room for the shifted args + inserted self slot.
            int needed = newBase + argc + 1;
            while (needed >= _stack.Length) GrowStack();

            // Shift args right by 1 to insert self at R(0) of the callee frame.
            for (int i = argc - 1; i >= 0; i--)
                _stack[newBase + i + 1] = _stack[newBase + i];
            _stack[newBase] = StashValue.FromObj(bound.Instance);

            ValidateAndPadArgsRegWindow(fnChunk, argc + 1, newBase, callerSourceMap.GetSpan(callerIP), 1);

            if (fnChunk.IsAsync)
                _stack[@base + a] = StashValue.FromObject(SpawnAsyncFunction(
                    fnChunk, boundFn.Upvalues, newBase, callerSourceMap.GetSpan(callerIP), boundFn.ModuleGlobals));
            else
                PushFrame(fnChunk, newBase, boundFn.Upvalues, fnChunk.Name, boundFn.ModuleGlobals);
        }
        else if (callee is VMExtensionBoundMethod extBound)
        {
            VMFunction extFn = extBound.Function;
            Chunk fnChunk = extFn.Chunk;
            int newBase = @base + a + 1;

            int needed = newBase + argc + 1;
            while (needed >= _stack.Length) GrowStack();

            for (int i = argc - 1; i >= 0; i--)
                _stack[newBase + i + 1] = _stack[newBase + i];
            _stack[newBase] = StashValue.FromObject(extBound.Receiver);

            ValidateAndPadArgsRegWindow(fnChunk, argc + 1, newBase, callerSourceMap.GetSpan(callerIP), 1);

            if (fnChunk.IsAsync)
                _stack[@base + a] = StashValue.FromObject(SpawnAsyncFunction(
                    fnChunk, extFn.Upvalues, newBase, callerSourceMap.GetSpan(callerIP), extFn.ModuleGlobals));
            else
                PushFrame(fnChunk, newBase, extFn.Upvalues, fnChunk.Name, extFn.ModuleGlobals);
        }
        else if (callee is BuiltInFunction builtIn)
        {
            // Fast path for built-ins: args are already in the register window.
            if (builtIn.Arity != -1 && argc != builtIn.Arity)
                throw new RuntimeError(
                    $"Expected {builtIn.Arity} arguments but got {argc}.",
                    callerSourceMap.GetSpan(callerIP));

            _context.CallSourceMap = callerSourceMap;
            _context.CallIP = callerIP;
            _context._currentSpan = null;

            ReadOnlySpan<StashValue> argSpan = _stack.AsSpan(@base + a + 1, argc);
            StashValue result;
            try
            {
                result = builtIn.CallDirect(_context, argSpan);
            }
            catch (Exception ex) when (ex is not RuntimeError and not OperationCanceledException and not Stash.Tpl.TemplateException)
            {
                throw new RuntimeError($"Built-in function error: {ex.Message}", _context.CurrentSpan);
            }
            _stack[@base + a] = result;
        }
        else if (callee is IStashCallable callable)
        {
            if (callable.Arity != -1)
            {
                int minArity = callable.MinArity;
                if (argc < minArity || argc > callable.Arity)
                {
                    string expectedStr = minArity == callable.Arity ? $"{callable.Arity}" : $"{minArity} to {callable.Arity}";
                    throw new RuntimeError(
                        $"Expected {expectedStr} arguments but got {argc}.",
                        callerSourceMap.GetSpan(callerIP));
                }
            }

            ReadOnlySpan<StashValue> argSpan = _stack.AsSpan(@base + a + 1, argc);
            StashValue result;
            try
            {
                _context.CurrentSpan = callerSourceMap.GetSpan(callerIP);
                result = callable.CallDirect(_context, argSpan);
            }
            catch (Exception ex) when (ex is not RuntimeError and not OperationCanceledException and not Stash.Tpl.TemplateException)
            {
                throw new RuntimeError($"Built-in function error: {ex.Message}", _context.CurrentSpan);
            }
            _stack[@base + a] = result;
        }
        else
        {
            throw new RuntimeError(
                $"Can only call functions. Got {RuntimeValues.Stringify(callee)}.",
                callerSourceMap.GetSpan(callerIP));
        }

        if (StepLimit > 0 && ++StepCount >= StepLimit)
            throw new Stash.Runtime.StepLimitExceededException(StepLimit);

        if (typeof(TDebugMode) == typeof(DebugOn) && _frameCount > prevFrameCount)
        {
            IDebugger debugger = _debugger!;
            SourceSpan? callSpan = callerSourceMap.GetSpan(callerIP);
            ref CallFrame newFrame = ref _frames[_frameCount - 1];
            IDebugScope scope = BuildFrameScope(ref newFrame);
            string funcName = newFrame.FunctionName ?? "<anonymous>";
            _debugCallStack.Add(new DebugCallFrame
            {
                FunctionName = funcName,
                CallSite = callSpan,
                LocalScope = scope,
            });
            if (callSpan is not null && debugger.ShouldBreakOnFunctionEntry(funcName))
                debugger.OnFunctionEnter(funcName, callSpan.Value, scope, _debugThreadId);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ExecuteCallBuiltIn<TDebugMode>(ref CallFrame frame, uint inst) where TDebugMode : struct
    {
        byte a = Instruction.GetA(inst);
        byte b = Instruction.GetB(inst);
        byte argc = Instruction.GetC(inst);
        int icIdx = (int)frame.Chunk.Code[frame.IP++]; // read companion word
        int @base = frame.BaseSlot;

        ref ICSlot ic = ref frame.Chunk.ICSlots![icIdx];

        // IC fast path: cached BuiltInFunction
        if (ic.State == 1 && _stack[@base + b].AsObj == ic.Guard)
        {
            var builtIn = (BuiltInFunction)ic.CachedValue.AsObj!;

            _context.CallSourceMap = frame.Chunk.SourceMap;
            _context.CallIP = frame.IP - 2; // points to CallBuiltIn, not companion
            _context._currentSpan = null;

            ReadOnlySpan<StashValue> argSpan = _stack.AsSpan(@base + a + 1, argc);
            StashValue result;
            try
            {
                result = builtIn.CallDirect(_context, argSpan);
            }
            catch (Exception ex) when (ex is not RuntimeError and not OperationCanceledException and not Stash.Tpl.TemplateException)
            {
                throw new RuntimeError($"Built-in function error: {ex.Message}", _context.CurrentSpan);
            }
            _stack[@base + a] = result;
            return;
        }

        // IC miss: fall back to full GetField + Call
        ExecuteCallBuiltInSlow<TDebugMode>(ref frame, a, b, argc, icIdx);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ExecuteCallBuiltInSlow<TDebugMode>(ref CallFrame frame, byte a, byte b, byte argc, int icIdx) where TDebugMode : struct
    {
        int @base = frame.BaseSlot;
        ref ICSlot ic = ref frame.Chunk.ICSlots![icIdx];
        string fieldName = (string)frame.Chunk.Constants[ic.ConstantIndex].AsObj!;
        StashValue objVal = _stack[@base + b];

        // Resolve the function via GetField logic
        StashValue calleeVal;
        if (objVal.Tag == StashValueTag.Obj && objVal.AsObj is StashNamespace ns)
        {
            calleeVal = ns.GetMemberValue(fieldName, null);
        }
        else
        {
            object? obj = objVal.ToObject();
            object? rawResult = GetFieldValue(obj, fieldName, GetCurrentSpan(ref frame));
            if (rawResult is StashBoundMethod bound && bound.Method is VMFunction vmFunc)
                rawResult = new VMBoundMethod(bound.Instance, vmFunc);
            calleeVal = StashValue.FromObject(rawResult);
        }

        // Store callee in R(A) and dispatch via normal Call logic
        _stack[@base + a] = calleeVal;

        // Build a synthetic Call instruction: Call R(a), 0, argc
        uint callInst = Instruction.EncodeABC(OpCode.Call, a, 0, argc);
        ExecuteCall<TDebugMode>(ref frame, callInst);

        // Populate IC if the callee is a BuiltInFunction from a frozen namespace
        if (ic.State == 0 && calleeVal.Tag == StashValueTag.Obj && calleeVal.AsObj is BuiltInFunction)
        {
            if (objVal.Tag == StashValueTag.Obj && objVal.AsObj is StashNamespace ns2 && ns2.IsFrozen)
            {
                ic.Guard = ns2;
                ic.CachedValue = calleeVal;
                ic.State = 1;
            }
        }
        else if (ic.State == 1)
        {
            ic.State = 2; // Megamorphic
        }
    }

    private void ExecuteCallSpread<TDebugMode>(ref CallFrame frame, uint inst) where TDebugMode : struct
    {
        byte a = Instruction.GetA(inst);
        byte argc = Instruction.GetB(inst);   // B = arg count (with spread markers)
        int @base = frame.BaseSlot;

        SourceMap callerSourceMap = frame.Chunk.SourceMap;
        int callerIP = frame.IP - 1;

        // Build flat arg list, expanding any SpreadMarker values.
        var flatArgs = new List<StashValue>();
        for (int i = 0; i < argc; i++)
        {
            StashValue arg = _stack[@base + a + 1 + i];
            if (arg.IsObj && arg.AsObj is SpreadMarker sm)
            {
                if (sm.Items is List<StashValue> items)
                {
                    flatArgs.AddRange(items);
                }
                else
                {
                    throw new RuntimeError(
                        $"Spread argument must be an array, got {RuntimeValues.Stringify(sm.Items)}.",
                        GetCurrentSpan(ref frame));
                }
            }
            else
                flatArgs.Add(arg);
        }

        object? callee = _stack[@base + a].AsObj;
        int savedSp = _sp;
        int prevFrameCount = _frameCount;

        // Position _sp so the callee slot lands at _stack[@base+a].
        // This ensures: newFrame.BaseSlot = @base+a+1, so BaseSlot-1 = @base+a.
        // ExecuteReturn will write the result to _stack[BaseSlot-1] = _stack[@base+a]. ✓
        _sp = @base + a;
        Push(StashValue.FromObj(callee!));
        foreach (StashValue arg in flatArgs) Push(arg);

        CallValue(callee, flatArgs.Count, callerSourceMap.GetSpan(callerIP));

        if (_frameCount == prevFrameCount)
        {
            // CallValue handled it inline (IStashCallable/BuiltIn).
            // The result was pushed at _stack[@base+a] by CallValue's internal Push.
            _sp = savedSp;
        }
        // If a frame was pushed, ExecuteReturn writes result to _stack[BaseSlot-1] == _stack[@base+a]
        // and restores _sp to callerFrame.BaseSlot + callerFrame.Chunk.MaxRegs == savedSp.

        if (StepLimit > 0 && ++StepCount >= StepLimit)
            throw new Stash.Runtime.StepLimitExceededException(StepLimit);

        if (typeof(TDebugMode) == typeof(DebugOn) && _frameCount > prevFrameCount)
        {
            IDebugger debugger = _debugger!;
            SourceSpan? callSpan = callerSourceMap.GetSpan(callerIP);
            ref CallFrame newFrame = ref _frames[_frameCount - 1];
            IDebugScope scope = BuildFrameScope(ref newFrame);
            string funcName = newFrame.FunctionName ?? "<anonymous>";
            _debugCallStack.Add(new DebugCallFrame
            {
                FunctionName = funcName,
                CallSite = callSpan,
                LocalScope = scope,
            });
            if (callSpan is not null && debugger.ShouldBreakOnFunctionEntry(funcName))
                debugger.OnFunctionEnter(funcName, callSpan.Value, scope, _debugThreadId);
        }
    }

    private bool ExecuteReturn<TDebugMode>(ref CallFrame frame, uint inst, int targetFrameCount, out object? result) where TDebugMode : struct
    {
        byte a = Instruction.GetA(inst);
        byte b = Instruction.GetB(inst);
        StashValue retVal = b != 0 ? _stack[frame.BaseSlot + a] : StashValue.Null;
        int baseSlot = frame.BaseSlot;

        if (typeof(TDebugMode) == typeof(DebugOn) && _debugCallStack.Count > 0)
        {
            string funcName = frame.FunctionName ?? "<anonymous>";
            _debugCallStack.RemoveAt(_debugCallStack.Count - 1);
            _debugger!.OnFunctionExit(funcName, _debugThreadId);
        }

        if (frame.Chunk.MayHaveCapturedLocals)
            CloseUpvalues(baseSlot);

        // Execute defers in LIFO order before popping the frame
        if (frame.Defers is { Count: > 0 })
        {
            var defers = frame.Defers;
            frame.Defers = null; // Prevent re-execution during exception unwinding
            retVal = ExecuteDefers<TDebugMode>(defers, baseSlot, frame.Chunk.MaxRegs, retVal);
        }

        _frameCount--;

        if (_frameCount == 0)
        {
            _sp = 0;
            result = retVal.ToObject();
            return true;
        }

        // Write return value to caller's R(A) = the slot just below our BaseSlot.
        _stack[baseSlot - 1] = retVal;

        // Restore _sp to the caller's register window.
        ref CallFrame caller = ref _frames[_frameCount - 1];
        _sp = caller.BaseSlot + caller.Chunk.MaxRegs;

        if (_frameCount <= targetFrameCount)
        {
            result = retVal.ToObject();
            return true;
        }

        result = null;
        return false;
    }

    /// <summary>
    /// Executes deferred closures in LIFO order. Called from ExecuteReturn during normal function exit.
    /// </summary>
    private StashValue ExecuteDefers<TDebugMode>(List<StashValue> defers, int frameBaseSlot, int frameMaxRegs, StashValue returnValue) where TDebugMode : struct
    {
        RuntimeError? firstError = null;
        List<StashError>? suppressed = null;

        for (int i = defers.Count - 1; i >= 0; i--)
        {
            try
            {
                // Restore stack pointer to the frame's register window
                _sp = frameBaseSlot + frameMaxRegs;

                // Push the defer closure and call it
                StashValue closure = defers[i];
                _stack[_sp] = closure;
                _sp++;
                CallValue(closure.AsObj!, 0, null);

                // Run the defer closure to completion (handles try-catch internally)
                RunDeferClosure(_frameCount - 1);
            }
            catch (RuntimeError ex)
            {
                if (firstError == null)
                    firstError = ex;
                else
                    (suppressed ??= new()).Add(
                        new StashError(ex.Message, ex.ErrorType ?? "RuntimeError", null, ex.Properties));
            }
        }

        if (firstError != null)
        {
            if (suppressed != null)
            {
                throw new RuntimeError(firstError.Message, firstError.Span, firstError.ErrorType)
                {
                    Properties = firstError.Properties,
                    SuppressedErrors = suppressed,
                };
            }
            throw firstError;
        }

        return returnValue;
    }

    /// <summary>
    /// Runs a defer closure to completion, handling any internal try-catch blocks.
    /// Stops when frame count drops to targetFrameCount.
    /// </summary>
    private void RunDeferClosure(int targetFrameCount)
    {
        while (true)
        {
            try
            {
                RunInner<DebugOff>(targetFrameCount);
                return;
            }
            catch (RuntimeError ex) when (_exceptionHandlers.Count > 0 && _exceptionHandlers[^1].FrameIndex >= targetFrameCount)
            {
                // Exception handler is within the defer closure — handle it internally
                ExceptionHandler handler = _exceptionHandlers[^1];
                _exceptionHandlers.RemoveAt(_exceptionHandlers.Count - 1);
                CloseUpvalues(handler.StackLevel);
                _frameCount = handler.FrameIndex + 1;
                _sp = handler.StackLevel;
                var stashError = new StashError(ex.Message, ex.ErrorType ?? "RuntimeError", null, ex.Properties);
                _context.LastError = stashError;
                ref CallFrame hf = ref _frames[_frameCount - 1];
                _stack[hf.BaseSlot + handler.ErrorReg] = StashValue.FromObj(stashError);
                hf.IP = handler.CatchIP;
                // Continue loop: RunInner resumes at the catch handler
            }
            // No matching handler within the defer — let the error propagate
        }
    }

    /// <summary>
    /// Executes defers for a frame being unwound during exception handling.
    /// Errors from defers are collected as suppressed, not propagated.
    /// </summary>
    private void RunFrameDefers(ref CallFrame frame, ref List<StashError>? suppressed)
    {
        var defers = frame.Defers!;
        frame.Defers = null; // Prevent re-execution

        for (int i = defers.Count - 1; i >= 0; i--)
        {
            try
            {
                _sp = frame.BaseSlot + frame.Chunk.MaxRegs;
                StashValue closure = defers[i];
                _stack[_sp] = closure;
                _sp++;
                CallValue(closure.AsObj!, 0, null);
                RunDeferClosure(_frameCount - 1);
            }
            catch (RuntimeError deferEx)
            {
                (suppressed ??= new()).Add(
                    new StashError(deferEx.Message, deferEx.ErrorType ?? "RuntimeError", null, deferEx.Properties));
            }
        }
    }

}
