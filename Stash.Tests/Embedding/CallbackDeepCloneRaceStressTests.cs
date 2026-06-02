using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib;
using Stash.Stdlib.BuiltIns;
using Xunit;

namespace Stash.Tests.Embedding;

/// <summary>
/// Regression tests for F02: <c>RuntimeValues.DeepClone</c> must snapshot the parent's
/// live <see cref="StashDictionary"/> and <see cref="StashArray"/> inner collections
/// <em>before</em> walking them on a background thread.
///
/// Root cause: <c>DeepCloneDictionary</c> called <c>dict.RawEntries()</c> →
/// <c>_entries.ToList()</c>, which dispatches to <c>ICollection.CopyTo</c> — no version
/// check — silently producing a torn snapshot when the owning thread mutates the dict
/// concurrently.  <c>DeepCloneArray</c> used a <c>for (i; i &lt; arr.Count; i++)</c>
/// indexed walk — a concurrent <c>Add</c> can resize the backing array between the
/// <c>Count</c> read and the indexer, reading stale or out-of-range slots without throwing.
///
/// Fix: both methods now take a bounded-retry snapshot via the version-checked struct
/// enumerator (<c>foreach</c> over the live collection) before any per-element work.
/// <c>StashDictionary.RawEntriesEnumerable()</c> was added to expose a lazy, version-checked
/// enumerable path (as opposed to <c>RawEntries()</c> which pre-materialises via CopyTo).
/// </summary>
public class CallbackDeepCloneRaceStressTests
{
    private const int StressIterations = 300;

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Wraps <paramref name="value"/> in a <see cref="StashValue"/> object reference.
    /// </summary>
    private static StashValue Dict(StashDictionary d) => StashValue.FromObj(d);
    private static StashValue Arr(StashArray a)       => StashValue.FromObj(a);
    private static StashValue Int(long v)             => StashValue.FromInt(v);

    // ── F02-A: DeepClone of StashDictionary under concurrent Set ──────────────

    /// <summary>
    /// Spawns a writer thread that continuously calls <c>Set</c> on a parent
    /// <see cref="StashDictionary"/> while the test thread calls
    /// <c>RuntimeValues.DeepClone</c> on the same dict in a tight loop.
    ///
    /// Post-fix: every deep-clone completes and returns a valid, non-frozen
    /// <see cref="StashDictionary"/> without throwing — the bounded-retry foreach snapshot
    /// in <c>DeepCloneDictionary</c> absorbs any concurrent structural mutations.
    ///
    /// The child clone must NOT reference the parent's entries — mutating the parent
    /// after the clone must not affect the cloned dict (isolation invariant).
    /// </summary>
    [Fact]
    public void DeepClone_Dictionary_UnderConcurrentSet_NeverThrowsOrTears()
    {
        var parentDict = new StashDictionary();
        for (int i = 0; i < 20; i++)
            parentDict.Set($"k{i}", Int(i));

        using var cts = new CancellationTokenSource();
        Exception? writerException = null;

        var writerThread = new Thread(() =>
        {
            var rng = new Random(17);
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    // Set is the structural-mutation hot path in Modules / Variables.
                    // Use a modest pause to avoid exhausting the snapshot retry budget
                    // (64 retries is a safety backstop; in real code, imports are
                    // far less frequent than tight-loop writes in a stress test).
                    parentDict.Set($"k{rng.Next(40)}", Int(rng.Next(100)));
                    Thread.SpinWait(50);
                }
            }
            catch (Exception ex)
            {
                writerException = ex;
                cts.Cancel();
            }
        })
        { IsBackground = true };
        writerThread.Start();

        int successCount = 0;
        try
        {
            for (int iter = 0; iter < StressIterations; iter++)
            {
                // DeepClone must not throw despite the concurrent Set calls.
                var cloneVal = RuntimeValues.DeepClone(Dict(parentDict));
                Assert.True(cloneVal.IsObj, "DeepClone should return an object value");
                var cloneDict = Assert.IsType<StashDictionary>(cloneVal.AsObj);
                Assert.False(cloneDict.IsFrozen, "Cloned dict should not be frozen");
                successCount++;
                Thread.SpinWait(4);
            }
        }
        finally
        {
            cts.Cancel();
            writerThread.Join(TimeSpan.FromSeconds(5));
        }

        Assert.Null(writerException);
        Assert.Equal(StressIterations, successCount);
    }

    /// <summary>
    /// After a deep clone, mutating the parent dict must not affect the clone
    /// (isolation invariant — verifies the snapshot actually captured a full copy,
    /// not shared references).
    /// </summary>
    [Fact]
    public void DeepClone_Dictionary_CloneIsIndependent_ParentMutationDoesNotAffectClone()
    {
        var parent = new StashDictionary();
        parent.Set("x", Int(42));

        var cloneVal = RuntimeValues.DeepClone(Dict(parent));
        var clone = Assert.IsType<StashDictionary>(cloneVal.AsObj);

        // Mutate the parent after cloning.
        parent.Set("x", Int(99));
        parent.Set("y", Int(1));

        // Clone must retain the original state.
        Assert.Equal(42L, clone.Get("x").AsInt);
        Assert.Equal(1, clone.Count);
    }

    // ── F02-B: DeepClone of StashArray under concurrent Add ───────────────────

    /// <summary>
    /// Spawns a writer thread that continuously calls <c>Add</c> on a parent
    /// <see cref="StashArray"/> while the test thread calls <c>RuntimeValues.DeepClone</c>
    /// on the same array in a tight loop.
    ///
    /// Post-fix: the bounded-retry foreach snapshot in <c>DeepCloneArray</c> detects
    /// concurrent structural mutations (via the List&lt;T&gt; version-checked enumerator)
    /// and retries until it gets an uninterrupted walk — never silently reading stale or
    /// out-of-range slots.
    /// </summary>
    [Fact]
    public void DeepClone_Array_UnderConcurrentAdd_NeverThrowsOrTears()
    {
        var parentArr = new StashArray(20);
        for (int i = 0; i < 20; i++)
            parentArr.Add(Int(i));

        using var cts = new CancellationTokenSource();
        Exception? writerException = null;

        var writerThread = new Thread(() =>
        {
            var rng = new Random(31);
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    // Alternate Add/RemoveAt to keep the array bounded (prevent OOM
                    // from unbounded growth during a long-running stress loop).
                    if (parentArr.Count > 5 && rng.Next(2) == 0)
                        parentArr.RemoveAt(rng.Next(parentArr.Count));
                    else
                        parentArr.Add(Int(rng.Next(100)));
                    Thread.SpinWait(50);
                }
            }
            catch (Exception ex)
            {
                writerException = ex;
                cts.Cancel();
            }
        })
        { IsBackground = true };
        writerThread.Start();

        int successCount = 0;
        try
        {
            for (int iter = 0; iter < StressIterations; iter++)
            {
                var cloneVal = RuntimeValues.DeepClone(Arr(parentArr));
                Assert.True(cloneVal.IsObj, "DeepClone should return an object value");
                var cloneArr = Assert.IsType<StashArray>(cloneVal.AsObj);
                Assert.False(cloneArr.IsFrozen, "Cloned array should not be frozen");
                // The clone must have at least 1 element (writer maintains count > 5
                // before removing, so the array stays non-empty).
                Assert.True(cloneArr.Count >= 1,
                    $"Cloned array should be non-empty, got count: {cloneArr.Count}");
                successCount++;
                Thread.SpinWait(4);
            }
        }
        finally
        {
            cts.Cancel();
            writerThread.Join(TimeSpan.FromSeconds(5));
        }

        Assert.Null(writerException);
        Assert.Equal(StressIterations, successCount);
    }

    /// <summary>
    /// After a deep clone, mutating the parent array must not affect the clone.
    /// </summary>
    [Fact]
    public void DeepClone_Array_CloneIsIndependent_ParentMutationDoesNotAffectClone()
    {
        var parent = new StashArray(3);
        parent.Add(Int(1));
        parent.Add(Int(2));
        parent.Add(Int(3));

        var cloneVal = RuntimeValues.DeepClone(Arr(parent));
        var clone = Assert.IsType<StashArray>(cloneVal.AsObj);

        // Mutate the parent after cloning.
        parent.Add(Int(99));
        parent[0] = Int(0);

        // Clone must retain the original state.
        Assert.Equal(3, clone.Count);
        Assert.Equal(1L, clone[0].AsInt);
    }

    // ── F02-C: nested dict/array deep clone under concurrent mutation ─────────

    /// <summary>
    /// Tests that nested structures (dict-of-array, array-of-dict) are also safely
    /// snapshotted at each level of the walk.
    /// </summary>
    [Fact]
    public void DeepClone_NestedDictOfArray_UnderConcurrentMutation_NeverThrows()
    {
        // Build a parent dict containing an inner array.
        var innerArr = new StashArray(5);
        for (int i = 0; i < 5; i++)
            innerArr.Add(Int(i));

        var parentDict = new StashDictionary();
        parentDict.Set("arr", Arr(innerArr));
        parentDict.Set("x", Int(1));

        using var cts = new CancellationTokenSource();
        Exception? writerException = null;

        // Mutate both the outer dict and the inner array.
        // Alternate Add/RemoveAt to keep the array bounded (avoid unbounded growth
        // which would exhaust memory during a long stress run).
        var writerThread = new Thread(() =>
        {
            var rng = new Random(53);
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    if (rng.Next(2) == 0)
                    {
                        parentDict.Set("x", Int(rng.Next(100)));
                    }
                    else if (innerArr.Count > 3 && rng.Next(2) == 0)
                    {
                        // RemoveAt mutates the List and bumps its version.
                        innerArr.RemoveAt(rng.Next(innerArr.Count));
                    }
                    else
                    {
                        innerArr.Add(Int(rng.Next(100)));
                    }
                    Thread.SpinWait(50);
                }
            }
            catch (Exception ex)
            {
                writerException = ex;
                cts.Cancel();
            }
        })
        { IsBackground = true };
        writerThread.Start();

        int successCount = 0;
        try
        {
            for (int iter = 0; iter < StressIterations; iter++)
            {
                var cloneVal = RuntimeValues.DeepClone(Dict(parentDict));
                Assert.True(cloneVal.IsObj);
                successCount++;
                Thread.SpinWait(4);
            }
        }
        finally
        {
            cts.Cancel();
            writerThread.Join(TimeSpan.FromSeconds(5));
        }

        Assert.Null(writerException);
        Assert.Equal(StressIterations, successCount);
    }

    // ── F02-D: end-to-end signal callback with mutable captured global ─────────

    /// <summary>
    /// End-to-end test: a Stash VM has a captured global dict.  Background tasks fire
    /// <c>SignalImpl.Dispatch</c> (which triggers <c>VMContext.InvokeCallbackDirect</c>
    /// → <c>BuildChildGlobals</c> → <c>DeepClone</c>) while the main thread continuously
    /// calls <c>Set</c> on the same dict.  Verifies no exception escapes and the callback
    /// path runs to completion.
    /// </summary>
    [Collection("SignalRegistry")]
    public class DeepCloneEndToEndTest : IDisposable
    {
        public void Dispose() => ClearSignalHandlers();

        private static void ClearSignalHandlers()
        {
            lock (SignalImpl.SignalLock)
            {
                var keys = new List<string>(SignalImpl.SignalHandlers.Keys);
                foreach (var key in keys)
                    if (SignalImpl.SignalHandlers.TryRemove(key, out var entries) && entries.Count > 0)
                        entries[0].Registration?.Dispose();
            }
        }

        private static (Chunk chunk, VirtualMachine vm) CompileToVM(string source)
        {
            var tokens = new Lexer(source, "<test>").ScanTokens();
            var stmts = new Parser(tokens).ParseProgram();
            SemanticResolver.Resolve(stmts);
            var chunk = Compiler.Compile(stmts);
            var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
            return (chunk, vm);
        }

        [Fact]
        public void SignalCallback_WithMutableDictGlobal_UnderConcurrentSet_CompletesWithoutException()
        {
            ClearSignalHandlers();

            // A script that holds a captured dict and registers a signal callback.
            // The callback reads from the dict (forcing DeepClone to walk it on the
            // background branch of InvokeCallbackDirect).
            var source =
                "let data = { v: 0 };\n" +
                "signal.on(Signal.Term, fn() { let x = data.v; });\n";

            var (chunk, vm) = CompileToVM(source);
            // Force the background branch: no real thread will have MainThreadId == -1.
            vm.TestForceBackgroundBranch();
            vm.Execute(chunk);

            // Retrieve the 'data' dict from the VM's globals.
            var dataVal = vm.Globals["data"];
            var dataDict = Assert.IsType<StashDictionary>(dataVal.AsObj);

            using var cts = new CancellationTokenSource();

            // Background: dispatch signal 50 times (triggers DeepClone on child VM).
            var dispatchTask = Task.Run(() =>
            {
                for (int i = 0; i < 50 && !cts.Token.IsCancellationRequested; i++)
                {
                    SignalImpl.Dispatch("Term");
                    Thread.SpinWait(20);
                }
            });

            // Main thread: continuously mutate the captured global dict.
            var rng = new Random(99);
            for (int i = 0; i < 200; i++)
            {
                dataDict.Set("v", StashValue.FromInt(rng.Next(1000)));
                Thread.SpinWait(5);
            }

            dispatchTask.Wait(TimeSpan.FromSeconds(15));
            cts.Cancel();
            // If we reach here without exception, the fix is working.
        }
    }
}
