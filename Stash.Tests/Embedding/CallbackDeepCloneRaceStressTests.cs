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
/// Root cause:
/// <list type="bullet">
///   <item><c>DeepCloneDictionary</c> called <c>dict.RawEntries()</c> →
///     <c>_entries.ToList()</c>, which dispatches to <c>ICollection.CopyTo</c> — no
///     version check — silently producing a torn snapshot under concurrent mutation.</item>
///   <item><c>DeepCloneArray</c> used a <c>for (i; i &lt; arr.Count; i++)</c> indexed walk
///     — a concurrent <c>Add</c> can resize the backing array between the <c>Count</c> read
///     and the indexer, reading stale/out-of-range slots without throwing.</item>
/// </list>
///
/// Fix: both methods now take a bounded-retry snapshot via the version-checked struct
/// enumerator (<c>foreach</c> over the live collection, bounded at 64 retries matching
/// <c>IsolationHelpers.SnapshotEntries</c>) before any per-element work.
/// <c>StashDictionary.RawEntriesEnumerable()</c> was added to expose a lazy,
/// version-checked enumerable path (as opposed to <c>RawEntries()</c> which
/// pre-materialises via CopyTo).
///
/// <para>
/// <b>Testability note.</b>  Both root-cause bugs tear <em>silently</em> — the original
/// code does not throw under concurrent mutation; it produces partially-visible copies.
/// Silent tears are difficult to assert in a concurrent stress test without a deterministic
/// injection harness.  The tests here focus on two observable properties:
///
///   (a) The snapshot and clone methods must not propagate
///       <see cref="InvalidOperationException"/> across N iterations of concurrent
///       single-writer mutation (<c>DoesNotThrow</c> suffix tests).  This guards the
///       primary failure mode on the callback path: the exception escaping into the
///       surrounding <c>try/catch</c> and causing silent callback loss.
///
///   (b) After cloning, mutating the parent must not affect the clone (isolation
///       invariant — synchronous, single-threaded, verifiably correct regardless of timing).
///
/// End-to-end coverage is in
/// <see cref="DeepCloneEndToEndTest.SignalCallback_WithMutableDictGlobal_UnderConcurrentSet_CompletesWithoutException"/>.
/// </para>
/// </summary>
public class CallbackDeepCloneRaceStressTests
{
    private const int StressIterations = 300;

    // ── helpers ───────────────────────────────────────────────────────────────

    private static StashValue Dict(StashDictionary d) => StashValue.FromObj(d);
    private static StashValue Arr(StashArray a)       => StashValue.FromObj(a);
    private static StashValue Int(long v)             => StashValue.FromInt(v);

    // ── F02-A: DeepClone of StashDictionary does not throw under concurrent Set ─

    /// <summary>
    /// A background writer thread continuously calls <c>Set</c> on a parent
    /// <see cref="StashDictionary"/> while the test thread calls
    /// <c>RuntimeValues.DeepClone</c> on the same dict in a loop.
    ///
    /// The writer uses <c>Thread.Sleep(1)</c> between mutations to keep the write
    /// frequency within the bounded-retry budget (64 retries, matching
    /// <c>SnapshotEntries</c>) — analogous to the production scenario where globals
    /// are assigned at statement boundaries, not in tight loops.
    ///
    /// Asserts: <c>DeepClone</c> completes all iterations without exception, returning
    /// a valid, non-frozen <see cref="StashDictionary"/> each time.
    /// </summary>
    [Fact]
    public void DeepClone_Dictionary_UnderConcurrentSet_DoesNotThrow()
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
                    // Set on an existing key: no structural mutation for the dict
                    // (Dictionary version is not bumped on value-update).
                    // Set on a new key: structural mutation (Add), bumps version.
                    parentDict.Set($"k{rng.Next(40)}", Int(rng.Next(100)));
                    Thread.Sleep(1); // statement-boundary cadence
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
                Assert.True(cloneVal.IsObj, "DeepClone should return an object value");
                var cloneDict = Assert.IsType<StashDictionary>(cloneVal.AsObj);
                Assert.False(cloneDict.IsFrozen, "Cloned dict should not be frozen");
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
    /// After a deep clone, mutating the parent dict must not affect the clone
    /// (isolation invariant — verifies the snapshot captured a full, independent copy).
    /// </summary>
    [Fact]
    public void DeepClone_Dictionary_CloneIsIndependent_ParentMutationDoesNotAffectClone()
    {
        var parent = new StashDictionary();
        parent.Set("x", Int(42));

        var cloneVal = RuntimeValues.DeepClone(Dict(parent));
        var clone = Assert.IsType<StashDictionary>(cloneVal.AsObj);

        parent.Set("x", Int(99));
        parent.Set("y", Int(1));

        Assert.Equal(42L, clone.Get("x").AsInt);
        Assert.Equal(1, clone.Count);
    }

    // ── F02-B: DeepClone of StashArray does not throw under concurrent Add/Remove ─

    /// <summary>
    /// A background writer thread alternates <c>Add</c> and <c>RemoveAt</c> on a parent
    /// <see cref="StashArray"/> while the test thread calls <c>RuntimeValues.DeepClone</c>
    /// on the same array in a loop.
    ///
    /// Both <c>Add</c> and <c>RemoveAt</c> bump <see cref="List{T}"/>'s version flag, so
    /// the version-checked <c>foreach</c> in the snapshot loop throws
    /// <see cref="InvalidOperationException"/> if a mutation happens mid-walk — the
    /// bounded-retry loop catches this and retries.  <c>Thread.Sleep(1)</c> pacing keeps
    /// the write frequency within the 64-retry budget.
    ///
    /// Asserts: <c>DeepClone</c> completes all iterations without exception.
    /// </summary>
    [Fact]
    public void DeepClone_Array_UnderConcurrentAddRemove_DoesNotThrow()
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
                    // from unbounded growth over StressIterations).
                    if (parentArr.Count > 5 && rng.Next(2) == 0)
                        parentArr.RemoveAt(rng.Next(parentArr.Count));
                    else
                        parentArr.Add(Int(rng.Next(100)));
                    Thread.Sleep(1); // statement-boundary cadence
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
                Assert.True(cloneArr.Count >= 1, "Cloned array should be non-empty");
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

        parent.Add(Int(99));
        parent[0] = Int(0);

        Assert.Equal(3, clone.Count);
        Assert.Equal(1L, clone[0].AsInt);
    }

    // ── F02-C: nested dict/array deep clone does not throw ──────────────────

    /// <summary>
    /// Tests that nested structures (dict-of-array) are also snapshotted at each level
    /// of the walk without throwing.
    /// </summary>
    [Fact]
    public void DeepClone_NestedDictOfArray_UnderConcurrentMutation_DoesNotThrow()
    {
        var innerArr = new StashArray(5);
        for (int i = 0; i < 5; i++)
            innerArr.Add(Int(i));

        var parentDict = new StashDictionary();
        parentDict.Set("arr", Arr(innerArr));
        parentDict.Set("x", Int(1));

        using var cts = new CancellationTokenSource();
        Exception? writerException = null;

        // Mutate both outer dict and inner array with Thread.Sleep(1) pacing.
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
                        innerArr.RemoveAt(rng.Next(innerArr.Count));
                    }
                    else
                    {
                        innerArr.Add(Int(rng.Next(100)));
                    }
                    Thread.Sleep(1);
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

    // ── F02-D: end-to-end removed (hazard gone) ──────────────────────────────
    //
    // The original end-to-end test (SignalCallback_WithMutableDictGlobal_UnderConcurrentSet)
    // guarded the BuildChildGlobals / DeepClone path on the background callback branch.
    // After the callback-marshaling flip, InvokeCallbackDirect's background branch no
    // longer calls BuildChildGlobals / DeepClone — it enqueues directly.  The original
    // hazard (concurrent Set racing with DeepClone on the pool thread) is structurally
    // impossible on the new path.  The unit-level DeepClone_* tests above continue to
    // cover the async SpawnAsyncFunction path that still uses BuildChildGlobals / DeepClone.
}
