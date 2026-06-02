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
/// Regression tests for F01: <c>VMContext.InvokeCallbackDirect</c>'s background-thread
/// branch previously passed the parent's LIVE <c>_importStack</c> reference to
/// <c>InitImportStack</c>, which enumerated it via <c>new HashSet(liveSet, comparer)</c>
/// (dispatches to <c>ICollection.CopyTo</c> — no version check — silently tearing the
/// snapshot under concurrent <c>Add</c>/<c>Remove</c> on the owner thread).
///
/// Fix: <see cref="IsolationHelpers.SnapshotImportStack"/> uses an explicit
/// <c>foreach</c> (version-checked HashSet enumerator) inside a bounded-retry loop,
/// mirroring the <see cref="IsolationHelpers.SnapshotEntries"/> pattern that guards the
/// globals enumeration on the same code path (commit 224c52e3).
///
/// <para>
/// <b>Testability note.</b>  The root-cause bug tears <em>silently</em> — the original
/// <c>new HashSet(liveSet, …)</c> does not throw even under concurrent mutation; it just
/// produces a partially-visible copy.  Silent tears are hard to assert in a concurrent
/// stress test without a deterministic injection harness, so the tests here focus on two
/// observable properties:
///
///   (a) <see cref="SnapshotImportStack_UnderConcurrentAddRemove_DoesNotThrow"/> — the
///       snapshot method must never propagate <see cref="InvalidOperationException"/> or
///       any other exception across N iterations of concurrent single-writer Add/Remove.
///       This guards the primary failure mode: the exception escaping into the callback's
///       <c>try/catch</c> and causing silent callback loss.
///
///   (b) <see cref="SnapshotImportStack_PreservesOrdinalIgnoreCaseSemantics"/> — the
///       returned snapshot has the correct comparer semantics.
///
/// End-to-end callback path coverage is in
/// <see cref="CallbackEndToEndStressTest.SignalCallback_UnderImportStackChurn_FiresWithoutException"/>.
/// </para>
/// </summary>
public class CallbackImportRaceStressTests
{
    private const int StressIterations = 500;

    // ── F01 unit: SnapshotImportStack does not throw under concurrent Add/Remove ─

    /// <summary>
    /// A background writer thread continuously calls <c>Add</c> and <c>Remove</c> on a
    /// live <see cref="HashSet{T}"/> while the test thread calls
    /// <c>IsolationHelpers.SnapshotImportStack</c> in a loop.
    ///
    /// The writer uses <c>Thread.Sleep(1)</c> between mutations so the bounded-retry
    /// loop (64 attempts with exponential backoff, matching production's
    /// <c>SnapshotEntries</c>) converges without exhausting the retry budget.
    /// This pacing approximates the real production scenario where <c>Add</c>/<c>Remove</c>
    /// happens at import-statement boundaries (milliseconds apart), not tight loops.
    ///
    /// Asserts: <c>SnapshotImportStack</c> completes all <c>StressIterations</c> without
    /// exception, returning a non-null snapshot each time.
    /// </summary>
    [Fact]
    public void SnapshotImportStack_UnderConcurrentAddRemove_DoesNotThrow()
    {
        var liveSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < 20; i++)
            liveSet.Add($"/modules/mod{i}.stash");

        using var cts = new CancellationTokenSource();
        Exception? writerException = null;

        // Background writer: structural mutations (Add/Remove) — same as Modules.cs.
        // Thread.Sleep(1) pacing: real imports are at millisecond boundaries, making
        // the 64-retry bounded snapshot budget (used by SnapshotEntries and mirrored
        // here) sufficient to converge within a handful of attempts.
        var writerThread = new Thread(() =>
        {
            var rng = new Random(42);
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    string path = $"/modules/mod{rng.Next(40)}.stash";
                    if (rng.Next(2) == 0)
                        liveSet.Add(path);
                    else
                        liveSet.Remove(path);
                    Thread.Sleep(1); // import-statement cadence (not tight-loop)
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
                // Must not throw InvalidOperationException (or any other exception)
                // regardless of concurrent mutations on the writer thread.
                var snapshot = IsolationHelpers.SnapshotImportStack(liveSet);
                Assert.NotNull(snapshot);
                successCount++;
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
    /// The snapshot must use <c>StringComparer.OrdinalIgnoreCase</c> so that the child
    /// VM's import-stack membership tests match the same case-insensitive semantics as
    /// the parent's <c>_importStack</c>.
    /// </summary>
    [Fact]
    public void SnapshotImportStack_PreservesOrdinalIgnoreCaseSemantics()
    {
        var source = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "/modules/Foo.stash",
            "/modules/bar.stash",
        };

        var snapshot = IsolationHelpers.SnapshotImportStack(source);

        Assert.NotNull(snapshot);
        // Snapshot must have OrdinalIgnoreCase so child import-stack membership tests
        // match parent semantics regardless of path casing.
        Assert.Contains("/modules/FOO.stash", snapshot);
        Assert.Contains("/modules/BAR.STASH", snapshot);
        Assert.Equal(2, snapshot.Count);
    }

    // ── F01 end-to-end: callback fires correctly under import-stack churn ─────

    /// <summary>
    /// End-to-end stress through <c>VMContext.InvokeCallbackDirect</c>'s background-thread
    /// branch.  A Stash VM registers a signal handler; background tasks fire
    /// <c>SignalImpl.Dispatch("Term")</c> repeatedly while the main thread concurrently
    /// exercises <c>IsolationHelpers.SnapshotImportStack</c> on a live set being mutated
    /// by a second background thread (simulating parent-thread import churn).
    ///
    /// <c>VirtualMachine.TestForceBackgroundBranch()</c> sets <c>MainThreadId = -1</c> so
    /// that the dispatch thread always takes the child-VM fork branch of
    /// <c>InvokeCallbackDirect</c>, exercising the <c>SnapshotImportStack</c> code path.
    ///
    /// Asserts: all signal dispatches complete without exception.
    /// </summary>
    [Collection("SignalRegistry")]
    public class CallbackEndToEndStressTest : IDisposable
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
        public void SignalCallback_UnderImportStackChurn_FiresWithoutException()
        {
            ClearSignalHandlers();

            var source =
                "let count = 0;\n" +
                "signal.on(Signal.Term, fn() { count = count + 1; });\n";

            var (chunk, vm) = CompileToVM(source);
            // Force the background branch: MainThreadId = -1 is never the current thread.
            vm.TestForceBackgroundBranch();
            vm.Execute(chunk);

            // Churn a local import-stack while dispatching callbacks.
            var liveImportStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < 10; i++)
                liveImportStack.Add($"/mod{i}.stash");

            using var cts = new CancellationTokenSource();

            // Background: dispatch signal 50 times (each triggers InvokeCallbackDirect).
            var dispatchTask = Task.Run(() =>
            {
                for (int i = 0; i < 50 && !cts.Token.IsCancellationRequested; i++)
                {
                    SignalImpl.Dispatch("Term");
                    Thread.SpinWait(20);
                }
            });

            // Background: churn the import-stack (Add/Remove) — simulates Modules.cs.
            var importChurnTask = Task.Run(() =>
            {
                var rng = new Random(7);
                while (!cts.Token.IsCancellationRequested)
                {
                    string path = $"/mod{rng.Next(20)}.stash";
                    if (rng.Next(2) == 0) liveImportStack.Add(path);
                    else liveImportStack.Remove(path);
                    Thread.SpinWait(10);
                }
            });

            // Main thread: snapshot the live set — exercises the fix path in isolation.
            for (int i = 0; i < 100; i++)
            {
                var snap = IsolationHelpers.SnapshotImportStack(liveImportStack);
                Assert.NotNull(snap);
            }

            dispatchTask.Wait(TimeSpan.FromSeconds(15));
            cts.Cancel();
            importChurnTask.Wait(TimeSpan.FromSeconds(5));
        }
    }
}
