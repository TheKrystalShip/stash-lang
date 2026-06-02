using System.Collections.Generic;
using Stash.Runtime;
using Stash.Runtime.Errors;
using Stash.Runtime.Types;
using Xunit;

namespace Stash.Tests.Core.Runtime;

/// <summary>
/// Unit tests for <see cref="RuntimeValues.DeepClone"/>.
///
/// done_when coverage:
///   #1 — DeepClone(value, visited) exists, mirrors DeepFreeze's cycle-safe walker
///        shape, and throws a typed error (ValueError) on cycle with the cycle path
///        in the message.
///   #2 (partial) — frozen values share by reference; non-frozen values produce
///        independent copies.
/// </summary>
public class DeepCloneTests
{
    // ── Primitives pass through ────────────────────────────────────────────────

    [Fact]
    public void DeepClone_Int_ReturnsIdenticalValue()
    {
        var val = StashValue.FromInt(42L);
        var clone = RuntimeValues.DeepClone(val);
        Assert.Equal(42L, clone.AsInt);
        Assert.False(clone.IsObj);
    }

    [Fact]
    public void DeepClone_Bool_ReturnsIdenticalValue()
    {
        var val = StashValue.True;
        var clone = RuntimeValues.DeepClone(val);
        Assert.Equal(StashValue.True, clone);
    }

    [Fact]
    public void DeepClone_Null_ReturnsNull()
    {
        var clone = RuntimeValues.DeepClone(StashValue.Null);
        Assert.True(clone.IsNull);
    }

    // ── Frozen values share by reference ──────────────────────────────────────

    [Fact]
    public void DeepClone_FrozenArray_SharesByReference()
    {
        var arr = new StashArray { StashValue.FromInt(1L) };
        arr.Freeze();
        var val = StashValue.FromObj(arr);

        var clone = RuntimeValues.DeepClone(val);

        // Frozen: must be the same object reference.
        Assert.Same(arr, clone.AsObj);
        Assert.True(RuntimeValues.IsFrozen(clone));
    }

    [Fact]
    public void DeepClone_FrozenDict_SharesByReference()
    {
        var dict = new StashDictionary();
        dict.Set("k", StashValue.FromInt(1L));
        dict.Freeze();
        var val = StashValue.FromObj(dict);

        var clone = RuntimeValues.DeepClone(val);

        Assert.Same(dict, clone.AsObj);
    }

    // ── Non-frozen values produce independent copies ───────────────────────────

    [Fact]
    public void DeepClone_NonFrozenDict_ProducesIndependentCopy()
    {
        var dict = new StashDictionary();
        dict.Set("x", StashValue.FromInt(1L));
        var val = StashValue.FromObj(dict);

        var clone = RuntimeValues.DeepClone(val);
        var cloneDict = (StashDictionary)clone.AsObj!;

        // Different object references.
        Assert.NotSame(dict, cloneDict);

        // Mutation in the clone is not visible in the original.
        cloneDict.Set("x", StashValue.FromInt(99L));
        Assert.Equal(1L, dict.Get("x").AsInt);
    }

    [Fact]
    public void DeepClone_NonFrozenArray_ProducesIndependentCopy()
    {
        var arr = new StashArray { StashValue.FromInt(7L) };
        var val = StashValue.FromObj(arr);

        var clone = RuntimeValues.DeepClone(val);
        var cloneArr = (StashArray)clone.AsObj!;

        Assert.NotSame(arr, cloneArr);
        Assert.Equal(1, cloneArr.Count);
        Assert.Equal(7L, cloneArr[0].AsInt);
    }

    [Fact]
    public void DeepClone_NestedDict_ClonesRecursively()
    {
        var inner = new StashDictionary();
        inner.Set("a", StashValue.FromInt(5L));

        var outer = new StashDictionary();
        outer.Set("inner", StashValue.FromObj(inner));
        var val = StashValue.FromObj(outer);

        var clone = RuntimeValues.DeepClone(val);
        var cloneOuter = (StashDictionary)clone.AsObj!;
        var cloneInner = (StashDictionary)cloneOuter.Get("inner").AsObj!;

        Assert.NotSame(inner, cloneInner);
        cloneInner.Set("a", StashValue.FromInt(99L));
        Assert.Equal(5L, inner.Get("a").AsInt); // original unchanged
    }

    // ── Diamond / shared-acyclic: no false positive ───────────────────────────

    [Fact]
    public void DeepClone_DiamondGraph_DoesNotThrow()
    {
        // Two dict entries pointing at the same (non-frozen) leaf — not a cycle.
        var leaf = new StashDictionary();
        leaf.Set("v", StashValue.FromInt(42L));

        var outer = new StashDictionary();
        outer.Set("a", StashValue.FromObj(leaf));
        outer.Set("b", StashValue.FromObj(leaf));

        var val = StashValue.FromObj(outer);

        // Must not throw; diamond is not a cycle.
        var clone = RuntimeValues.DeepClone(val);
        var cloneOuter = (StashDictionary)clone.AsObj!;
        Assert.NotNull(cloneOuter.Get("a").AsObj);
        Assert.NotNull(cloneOuter.Get("b").AsObj);
    }

    // ── Cycle detection ───────────────────────────────────────────────────────

    [Fact]
    public void DeepClone_CyclicDict_ThrowsValueError()
    {
        // dict X contains a reference to itself → cycle.
        var x = new StashDictionary();
        x.Set("self", StashValue.FromObj(x));

        var val = StashValue.FromObj(x);
        var ex = Assert.Throws<ValueError>(() => RuntimeValues.DeepClone(val));

        // The error message must mention the cycle path.
        Assert.Contains("cycle", ex.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeepClone_TwoNodeCycle_ThrowsValueError()
    {
        // X → Y → X
        var x = new StashDictionary();
        var y = new StashDictionary();
        x.Set("y", StashValue.FromObj(y));
        y.Set("x", StashValue.FromObj(x));

        var val = StashValue.FromObj(x);
        var ex = Assert.Throws<ValueError>(() => RuntimeValues.DeepClone(val));
        Assert.Contains("cycle", ex.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    // ── IsFrozen unified predicate ────────────────────────────────────────────

    [Fact]
    public void IsFrozen_FrozenArray_ReturnsTrue()
    {
        var arr = new StashArray();
        arr.Freeze();
        Assert.True(RuntimeValues.IsFrozen(StashValue.FromObj(arr)));
    }

    [Fact]
    public void IsFrozen_NonFrozenArray_ReturnsFalse()
    {
        var arr = new StashArray();
        Assert.False(RuntimeValues.IsFrozen(StashValue.FromObj(arr)));
    }

    [Fact]
    public void IsFrozen_FrozenDict_ReturnsTrue()
    {
        var dict = new StashDictionary();
        dict.Freeze();
        Assert.True(RuntimeValues.IsFrozen(StashValue.FromObj(dict)));
    }

    [Fact]
    public void IsFrozen_Primitive_ReturnsFalse()
    {
        Assert.False(RuntimeValues.IsFrozen(StashValue.FromInt(1L)));
        Assert.False(RuntimeValues.IsFrozen(StashValue.True));
        Assert.False(RuntimeValues.IsFrozen(StashValue.Null));
    }
}
