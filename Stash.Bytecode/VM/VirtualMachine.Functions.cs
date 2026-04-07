using System;
using System.Collections.Generic;
using Stash.Common;
using Stash.Debugging;
using Stash.Runtime;
using Stash.Runtime.Types;

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
            catch (Exception ex) when (ex is not RuntimeError and not Stash.Tpl.TemplateException)
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

    private bool ExecuteReturn(ref CallFrame frame, int targetFrameCount, IDebugger? debugger, out object? result)
    {
        StashValue retVal = Pop();
        int baseSlot = _frames[_frameCount - 1].BaseSlot;
        if (debugger is not null && _debugCallStack.Count > 0)
        {
            string funcName = _frames[_frameCount - 1].FunctionName ?? "<anonymous>";
            _debugCallStack.RemoveAt(_debugCallStack.Count - 1);
            debugger.OnFunctionExit(funcName, _debugThreadId);
        }
        CloseUpvalues(baseSlot);
        _frameCount--;
        if (_frameCount == 0)
        {
            _sp = 0;
            result = retVal.ToObject();
            return true;
        }
        _sp = baseSlot - 1;
        Push(retVal);
        if (_frameCount <= targetFrameCount)
        {
            result = retVal.ToObject();
            return true;
        }
        result = null;
        return false;
    }

    private void ExecuteCall(ref CallFrame frame, IDebugger? debugger)
    {
        byte argc = ReadByte(ref frame);
        // Save caller context before frame may be invalidated by PushFrame.
        // Avoid the O(log n) binary search — only compute span when actually needed.
        int callerIP = frame.IP - 1;
        SourceMap callerSourceMap = frame.Chunk.SourceMap;
        object? callee = _stack[_sp - argc - 1].AsObj;  // Callees are always Obj-tagged
        int prevFrameCount = _frameCount;

        // Fast path: VMFunction calls skip span lookup on the success path
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
                        callerSourceMap.GetSpan(callerIP));
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
                        ? $"{expected}"
                        : $"{minArity} to {expected}";
                    throw new RuntimeError(
                        $"Expected {expectedStr} arguments but got {provided}.",
                        callerSourceMap.GetSpan(callerIP));
                }

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
                Push(StashValue.FromObject(SpawnAsyncFunction(fnChunk, fn.Upvalues, baseSlot, callerSourceMap.GetSpan(callerIP), fn.ModuleGlobals)));
            }
            else
            {
                PushFrame(fnChunk, baseSlot, fn.Upvalues, fnChunk.Name, fn.ModuleGlobals);
            }
        }
        else if (callee is BuiltInFunction builtIn)
        {
            // Fast path: built-in function — skip CallValue dispatch chain
            _context._callSourceMap = callerSourceMap;
            _context._callIP = callerIP;
            _context._currentSpan = null;

            if (builtIn.Arity != -1)
            {
                if (argc != builtIn.Arity)
                {
                    throw new RuntimeError(
                        $"Expected {builtIn.Arity} arguments but got {argc}.",
                        _context.CurrentSpan);
                }
            }

            int argStart = _sp - argc;
            ReadOnlySpan<StashValue> argSpan = _stack.AsSpan(argStart, argc);
            _sp = argStart - 1; // pop args + callee slot

            StashValue result;
            try
            {
                result = builtIn.CallDirect(_context, argSpan);
            }
            catch (Exception ex) when (ex is not RuntimeError and not Stash.Tpl.TemplateException)
            {
                throw new RuntimeError($"Built-in function error: {ex.Message}",
                    _context.CurrentSpan);
            }
            Push(result);
        }
        else
        {
            // Remaining cases: VMBoundMethod, VMExtensionBoundMethod, lambdas, etc.
            _context._callSourceMap = callerSourceMap;
            _context._callIP = callerIP;
            _context._currentSpan = null;
            CallValue(callee, argc, null);
        }

        if (StepLimit > 0 && ++StepCount >= StepLimit)
        {
            throw new Stash.Runtime.StepLimitExceededException(StepLimit);
        }

        // Debug: track function entry for VM function calls
        if (debugger is not null && _frameCount > prevFrameCount)
        {
            SourceSpan? callSpan = callerSourceMap.GetSpan(callerIP);
            ref CallFrame newFrame = ref _frames[_frameCount - 1];
            IDebugScope scope = BuildFrameScope(ref newFrame);
            string funcName = newFrame.FunctionName ?? "<anonymous>";

            _debugCallStack.Add(new Stash.Debugging.CallFrame
            {
                FunctionName = funcName,
                CallSite = callSpan,
                LocalScope = scope,
            });

            if (debugger.ShouldBreakOnFunctionEntry(funcName))
            {
                debugger.OnFunctionEnter(funcName, callSpan!.Value, scope, _debugThreadId);
            }
        }
    }

    private void ExecuteClosure(ref CallFrame frame)
    {
        ushort chunkIdx = ReadU16(ref frame);
        Chunk fnChunk = (Chunk)frame.Chunk.Constants[chunkIdx].AsObj!;
        var upvalues = new Upvalue[fnChunk.Upvalues.Length];
        for (int i = 0; i < fnChunk.Upvalues.Length; i++)
        {
            byte isLocal = frame.Chunk.Code[frame.IP++];
            byte index = frame.Chunk.Code[frame.IP++];
            if (isLocal == 1)
            {
                upvalues[i] = CaptureUpvalue(frame.BaseSlot + index);
            }
            else
            {
                upvalues[i] = frame.Upvalues![index];
            }
        }
        Push(StashValue.FromObj(new VMFunction(fnChunk, upvalues) { ModuleGlobals = _globals }));
    }

}
