using System;
using System.Collections.Generic;
using Stash.Common;
using Stash.Debugging;
using Stash.Runtime;
using Stash.Runtime.Types;
using DebugCallFrame = Stash.Debugging.CallFrame;

namespace Stash.Bytecode;

/// <summary>
/// Debugger integration: scope building, debug execution modes.
/// </summary>
public sealed partial class VirtualMachine
{
    private object? RunUntilFrameDebug(int targetFrameCount)
    {
        IDebugger debugger = _debugger!;

        while (true)
        {
            try
            {
                return RunInner<DebugOn>(targetFrameCount);
            }
            catch (RuntimeError ex) when (_exceptionHandlers.Count > 0 && _exceptionHandlers[^1].FrameIndex >= targetFrameCount)
            {
                ExceptionHandler handler = _exceptionHandlers[^1];
                _exceptionHandlers.RemoveAt(_exceptionHandlers.Count - 1);
                CloseUpvalues(handler.StackLevel);
                _frameCount = handler.FrameIndex + 1;
                while (_debugCallStack.Count > handler.FrameIndex)
                {
                    _debugCallStack.RemoveAt(_debugCallStack.Count - 1);
                }

                _sp = handler.StackLevel;
                var stashError = new StashError(ex.Message, ex.ErrorType ?? "RuntimeError", null, ex.Properties);
                _context.LastError = stashError;
                ref CallFrame hf1 = ref _frames[_frameCount - 1];
                _stack[hf1.BaseSlot + handler.ErrorReg] = StashValue.FromObj(stashError);
                hf1.IP = handler.CatchIP;
            }
            catch (RuntimeError ex)
            {
                if (debugger.ShouldBreakOnException(ex))
                {
                    SourceSpan? span = (_frameCount > 0) ? GetCurrentSpan(ref _frames[_frameCount - 1]) : null;
                    if (span is not null)
                    {
                        IDebugScope scope = (_frameCount > 0)
                            ? BuildFrameScope(ref _frames[_frameCount - 1])
                            : BuildGlobalScope();
                        debugger.OnBeforeExecute(span.Value, scope, _debugThreadId);
                    }
                }
                debugger.OnError(ex, _debugCallStack, _debugThreadId);
                throw;
            }
        }
    }

    /// <summary>
    /// Builds an <see cref="IDebugScope"/> from a VM call frame for debugger inspection.
    /// Creates a live-stack scope — reads and writes go directly to the VM's value stack.
    /// </summary>
    private IDebugScope BuildFrameScope(ref CallFrame frame)
    {
        // At the top-level (no function calls on the debug call stack), top-level
        // variables are accessed via LoadGlobal/StoreGlobal and live in _globals.
        // The local stack slots are only set at declaration time and go stale
        // immediately after the first assignment through StoreGlobal.  Returning the
        // global scope directly gives the debugger accurate, up-to-date values.
        if (_debugCallStack.Count == 0)
        {
            return BuildGlobalScope();
        }

        Chunk chunk = frame.Chunk;
        IDebugScope enclosing = BuildGlobalScope();

        // If present, insert a closure scope between local and global
        if (frame.Upvalues is not null && frame.Upvalues.Length > 0)
        {
            string[]? upvalueNames = chunk.UpvalueNames;
            var closureBindings = new KeyValuePair<string, object?>[frame.Upvalues.Length];
            for (int i = 0; i < frame.Upvalues.Length; i++)
            {
                string name = upvalueNames is not null && i < upvalueNames.Length
                    ? upvalueNames[i] : $"upvalue_{i}";
                object? value = frame.Upvalues[i].Value.ToObject();
                closureBindings[i] = new KeyValuePair<string, object?>(name, value);
            }
            enclosing = VMDebugScope.FromSnapshot(closureBindings, enclosing);
        }

        int activeLocalCount = Math.Min(chunk.LocalNames?.Length ?? 0, Math.Max(0, _sp - frame.BaseSlot));
        return VMDebugScope.FromStack(
            _stack, frame.BaseSlot, activeLocalCount,
            chunk.LocalNames, chunk.LocalIsConst, enclosing);
    }

    /// <summary>
    /// Builds an <see cref="IDebugScope"/> wrapping the global variables.
    /// </summary>
    internal IDebugScope BuildGlobalScope()
    {
        return VMDebugScope.FromGlobals(_globals, _constGlobals, null);
    }


    private object? RunDebug()
    {
        IDebugger debugger = _debugger!;

        while (true)
        {
            try
            {
                return RunInner<DebugOn>(0);
            }
            catch (ExitException ex)
            {
                // Defer-aware exit: run all pending defers on every frame (LIFO), then terminate.
                List<StashError>? suppressed = null;
                for (int i = _frameCount - 1; i >= 0; i--)
                {
                    ref CallFrame frame = ref _frames[i];
                    if (frame.Defers is { Count: > 0 })
                    {
                        RunFrameDefers(ref frame, ref suppressed);
                    }
                }

                _frameCount = 0;
                _sp = 0;
                _exceptionHandlers.Clear();

                if (suppressed is { Count: > 0 })
                {
                    ex.SuppressedErrors = suppressed;
                }

                if (_context.EmbeddedMode)
                {
                    throw;
                }

                if (suppressed is { Count: > 0 })
                {
                    foreach (var err in suppressed)
                    {
                        try { _context.ErrorOutput.WriteLine($"[exit defer error] {err.Type}: {err.Message}"); }
                        catch { /* best effort */ }
                    }
                }

                System.Environment.Exit(ex.ExitCode);
                return null; // unreachable
            }
            catch (RuntimeError ex) when (_exceptionHandlers.Count > 0)
            {
                ExceptionHandler handler = _exceptionHandlers[^1];
                _exceptionHandlers.RemoveAt(_exceptionHandlers.Count - 1);

                // Execute defers for frames being unwound BEFORE closing upvalues
                List<StashError>? suppressed = null;
                for (int i = _frameCount - 1; i > handler.FrameIndex; i--)
                {
                    ref CallFrame unwoundFrame = ref _frames[i];
                    if (unwoundFrame.Defers is { Count: > 0 })
                    {
                        RunFrameDefers(ref unwoundFrame, ref suppressed);
                    }
                }

                CloseUpvalues(handler.StackLevel);
                _frameCount = handler.FrameIndex + 1;
                while (_debugCallStack.Count > handler.FrameIndex)
                {
                    _debugCallStack.RemoveAt(_debugCallStack.Count - 1);
                }

                _sp = handler.StackLevel;

                // Merge suppressed errors from the exception itself (e.g. from ExecuteDefers
                // when multiple defers throw) with any collected during frame unwinding.
                if (ex.SuppressedErrors is { Count: > 0 })
                    (suppressed ??= new()).AddRange(ex.SuppressedErrors);

                var stashError = new StashError(ex.Message, ex.ErrorType ?? "RuntimeError", null, ex.Properties);
                if (suppressed != null)
                    stashError.Suppressed = suppressed;
                _context.LastError = stashError;
                ref CallFrame hf2 = ref _frames[_frameCount - 1];
                _stack[hf2.BaseSlot + handler.ErrorReg] = StashValue.FromObj(stashError);
                hf2.IP = handler.CatchIP;
            }
            catch (RuntimeError ex)
            {
                // Uncaught error — notify debugger
                if (debugger.ShouldBreakOnException(ex))
                {
                    SourceSpan? span = (_frameCount > 0) ? GetCurrentSpan(ref _frames[_frameCount - 1]) : null;
                    if (span is not null)
                    {
                        IDebugScope scope = (_frameCount > 0)
                            ? BuildFrameScope(ref _frames[_frameCount - 1])
                            : BuildGlobalScope();
                        debugger.OnBeforeExecute(span.Value, scope, _debugThreadId);
                    }
                }
                debugger.OnError(ex, _debugCallStack, _debugThreadId);
                throw;
            }
        }
    }

    // ------------------------------------------------------------------
    // Execution helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Runs the dispatch loop until <see cref="_frameCount"/> drops to
    /// <paramref name="targetFrameCount"/>. Handles exceptions the same
    /// way as the outer <see cref="Run"/> loop.
    /// </summary>
    private object? RunUntilFrame(int targetFrameCount)
    {
        if (_debugger is not null)
        {
            return RunUntilFrameDebug(targetFrameCount);
        }

        while (true)
        {
            try
            {
                return RunInner<DebugOff>(targetFrameCount);
            }
            catch (RuntimeError ex) when (_exceptionHandlers.Count > 0 && _exceptionHandlers[^1].FrameIndex >= targetFrameCount)
            {
                ExceptionHandler handler = _exceptionHandlers[^1];
                _exceptionHandlers.RemoveAt(_exceptionHandlers.Count - 1);
                CloseUpvalues(handler.StackLevel);
                _frameCount = handler.FrameIndex + 1;
                _sp = handler.StackLevel;
                var stashError = new StashError(ex.Message, ex.ErrorType ?? "RuntimeError", null, ex.Properties);
                _context.LastError = stashError;
                ref CallFrame handlerFrame = ref _frames[_frameCount - 1];
                _stack[handlerFrame.BaseSlot + handler.ErrorReg] = StashValue.FromObj(stashError);
                handlerFrame.IP = handler.CatchIP;
            }
        }
    }

}
