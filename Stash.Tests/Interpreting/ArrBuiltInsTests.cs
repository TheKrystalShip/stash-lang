using Stash.Runtime.Types;

namespace Stash.Tests.Interpreting;

public class ArrBuiltInsTests : StashTestBase
{
    // ── arr.sortBy ────────────────────────────────────────────────────────────

    [Fact]
    public void SortBy_NumericKey()
    {
        var result = Run("let result = arr.sortBy([3, 1, 2], (x) => x);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
        Assert.Equal(1L, list[0]);
        Assert.Equal(2L, list[1]);
        Assert.Equal(3L, list[2]);
    }

    [Fact]
    public void SortBy_StringKey()
    {
        var result = Run(@"let result = arr.sortBy([""banana"", ""apple"", ""cherry""], (x) => x);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
        Assert.Equal("apple", list[0]);
        Assert.Equal("banana", list[1]);
        Assert.Equal("cherry", list[2]);
    }

    [Fact]
    public void SortBy_ObjectProperty()
    {
        var result = Run(@"
let items = [
    {name: ""Charlie"", age: 30},
    {name: ""Alice"",   age: 25},
    {name: ""Bob"",     age: 35}
];
let sorted = arr.sortBy(items, (x) => x.age);
let result = sorted[0].age;
");
        Assert.Equal(25L, result);
    }

    [Fact]
    public void SortBy_DoesNotMutate()
    {
        var result = Run(@"
let original = [3, 1, 2];
let sorted = arr.sortBy(original, (x) => x);
let result = original[0];
");
        Assert.Equal(3L, result);
    }

    [Fact]
    public void SortBy_EmptyArray()
    {
        var result = Run("let result = arr.sortBy([], (x) => x);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Empty(list);
    }

    [Fact]
    public void SortBy_NonArrayThrows()
    {
        RunExpectingError(@"arr.sortBy(""not an array"", (x) => x);");
    }

    [Fact]
    public void SortBy_NonFunctionThrows()
    {
        RunExpectingError("arr.sortBy([1, 2, 3], 42);");
    }

    [Fact]
    public void SortBy_FloatKeys()
    {
        var result = Run("let result = arr.sortBy([3.5, 1.1, 2.2], (x) => x);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
        Assert.Equal(1.1, list[0]);
        Assert.Equal(2.2, list[1]);
        Assert.Equal(3.5, list[2]);
    }

    // ── arr.groupBy ───────────────────────────────────────────────────────────

    [Fact]
    public void GroupBy_BasicGrouping()
    {
        var result = Run(@"
let result = arr.groupBy([1, 2, 3, 4, 5], (x) => {
    if (x % 2 == 0) { return ""even""; }
    else { return ""odd""; }
});
");
        var dict = Assert.IsType<StashDictionary>(result);
        var even = Assert.IsType<List<object?>>(dict.Get("even").ToObject());
        var odd  = Assert.IsType<List<object?>>(dict.Get("odd").ToObject());
        Assert.Equal(2, even.Count);
        Assert.Contains(2L, even);
        Assert.Contains(4L, even);
        Assert.Equal(3, odd.Count);
        Assert.Contains(1L, odd);
        Assert.Contains(3L, odd);
        Assert.Contains(5L, odd);
    }

    [Fact]
    public void GroupBy_StringLength()
    {
        var result = Run(@"
let words = [""hi"", ""hello"", ""hey"", ""howdy""];
let result = arr.groupBy(words, (x) => conv.toStr(len(x)));
");
        var dict = Assert.IsType<StashDictionary>(result);
        var twoChars = Assert.IsType<List<object?>>(dict.Get("2").ToObject());
        Assert.Contains("hi", twoChars);
        var fiveChars = Assert.IsType<List<object?>>(dict.Get("5").ToObject());
        Assert.Contains("hello", fiveChars);
        Assert.Contains("howdy", fiveChars);
    }

    [Fact]
    public void GroupBy_EmptyArray()
    {
        var result = Run("let result = arr.groupBy([], (x) => x);");
        var dict = Assert.IsType<StashDictionary>(result);
        Assert.Equal(0, dict.Count);
    }

    [Fact]
    public void GroupBy_AllSameGroup()
    {
        var result = Run(@"let result = arr.groupBy([1, 2, 3], (x) => ""all"");");
        var dict = Assert.IsType<StashDictionary>(result);
        var all = Assert.IsType<List<object?>>(dict.Get("all").ToObject());
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public void GroupBy_NonArrayThrows()
    {
        RunExpectingError(@"arr.groupBy(""not an array"", (x) => x);");
    }

    [Fact]
    public void GroupBy_NonFunctionThrows()
    {
        RunExpectingError("arr.groupBy([1, 2, 3], 42);");
    }

    // ── arr.sum ───────────────────────────────────────────────────────────────

    [Fact]
    public void Sum_Integers()
    {
        var result = Run("let result = arr.sum([1, 2, 3, 4, 5]);");
        Assert.Equal(15L, result);
    }

    [Fact]
    public void Sum_Floats()
    {
        var result = Run("let result = arr.sum([1.5, 2.5, 3.0]);");
        Assert.Equal(7.0, result);
    }

    [Fact]
    public void Sum_MixedTypes()
    {
        var result = Run("let result = arr.sum([1, 2.5, 3]);");
        Assert.Equal(6.5, result);
    }

    [Fact]
    public void Sum_EmptyArray()
    {
        var result = Run("let result = arr.sum([]);");
        Assert.Equal(0L, result);
    }

    [Fact]
    public void Sum_NonNumericThrows()
    {
        RunExpectingError(@"arr.sum([""a"", ""b""]);");
    }

    // ── arr.min ───────────────────────────────────────────────────────────────

    [Fact]
    public void Min_Integers()
    {
        var result = Run("let result = arr.min([3, 1, 4, 1, 5]);");
        Assert.Equal(1L, result);
    }

    [Fact]
    public void Min_Floats()
    {
        var result = Run("let result = arr.min([3.5, 1.2, 4.7]);");
        Assert.Equal(1.2, result);
    }

    [Fact]
    public void Min_MixedTypes()
    {
        var result = Run("let result = arr.min([3, 1.5, 4]);");
        Assert.Equal(1.5, result);
    }

    [Fact]
    public void Min_EmptyArrayThrows()
    {
        RunExpectingError("arr.min([]);");
    }

    [Fact]
    public void Min_NonNumericThrows()
    {
        RunExpectingError(@"arr.min([""a"", ""b""]);");
    }

    // ── arr.max ───────────────────────────────────────────────────────────────

    [Fact]
    public void Max_Integers()
    {
        var result = Run("let result = arr.max([3, 1, 4, 1, 5]);");
        Assert.Equal(5L, result);
    }

    [Fact]
    public void Max_Floats()
    {
        var result = Run("let result = arr.max([3.5, 1.2, 4.7]);");
        Assert.Equal(4.7, result);
    }

    [Fact]
    public void Max_MixedTypes()
    {
        var result = Run("let result = arr.max([3, 4.5, 1]);");
        Assert.Equal(4.5, result);
    }

    [Fact]
    public void Max_EmptyArrayThrows()
    {
        RunExpectingError("arr.max([]);");
    }

    [Fact]
    public void Max_NonNumericThrows()
    {
        RunExpectingError(@"arr.max([""a"", ""b""]);");
    }

    // ── arr.zip ───────────────────────────────────────────────────────────

    [Fact]
    public void Zip_BasicArrays()
    {
        var result = Run("let result = arr.zip([1, 2, 3], [\"a\", \"b\", \"c\"]);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
        var pair0 = Assert.IsType<List<object?>>(list[0]);
        Assert.Equal(1L, pair0[0]);
        Assert.Equal("a", pair0[1]);
    }

    [Fact]
    public void Zip_UnequalLengths()
    {
        var result = Run("let result = arr.zip([1, 2], [\"a\", \"b\", \"c\"]);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void Zip_EmptyArrays()
    {
        var result = Run("let result = arr.zip([], []);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Empty(list);
    }

    [Fact]
    public void Zip_NonArrayThrows()
    {
        RunExpectingError("let result = arr.zip(42, [1]);");
    }

    // ── arr.chunk ─────────────────────────────────────────────────────────

    [Fact]
    public void Chunk_EvenSplit()
    {
        var result = Run("let result = arr.chunk([1, 2, 3, 4], 2);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(2, list.Count);
        var chunk0 = Assert.IsType<List<object?>>(list[0]);
        Assert.Equal(2, chunk0.Count);
        Assert.Equal(1L, chunk0[0]);
        Assert.Equal(2L, chunk0[1]);
    }

    [Fact]
    public void Chunk_UnevenSplit()
    {
        var result = Run("let result = arr.chunk([1, 2, 3, 4, 5], 2);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
        var lastChunk = Assert.IsType<List<object?>>(list[2]);
        Assert.Single(lastChunk);
    }

    [Fact]
    public void Chunk_EmptyArray()
    {
        var result = Run("let result = arr.chunk([], 3);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Empty(list);
    }

    [Fact]
    public void Chunk_ZeroSizeThrows()
    {
        RunExpectingError("let result = arr.chunk([1, 2], 0);");
    }

    [Fact]
    public void Chunk_NonArrayThrows()
    {
        RunExpectingError("let result = arr.chunk(42, 2);");
    }

    // ── arr.shuffle ───────────────────────────────────────────────────────

    [Fact]
    public void Shuffle_PreservesElements()
    {
        var result = Run(@"
            let a = [1, 2, 3, 4, 5];
            arr.shuffle(a);
            arr.sort(a);
            let result = a;
        ");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(5, list.Count);
        Assert.Equal(1L, list[0]);
        Assert.Equal(5L, list[4]);
    }

    [Fact]
    public void Shuffle_ReturnsNull()
    {
        var result = Run("let result = arr.shuffle([1, 2, 3]);");
        Assert.Null(result);
    }

    [Fact]
    public void Shuffle_NonArrayThrows()
    {
        RunExpectingError("arr.shuffle(42);");
    }

    // ── arr.take ──────────────────────────────────────────────────────────

    [Fact]
    public void Take_FirstN()
    {
        var result = Run("let result = arr.take([1, 2, 3, 4, 5], 3);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
        Assert.Equal(1L, list[0]);
        Assert.Equal(3L, list[2]);
    }

    [Fact]
    public void Take_MoreThanLength()
    {
        var result = Run("let result = arr.take([1, 2], 5);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void Take_Zero()
    {
        var result = Run("let result = arr.take([1, 2, 3], 0);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Empty(list);
    }

    [Fact]
    public void Take_NonArrayThrows()
    {
        RunExpectingError("let result = arr.take(42, 2);");
    }

    // ── arr.drop ──────────────────────────────────────────────────────────

    [Fact]
    public void Drop_FirstN()
    {
        var result = Run("let result = arr.drop([1, 2, 3, 4, 5], 2);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
        Assert.Equal(3L, list[0]);
        Assert.Equal(5L, list[2]);
    }

    [Fact]
    public void Drop_MoreThanLength()
    {
        var result = Run("let result = arr.drop([1, 2], 5);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Empty(list);
    }

    [Fact]
    public void Drop_Zero()
    {
        var result = Run("let result = arr.drop([1, 2, 3], 0);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
    }

    [Fact]
    public void Drop_NonArrayThrows()
    {
        RunExpectingError("let result = arr.drop(42, 2);");
    }

    // ── arr.partition ─────────────────────────────────────────────────────

    [Fact]
    public void Partition_EvenOdd()
    {
        var result = Run("let result = arr.partition([1, 2, 3, 4, 5], (x) => x % 2 == 0);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(2, list.Count);
        var even = Assert.IsType<List<object?>>(list[0]);
        var odd = Assert.IsType<List<object?>>(list[1]);
        Assert.Equal(2, even.Count);
        Assert.Equal(3, odd.Count);
        Assert.Equal(2L, even[0]);
        Assert.Equal(4L, even[1]);
    }

    [Fact]
    public void Partition_AllMatch()
    {
        var result = Run("let result = arr.partition([2, 4, 6], (x) => x % 2 == 0);");
        var list = Assert.IsType<List<object?>>(result);
        var matching = Assert.IsType<List<object?>>(list[0]);
        var nonMatching = Assert.IsType<List<object?>>(list[1]);
        Assert.Equal(3, matching.Count);
        Assert.Empty(nonMatching);
    }

    [Fact]
    public void Partition_NoneMatch()
    {
        var result = Run("let result = arr.partition([1, 3, 5], (x) => x % 2 == 0);");
        var list = Assert.IsType<List<object?>>(result);
        var matching = Assert.IsType<List<object?>>(list[0]);
        var nonMatching = Assert.IsType<List<object?>>(list[1]);
        Assert.Empty(matching);
        Assert.Equal(3, nonMatching.Count);
    }

    [Fact]
    public void Partition_EmptyArray()
    {
        var result = Run("let result = arr.partition([], (x) => true);");
        var list = Assert.IsType<List<object?>>(result);
        var matching = Assert.IsType<List<object?>>(list[0]);
        var nonMatching = Assert.IsType<List<object?>>(list[1]);
        Assert.Empty(matching);
        Assert.Empty(nonMatching);
    }

    [Fact]
    public void Partition_NonArrayThrows()
    {
        RunExpectingError("let result = arr.partition(42, (x) => true);");
    }

    [Fact]
    public void Partition_NonFunctionThrows()
    {
        RunExpectingError("let result = arr.partition([1, 2], 42);");
    }
}
