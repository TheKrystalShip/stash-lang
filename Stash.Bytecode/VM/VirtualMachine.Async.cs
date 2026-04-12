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

        var cts = _ct.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(_ct)
            : new CancellationTokenSource();
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


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteAwait(ref CallFrame frame, uint inst)
    {
        byte a = Instruction.GetA(inst);
        byte b = Instruction.GetB(inst);
        int @base = frame.BaseSlot;
        object? future = _stack[@base + b].ToObject();
        _stack[@base + a] = future is StashFuture sf
            ? StashValue.FromObject(sf.GetResult())
            : _stack[@base + b]; // non-future values pass through
    }
}
