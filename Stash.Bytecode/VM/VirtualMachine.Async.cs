using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Stash.Common;
using Stash.Debugging;
using Stash.Runtime;
using Stash.Runtime.Types;

namespace Stash.Bytecode;

/// <summary>
/// Async function spawning, spread calls, and await opcode handlers.
/// </summary>
public sealed partial class VirtualMachine
{
    /// <summary>
    /// Spawns an async <see cref="VMFunction"/> on a background thread via a child VM and
    /// returns a <see cref="StashFuture"/>. Called when a Chunk with <c>IsAsync = true</c> is
    /// invoked. All arity checks, rest-param collection, and default-param padding have already
    /// been applied by <see cref="CallValue"/>; <paramref name="baseSlot"/> points to the first
    /// argument on the current stack.
    /// </summary>
    private StashFuture SpawnAsyncFunction(Chunk fnChunk, Upvalue[] upvalues, int baseSlot, SourceSpan? callSpan, Dictionary<string, StashValue>? moduleGlobals = null)
    {
        // Snapshot the fully-prepared arguments (rest-collected, defaults applied).
        int arity = fnChunk.Arity;
        var capturedArgs = new object?[arity];
        for (int i = 0; i < arity; i++)
        {
            capturedArgs[i] = _stack[baseSlot + i].ToObject();
        }

        _sp = baseSlot - 1; // pop callee slot + all args off the parent stack

        // Upgrade parent IO streams to thread-safe wrappers before spawning.
        if (_context.Output is not SynchronizedTextWriter)
        {
            _context.Output = new SynchronizedTextWriter(_context.Output);
        }

        if (_context.ErrorOutput is not SynchronizedTextWriter)
        {
            _context.ErrorOutput = new SynchronizedTextWriter(_context.ErrorOutput);
        }

        // Snapshot everything the child VM needs — capture before Task.Run to avoid races.
        var capturedGlobals = new Dictionary<string, StashValue>(_globals);
        var capturedModuleLoader = _moduleLoader;
        var capturedModuleCache = ModuleCache;
        var capturedImportStack = _importStack;
        var capturedModuleLocks = ModuleLocks;
        string? capturedFile = _context.CurrentFile;
        TextWriter capturedOutput = _context.Output;
        TextWriter capturedErrorOutput = _context.ErrorOutput;
        TextReader capturedInput = _context.Input;
        bool capturedEmbedded = EmbeddedMode;

        var cts = new CancellationTokenSource();
        var task = Task.Run(() =>
        {
            var childVM = new VirtualMachine(capturedGlobals, cts.Token)
            {
                _moduleLoader = capturedModuleLoader,
                ModuleCache = capturedModuleCache,
                _importStack = capturedImportStack,
                ModuleLocks = capturedModuleLocks,
                EmbeddedMode = capturedEmbedded,
            };
            childVM._context.CurrentFile = capturedFile;
            childVM._context.Output = capturedOutput;
            childVM._context.ErrorOutput = capturedErrorOutput;
            childVM._context.Input = capturedInput;

            // Replicate the call-frame layout: callee slot + prepared args, then run.
            childVM.Push(StashValue.FromObj(new VMFunction(fnChunk, upvalues) { ModuleGlobals = moduleGlobals }));
            for (int i = 0; i < arity; i++)
            {
                childVM.Push(StashValue.FromObject(capturedArgs[i]));
            }

            int childBase = childVM._sp - arity;
            childVM.PushFrame(fnChunk, childBase, upvalues, fnChunk.Name, moduleGlobals);
            childVM.InitGlobalSlots(fnChunk);
            return childVM.Run();
        }, cts.Token);

        return new StashFuture(task, cts);
    }


    private void ExecuteCallSpread(ref CallFrame frame, IDebugger? debugger)
    {
        // Scan backward from stack top to find ArgSentinel
        int rawArgc = 0;
        int sentinelIdx = -1;
        for (int i = _sp - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_stack[i].AsObj, _argSentinel))
            {
                sentinelIdx = i;
                break;
            }
            rawArgc++;
        }

        if (sentinelIdx < 0)
        {
            throw new RuntimeError("Internal error: ArgMark sentinel not found.", GetCurrentSpan(ref frame));
        }

        // Callee is right below the sentinel
        object? callee = _stack[sentinelIdx - 1].AsObj;
        SourceSpan? callSpan = GetCurrentSpan(ref frame);

        // Expand SpreadMarkers: collect all args, expanding spreads
        var expandedArgs = new List<StashValue>(rawArgc);
        for (int i = sentinelIdx + 1; i < _sp; i++)
        {
            StashValue argVal = _stack[i];
            if (argVal.IsObj && argVal.AsObj is SpreadMarker sm)
            {
                if (sm.Items is List<StashValue> svSpreadList)
                {
                    expandedArgs.AddRange(svSpreadList);
                }
                else
                {
                    throw new RuntimeError("Spread in function call requires an array.",
                        callSpan);
                }
            }
            else
            {
                expandedArgs.Add(argVal);
            }
        }

        // Write expanded args back to stack starting at sentinelIdx
        int expandedArgc = expandedArgs.Count;
        // Ensure stack capacity
        while (sentinelIdx + expandedArgc >= _stack.Length)
        {
            var bigger = new StashValue[_stack.Length * 2];
            Array.Copy(_stack, bigger, _stack.Length);
            _stack = bigger;
        }
        for (int i = 0; i < expandedArgc; i++)
        {
            _stack[sentinelIdx + i] = expandedArgs[i];
        }
        _sp = sentinelIdx + expandedArgc;

        int prevFrameCount = _frameCount;
        CallValue(callee, expandedArgc, callSpan);

        if (StepLimit > 0 && ++StepCount >= StepLimit)
        {
            throw new Stash.Runtime.StepLimitExceededException(StepLimit);
        }

        // Debug: track function entry (same as OP_CALL)
        if (debugger is not null && _frameCount > prevFrameCount)
        {
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


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteAwait(ref CallFrame frame)
    {
        object? future = Pop().ToObject();
        if (future is StashFuture sf)
        {
            Push(StashValue.FromObject(sf.GetResult()));
        }
        else
        {
            Push(StashValue.FromObject(future)); // non-future values pass through
        }
    }
}
