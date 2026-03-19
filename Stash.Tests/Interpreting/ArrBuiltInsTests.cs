using Stash.Lexing;
using Stash.Parsing;
using Stash.Interpreting;
using Stash.Interpreting.Types;

namespace Stash.Tests.Interpreting;

public class ArrBuiltInsTests
{
    private static object? Run(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        interpreter.Interpret(statements);

        var resultLexer = new Lexer("result");
        var resultTokens = resultLexer.ScanTokens();
        var resultParser = new Parser(resultTokens);
        var resultExpr = resultParser.Parse();
        return interpreter.Interpret(resultExpr);
    }

    private static void RunExpectingError(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        Assert.Throws<RuntimeError>(() => interpreter.Interpret(statements));
    }

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
        var even = Assert.IsType<List<object?>>(dict.Get("even"));
        var odd  = Assert.IsType<List<object?>>(dict.Get("odd"));
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
        var twoChars = Assert.IsType<List<object?>>(dict.Get("2"));
        Assert.Contains("hi", twoChars);
        var fiveChars = Assert.IsType<List<object?>>(dict.Get("5"));
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
        var all = Assert.IsType<List<object?>>(dict.Get("all"));
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
}
