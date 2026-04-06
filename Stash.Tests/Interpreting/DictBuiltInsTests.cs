using Stash.Runtime.Types;

namespace Stash.Tests.Interpreting;

public class DictBuiltInsTests : StashTestBase
{
    // ── dict.fromPairs ────────────────────────────────────────────────────

    [Fact]
    public void FromPairs_Basic()
    {
        var result = Run(@"
            let result = dict.fromPairs([[""a"", 1], [""b"", 2]]);
        ");
        var d = Assert.IsType<StashDictionary>(result);
        Assert.Equal(1L, d.Get("a"));
        Assert.Equal(2L, d.Get("b"));
    }

    [Fact]
    public void FromPairs_EmptyArray()
    {
        var result = Run("let result = dict.fromPairs([]);");
        var d = Assert.IsType<StashDictionary>(result);
        Assert.Equal(0, d.Count);
    }

    [Fact]
    public void FromPairs_InvalidPairThrows()
    {
        RunExpectingError("dict.fromPairs([[\"a\"]]);");
    }

    [Fact]
    public void FromPairs_NonArrayThrows()
    {
        RunExpectingError("dict.fromPairs(42);");
    }

    // ── dict.pick ─────────────────────────────────────────────────────────

    [Fact]
    public void Pick_SelectsSpecifiedKeys()
    {
        var result = Run(@"
            let d = {a: 1, b: 2, c: 3};
            let result = dict.pick(d, [""a"", ""c""]);
        ");
        var picked = Assert.IsType<StashDictionary>(result);
        Assert.Equal(2, picked.Count);
        Assert.Equal(1L, picked.Get("a"));
        Assert.Equal(3L, picked.Get("c"));
    }

    [Fact]
    public void Pick_MissingKeysIgnored()
    {
        var result = Run(@"
            let d = {a: 1, b: 2};
            let result = dict.pick(d, [""a"", ""z""]);
        ");
        var picked = Assert.IsType<StashDictionary>(result);
        Assert.Equal(1, picked.Count);
        Assert.Equal(1L, picked.Get("a"));
    }

    [Fact]
    public void Pick_NonDictThrows()
    {
        RunExpectingError("dict.pick(42, [\"a\"]);");
    }

    // ── dict.omit ─────────────────────────────────────────────────────────

    [Fact]
    public void Omit_ExcludesSpecifiedKeys()
    {
        var result = Run(@"
            let d = {a: 1, b: 2, c: 3};
            let result = dict.omit(d, [""b""]);
        ");
        var omitted = Assert.IsType<StashDictionary>(result);
        Assert.Equal(2, omitted.Count);
        Assert.Equal(1L, omitted.Get("a"));
        Assert.Equal(3L, omitted.Get("c"));
    }

    [Fact]
    public void Omit_NonExistentKeysIgnored()
    {
        var result = Run(@"
            let d = {a: 1};
            let result = dict.omit(d, [""z""]);
        ");
        var omitted = Assert.IsType<StashDictionary>(result);
        Assert.Equal(1, omitted.Count);
    }

    [Fact]
    public void Omit_NonDictThrows()
    {
        RunExpectingError("dict.omit(42, [\"a\"]);");
    }

    // ── dict.defaults ─────────────────────────────────────────────────────

    [Fact]
    public void Defaults_FillsMissingKeys()
    {
        var result = Run(@"
            let d = {a: 1};
            let defaults = {a: 99, b: 2, c: 3};
            let result = dict.defaults(d, defaults);
        ");
        var merged = Assert.IsType<StashDictionary>(result);
        Assert.Equal(1L, merged.Get("a")); // original value preserved
        Assert.Equal(2L, merged.Get("b")); // filled from defaults
        Assert.Equal(3L, merged.Get("c")); // filled from defaults
    }

    [Fact]
    public void Defaults_EmptyDict()
    {
        var result = Run(@"
            let d = {};
            let defaults = {x: 42};
            let result = dict.defaults(d, defaults);
        ");
        var merged = Assert.IsType<StashDictionary>(result);
        Assert.Equal(42L, merged.Get("x"));
    }

    [Fact]
    public void Defaults_NonDictThrows()
    {
        RunExpectingError("dict.defaults(42, {});");
    }

    // ── dict.any ──────────────────────────────────────────────────────────

    [Fact]
    public void Any_SomeMatch()
    {
        var result = Run(@"
            let d = {a: 1, b: 10, c: 3};
            let result = dict.any(d, (k, v) => v > 5);
        ");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Any_NoneMatch()
    {
        var result = Run(@"
            let d = {a: 1, b: 2};
            let result = dict.any(d, (k, v) => v > 100);
        ");
        Assert.Equal(false, result);
    }

    [Fact]
    public void Any_EmptyDict()
    {
        var result = Run("let result = dict.any({}, (k, v) => true);");
        Assert.Equal(false, result);
    }

    [Fact]
    public void Any_NonDictThrows()
    {
        RunExpectingError("dict.any(42, (k, v) => true);");
    }

    // ── dict.every ────────────────────────────────────────────────────────

    [Fact]
    public void Every_AllMatch()
    {
        var result = Run(@"
            let d = {a: 10, b: 20};
            let result = dict.every(d, (k, v) => v > 5);
        ");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Every_SomeFail()
    {
        var result = Run(@"
            let d = {a: 10, b: 2};
            let result = dict.every(d, (k, v) => v > 5);
        ");
        Assert.Equal(false, result);
    }

    [Fact]
    public void Every_EmptyDict()
    {
        var result = Run("let result = dict.every({}, (k, v) => false);");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Every_NonDictThrows()
    {
        RunExpectingError("dict.every(42, (k, v) => true);");
    }

    // ── dict.find ─────────────────────────────────────────────────────────

    [Fact]
    public void Find_MatchingEntry()
    {
        var result = Run(@"
            let d = {a: 1, b: 10, c: 3};
            let result = dict.find(d, (k, v) => v > 5);
        ");
        var pair = Assert.IsType<List<object?>>(result);
        Assert.Equal(2, pair.Count);
        Assert.Equal("b", pair[0]);
        Assert.Equal(10L, pair[1]);
    }

    [Fact]
    public void Find_NoMatch()
    {
        var result = Run(@"
            let d = {a: 1, b: 2};
            let result = dict.find(d, (k, v) => v > 100);
        ");
        Assert.Null(result);
    }

    [Fact]
    public void Find_EmptyDict()
    {
        var result = Run("let result = dict.find({}, (k, v) => true);");
        Assert.Null(result);
    }

    [Fact]
    public void Find_NonDictThrows()
    {
        RunExpectingError("dict.find(42, (k, v) => true);");
    }
}
