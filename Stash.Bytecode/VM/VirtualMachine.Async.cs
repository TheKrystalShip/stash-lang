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
        // BuildChildGlobals applies the freeze-or-clone rule: frozen globals are shared by
        // reference; non-frozen mutable globals are deep-cloned so the child gets a private
        // copy (call-local mutation; no cross-task data race on the parent's values).
        var capturedGlobals = IsolationHelpers.BuildChildGlobals(_globals);
        // SnapshotUpvalues applies the same rule to captured (closed-over) locals: the child
        // receives pre-closed copies of each upvalue so mutations inside the async body remain
        // call-local and never propagate back through a shared Upvalue object.
        var isolatedUpvalues = IsolationHelpers.SnapshotUpvalues(upvalues);
        // Ensure the child frame's ModuleGlobals points at the isolated copy, not the parent's
        // live _globals dict.  When moduleGlobals is null or IS the parent's own _globals (the
        // common case for top-level scripts and inline async fns), substitute capturedGlobals
        // so the child's ExecuteGetGlobal fast path and slow path both read from the clone.
        // For a genuine imported-module async fn (moduleGlobals is a different, pre-existing
        // module dict), pass it through unchanged — it is not the parent's main globals and
        // does not need re-cloning here; that cross-module isolation case is handled elsewhere.
        var capturedModuleGlobals = (moduleGlobals is null || ReferenceEquals(moduleGlobals, _globals))
            ? capturedGlobals
            : moduleGlobals;
        var capturedModuleLoader = _moduleLoader;
        var capturedModuleCache = ModuleCache;
        // Snapshot the import stack: the child gets an independent copy so concurrent async
        // imports cannot race on the parent's live HashSet, and cannot produce spurious
        // "Circular import detected" errors for paths the parent currently has open.
        // The module-load path (VirtualMachine.Modules.cs) deliberately shares _importStack
        // by reference — that same-thread sharing is what enables synchronous circular-import
        // detection and must NOT be changed here.
        var capturedImportStack = new HashSet<string>(_importStack, StringComparer.OrdinalIgnoreCase);
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
                ModuleLocks = capturedModuleLocks,
                EmbeddedMode = capturedEmbedded,
                // Allow OCE to propagate so the Task ends Canceled (not Faulted) on cancel.
                IsAsyncChild = true,
            };
            // InitImportStack sets both _importStack and _context.ImportStack in one call —
            // single chokepoint so a future refactor cannot forget to sync the context reference.
            childVM.InitImportStack(capturedImportStack);
            childVM._context.CurrentFile = capturedFile;
            childVM._context.Output = capturedOutput;
            childVM._context.ErrorOutput = capturedErrorOutput;
            childVM._context.Input = capturedInput;

            // Replicate the call-frame layout: callee slot + prepared args, then run.
            // Use isolatedUpvalues (freeze-or-cloned snapshot) so the child reads its own
            // private copy of each captured reference, not the parent's live Upvalue objects.
            // Use capturedModuleGlobals so both the VMFunction and the frame point at the
            // isolated globals dict, routing ExecuteGetGlobal's fast and slow paths to the
            // cloned copy rather than the parent's live _globals dictionary.
            childVM.Push(StashValue.FromObj(new VMFunction(fnChunk, isolatedUpvalues) { ModuleGlobals = capturedModuleGlobals }));
            for (int i = 0; i < arity; i++)
            {
                childVM.Push(StashValue.FromObject(capturedArgs[i]));
            }

            int childBase = childVM._sp - arity;
            childVM.PushFrame(fnChunk, childBase, isolatedUpvalues, fnChunk.Name, capturedModuleGlobals);
            childVM.InitGlobalSlots(fnChunk);
            return childVM.Run();
        }, cts.Token);

        return new StashFuture(task, cts);
    }


    [MethodImpl(MethodImplOptions.NoInlining)]
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
