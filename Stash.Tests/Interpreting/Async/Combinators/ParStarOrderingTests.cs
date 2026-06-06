namespace Stash.Tests.Interpreting.Async.Combinators;

using Stash.Tests.Interpreting;

/// <summary>
/// D10 — arr.par* order preservation contract:
///   arr.parMap and arr.parFilter preserve input order even when async callbacks
///   complete out of order (e.g. due to staggered sleeps).
/// </summary>
public class ParStarOrderingTests : StashTestBase
{
    // ── arr.parMap order preservation ──────────────────────────────────────────

    [Fact]
    public void ParMap_AsyncCallbacksCompleteOutOfOrder_ResultInInputOrder()
    {
        // Deliberately stagger completion: element 0 sleeps longest, element 2 shortest.
        // If parMap respected completion order instead of input order, the result would
        // be [3, 2, 1]. Input-order preservation means it must be [1, 2, 3].
        var result = Run(@"
let result = arr.parMap([1, 2, 3], async (x) => {
    // Higher index = shorter sleep so callbacks complete in reverse input order
    time.sleep((4 - x) * 20);
    return x;
});
");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
        Assert.Equal(1L, list[0]);
        Assert.Equal(2L, list[1]);
        Assert.Equal(3L, list[2]);
    }

    [Fact]
    public void ParMap_AsyncCallbacksStaggered_LargerInputStillInOrder()
    {
        // Five elements, each sleeping proportional to (6 - x) ms, completing in reverse.
        // Output must still be [1, 2, 3, 4, 5].
        var result = Run(@"
let result = arr.parMap([1, 2, 3, 4, 5], async (x) => {
    time.sleep((6 - x) * 10);
    return x;
});
");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(5, list.Count);
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal((long)(i + 1), list[i]);
        }
    }

    // ── arr.parFilter order preservation ─────────────────────────────────────

    [Fact]
    public void ParFilter_AsyncCallbacksCompleteOutOfOrder_ResultInInputOrder()
    {
        // Even elements kept. Callbacks complete in reverse order (highest index first).
        // Result must still respect original input order: [2, 4].
        var result = Run(@"
let result = arr.parFilter([1, 2, 3, 4], async (x) => {
    time.sleep((5 - x) * 20);
    return x % 2 == 0;
});
");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(2, list.Count);
        Assert.Equal(2L, list[0]);
        Assert.Equal(4L, list[1]);
    }

    [Fact]
    public void ParMap_SyncCallbacksStillPreserveOrder()
    {
        // Regression guard: sync callbacks must still produce input-order results.
        var result = Run(@"
let result = arr.parMap([5, 4, 3, 2, 1], (x) => x * 10);
");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(5, list.Count);
        Assert.Equal(50L, list[0]);
        Assert.Equal(40L, list[1]);
        Assert.Equal(30L, list[2]);
        Assert.Equal(20L, list[3]);
        Assert.Equal(10L, list[4]);
    }
}
