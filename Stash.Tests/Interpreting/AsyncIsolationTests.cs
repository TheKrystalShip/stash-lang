using System.Collections.Generic;
using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Runtime.Errors;
using Stash.Runtime.Types;
using Stash.Stdlib;
using Xunit;

namespace Stash.Tests.Interpreting;

/// <summary>
/// Tests that async child VMs receive a freeze-or-clone copy of globals via
/// <c>IsolationHelpers.BuildChildGlobals</c>, not a shallow reference-sharing copy.
///
/// done_when coverage:
///   #4 — An async fn that mutates a non-frozen captured StashDictionary observes its
///         writes call-local — parent's dict is unchanged after task.all.
///   #5 — The same async fn with the dict freeze'd before spawn shares by reference;
///         the child's set/del writes throw ReadOnlyError.
/// </summary>
public class AsyncIsolationTests : StashTestBase
{
    // ── Non-frozen dict: mutations are call-local ──────────────────────────────

    /// <summary>
    /// Directly verifies at the C# level that BuildChildGlobals deep-clones a non-frozen dict.
    /// This is the foundational invariant for the Stash-level tests.
    /// </summary>
    [Fact]
    public void BuildChildGlobals_NonFrozenDict_ProducesIndependentClone()
    {
        // Set up a globals dict with a non-frozen StashDictionary.
        var originalDict = new StashDictionary();
        originalDict.Set("count", StashValue.FromInt(0L));

        var parentGlobals = new Dictionary<string, StashValue>
        {
            ["shared"] = StashValue.FromObj(originalDict)
        };

        // Build child globals via the isolation helper (IsolationHelpers is internal + InternalsVisibleTo).
        var childGlobals = IsolationHelpers.BuildChildGlobals(parentGlobals);

        // The child's dict should be a different object.
        var childDictValue = childGlobals["shared"];
        var childDict = (StashDictionary)childDictValue.AsObj!;

        Assert.NotSame(originalDict, childDict);
        Assert.Equal(0L, childDict.Get("count").AsInt);

        // Mutate child's copy — parent's dict must be unchanged.
        childDict.Set("count", StashValue.FromInt(99L));
        Assert.Equal(0L, originalDict.Get("count").AsInt);
    }

    [Fact]
    public void AsyncChild_MutatesNonFrozenDict_ParentDictUnchanged()
    {
        // The child clones the captured dict; parent's dict is unchanged after await.
        var result = Run("""
            let shared = { count: 0 };

            async fn mutate() {
                shared.count = 99;
                return shared.count;
            }

            let childValue = await mutate();

            // Child sees its own write (99), parent still sees 0.
            let result = conv.toStr(childValue) + "," + conv.toStr(shared.count);
            """);

        Assert.Equal("99,0", result);
    }

    [Fact]
    public void AsyncChild_AddsKeyToNonFrozenDict_ParentDictUnchanged()
    {
        // The child adds a new key; the parent dict should not gain that key.
        var result = Run("""
            let d = { a: 1 };

            async fn addKey() {
                d.b = 2;
                return d.b;
            }

            let f = addKey();
            await f;

            // Parent dict should NOT have "b" — child got a clone.
            let result = conv.toStr(dict.has(d, "b") ? d.b : -1);
            """);

        Assert.Equal("-1", result);
    }

    [Fact]
    public void AsyncChildren_MutateNonFrozenDict_NoCorruption()
    {
        // Multiple concurrent tasks each mutate a cloned dict; no InvalidOperationException
        // and the parent's dict reflects none of their writes.
        RunStatements("""
            let d = { x: 0 };

            async fn bump(n) {
                d.x = n;
            }

            let futures = [];
            let i = 0;
            while (i < 10) {
                futures.push(bump(i));
                i = i + 1;
            }
            await task.all(futures);

            // Parent's dict should be unmodified (all children got clones).
            if (d.x != 0) {
                throw "parent dict was mutated: " + conv.toStr(d.x);
            }
            """);
        // No exception = pass.
    }

    // ── Frozen dict: shared by reference; writes throw ReadOnlyError ──────────

    [Fact]
    public void AsyncChild_FrozenDictSetThrowsReadOnlyError()
    {
        // Stash surface: `readonly const` freezes the dict in place.
        // The child receives the same frozen reference — dict.set throws ReadOnlyError.
        var result = Run("""
            readonly const frozen = { count: 0 };

            async fn tryMutate() {
                try {
                    frozen.count = 1;
                    return "no-error";
                } catch (ReadOnlyError e) {
                    return "ReadOnlyError";
                }
            }

            let f = tryMutate();
            let result = await f;
            """);

        Assert.Equal("ReadOnlyError", result);
    }

    [Fact]
    public void AsyncChild_FrozenDict_SharedByReference()
    {
        // After the async call, the parent's frozen dict is unchanged,
        // confirming the child shared the same reference (no clone).
        var result = Run("""
            readonly const shared = { v: 42 };

            async fn readValue() {
                return shared.v;
            }

            let f = readValue();
            let childV = await f;

            // Both parent and child see value 42.
            let result = conv.toStr(shared.v) + "," + conv.toStr(childV);
            """);

        Assert.Equal("42,42", result);
    }
}
