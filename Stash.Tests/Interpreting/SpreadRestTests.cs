using Stash.Lexing;
using Stash.Parsing;
using Stash.Runtime.Types;

namespace Stash.Tests.Interpreting;

public class SpreadRestTests : StashTestBase
{
    private static void RunExpectingParseError(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        parser.ParseProgram();
        Assert.NotEmpty(parser.Errors);
    }

    // === Rest Parameters (#1-10) ===

    [Fact]
    public void RestParam_OnlyRest_CollectsAllArgs()
    {
        var result = Run("fn f(...args) { return args; } let result = f(1, 2, 3);") as List<object?>;
        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
        Assert.Equal(1L, result[0]);
        Assert.Equal(2L, result[1]);
        Assert.Equal(3L, result[2]);
    }

    [Fact]
    public void RestParam_NoArgs_ReturnsEmptyList()
    {
        var result = Run("fn f(...args) { return args; } let result = f();") as List<object?>;
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void RestParam_WithLeadingParam_CollectsRemainder()
    {
        var result = Run("fn f(a, ...rest) { return rest; } let result = f(1, 2, 3);") as List<object?>;
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal(2L, result[0]);
        Assert.Equal(3L, result[1]);
    }

    [Fact]
    public void RestParam_OnlyRequiredArg_ReturnsEmptyRest()
    {
        var result = Run("fn f(a, ...rest) { return rest; } let result = f(1);") as List<object?>;
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void RestParam_MissingRequiredArg_ThrowsRuntimeError()
    {
        RunExpectingError("fn f(a, ...rest) { } f();");
    }

    [Fact]
    public void RestParam_WithDefault_UsesDefaultAndEmptyRest()
    {
        var result = Run("fn f(a, b = 5, ...rest) { return [a, b, rest]; } let result = f(1);") as List<object?>;
        Assert.NotNull(result);
        Assert.Equal(1L, result[0]);
        Assert.Equal(5L, result[1]);
        var rest = result[2] as List<object?>;
        Assert.NotNull(rest);
        Assert.Empty(rest);
    }

    [Fact]
    public void RestParam_WithDefaultOverridden_CapturesExtraArgs()
    {
        var result = Run("fn f(a, b = 5, ...rest) { return [a, b, rest]; } let result = f(1, 2, 3, 4);") as List<object?>;
        Assert.NotNull(result);
        Assert.Equal(1L, result[0]);
        Assert.Equal(2L, result[1]);
        var rest = result[2] as List<object?>;
        Assert.NotNull(rest);
        Assert.Equal(2, rest.Count);
        Assert.Equal(3L, rest[0]);
        Assert.Equal(4L, rest[1]);
    }

    [Fact]
    public void RestParam_Lambda_CollectsAllArgs()
    {
        var result = Run("let f = (...args) => args; let result = f(1, 2);") as List<object?>;
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal(1L, result[0]);
        Assert.Equal(2L, result[1]);
    }

    [Fact]
    public void RestParam_LambdaWithLeadingParam_CollectsRemainder()
    {
        var result = Run("let f = (a, ...rest) => rest; let result = f(1, 2, 3);") as List<object?>;
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal(2L, result[0]);
        Assert.Equal(3L, result[1]);
    }

    [Fact]
    public void RestParam_TypedRest_CollectsTypedArgs()
    {
        var result = Run("fn f(...args: string) { return args; } let result = f(\"a\", \"b\");") as List<object?>;
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal("a", result[0]);
        Assert.Equal("b", result[1]);
    }

    // === Rest Parameter Errors (#11-13) ===

    [Fact]
    public void RestParam_NotLast_ParseError()
    {
        RunExpectingParseError("fn f(...a, b) { }");
    }

    [Fact]
    public void RestParam_MultipleRest_ParseError()
    {
        RunExpectingParseError("fn f(...a, ...b) { }");
    }

    [Fact]
    public void RestParam_WithDefault_ParseError()
    {
        RunExpectingParseError("fn f(...a = []) { }");
    }

    // === Spread in Function Calls (#14-19) ===

    [Fact]
    public void SpreadCall_ArrayArg_PassesElements()
    {
        var result = Run("fn f(a, b) { return a + b; } let args = [1, 2]; let result = f(...args);");
        Assert.Equal(3L, result);
    }

    [Fact]
    public void SpreadCall_SpreadPlusExtra_CombinesArgs()
    {
        var result = Run("fn f(a, b, c) { return a + b + c; } let args = [1, 2]; let result = f(...args, 3);");
        Assert.Equal(6L, result);
    }

    [Fact]
    public void SpreadCall_MultipleSpread_CombinesBoth()
    {
        var result = Run("fn f(a, b, c) { return a + b + c; } let result = f(...[1], ...[2, 3]);");
        Assert.Equal(6L, result);
    }

    [Fact]
    public void SpreadCall_TooManyArgs_ThrowsRuntimeError()
    {
        RunExpectingError("fn f(a, b) { } f(...[1, 2, 3]);");
    }

    [Fact]
    public void SpreadCall_NonIterable_ThrowsRuntimeError()
    {
        RunExpectingError("fn f(a) { } f(...5);");
    }

    [Fact]
    public void SpreadCall_InterleavedSpread_CombinesAll()
    {
        var result = Run("fn f(...args) { return args; } let a = [1, 2]; let result = f(...a, 3, ...a);") as List<object?>;
        Assert.NotNull(result);
        Assert.Equal(5, result.Count);
        Assert.Equal(1L, result[0]);
        Assert.Equal(2L, result[1]);
        Assert.Equal(3L, result[2]);
        Assert.Equal(1L, result[3]);
        Assert.Equal(2L, result[4]);
    }

    // === Spread in Array Literals (#20-26) ===

    [Fact]
    public void SpreadArray_SpreadIntoNew_AppendsElements()
    {
        var result = Run("let a = [1, 2]; let result = [...a, 3];") as List<object?>;
        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
        Assert.Equal(1L, result[0]);
        Assert.Equal(2L, result[1]);
        Assert.Equal(3L, result[2]);
    }

    [Fact]
    public void SpreadArray_TwoSpreads_ConcatenatesArrays()
    {
        var result = Run("let result = [...[1, 2], ...[3, 4]];") as List<object?>;
        Assert.NotNull(result);
        Assert.Equal(4, result.Count);
        Assert.Equal(1L, result[0]);
        Assert.Equal(2L, result[1]);
        Assert.Equal(3L, result[2]);
        Assert.Equal(4L, result[3]);
    }

    [Fact]
    public void SpreadArray_EmptySpread_ProducesNormalArray()
    {
        var result = Run("let e = []; let result = [1, ...e, 2];") as List<object?>;
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal(1L, result[0]);
        Assert.Equal(2L, result[1]);
    }

    [Fact]
    public void SpreadArray_NestedArrays_ShallowCopy()
    {
        var result = Run("let n = [[1], [2]]; let result = [...n];") as List<object?>;
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        var inner0 = result[0] as List<object?>;
        var inner1 = result[1] as List<object?>;
        Assert.NotNull(inner0);
        Assert.NotNull(inner1);
        Assert.Equal(1L, inner0[0]);
        Assert.Equal(2L, inner1[0]);
    }

    [Fact]
    public void SpreadArray_NonIterable_ThrowsRuntimeError()
    {
        RunExpectingError("let result = [...5];");
    }

    [Fact]
    public void SpreadArray_StringNotSpreadable_ThrowsRuntimeError()
    {
        RunExpectingError("let result = [...\"abc\"];");
    }

    [Fact]
    public void SpreadArray_NullNotSpreadable_ThrowsRuntimeError()
    {
        RunExpectingError("let result = [...null];");
    }

    // === Spread in Dict Literals (#27-35) ===

    [Fact]
    public void SpreadDict_SpreadIntoNew_MergesEntries()
    {
        var result = Run("let d = { a: 1 }; let result = { ...d, b: 2 };");
        var dict = Assert.IsType<StashDictionary>(result);
        Assert.Equal(2, dict.Count);
        Assert.Equal(1L, dict.Get("a").ToObject());
        Assert.Equal(2L, dict.Get("b").ToObject());
    }

    [Fact]
    public void SpreadDict_DuplicateKey_SecondWins()
    {
        var result = Run("let result = { ...{ a: 1 }, ...{ a: 2 } };");
        var dict = Assert.IsType<StashDictionary>(result);
        Assert.Equal(1, dict.Count);
        Assert.Equal(2L, dict.Get("a").ToObject());
    }

    [Fact]
    public void SpreadDict_SpreadThenLiteral_LiteralOverrides()
    {
        var result = Run("let result = { ...{ a: 1 }, a: 2 };");
        var dict = Assert.IsType<StashDictionary>(result);
        Assert.Equal(1, dict.Count);
        Assert.Equal(2L, dict.Get("a").ToObject());
    }

    [Fact]
    public void SpreadDict_LiteralThenSpread_SpreadOverrides()
    {
        var result = Run("let result = { a: 1, ...{ a: 2 } };");
        var dict = Assert.IsType<StashDictionary>(result);
        Assert.Equal(1, dict.Count);
        Assert.Equal(2L, dict.Get("a").ToObject());
    }

    [Fact]
    public void SpreadDict_EmptySpread_ProducesNormalDict()
    {
        var result = Run("let e = {}; let result = { ...e, k: \"v\" };");
        var dict = Assert.IsType<StashDictionary>(result);
        Assert.Equal(1, dict.Count);
        Assert.Equal("v", dict.Get("k").ToObject());
    }

    [Fact]
    public void SpreadDict_StructSpread_ExtractsFields()
    {
        var result = Run("struct S { x } let s = S { x: 1 }; let result = { ...s, y: 2 };");
        var dict = Assert.IsType<StashDictionary>(result);
        Assert.Equal(2, dict.Count);
        Assert.Equal(1L, dict.Get("x").ToObject());
        Assert.Equal(2L, dict.Get("y").ToObject());
    }

    [Fact]
    public void SpreadDict_NumberNotSpreadable_ThrowsRuntimeError()
    {
        RunExpectingError("let result = { ...5 };");
    }

    [Fact]
    public void SpreadDict_ArrayNotSpreadable_ThrowsRuntimeError()
    {
        RunExpectingError("let result = { ...[1, 2] };");
    }

    [Fact]
    public void SpreadDict_NullNotSpreadable_ThrowsRuntimeError()
    {
        RunExpectingError("let result = { ...null };");
    }

    // === Rest in Array Destructuring (#36-41) ===

    [Fact]
    public void RestDestructureArray_Basic_CapturesRemainder()
    {
        var result = Run("let [a, ...rest] = [1, 2, 3]; let result = rest;") as List<object?>;
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal(2L, result[0]);
        Assert.Equal(3L, result[1]);
    }

    [Fact]
    public void RestDestructureArray_OnlyOneElement_ReturnsEmptyRest()
    {
        var result = Run("let [a, ...rest] = [1]; let result = rest;") as List<object?>;
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void RestDestructureArray_RestOnly_CapturesAll()
    {
        var result = Run("let [...all] = [1, 2, 3]; let result = all;") as List<object?>;
        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
        Assert.Equal(1L, result[0]);
        Assert.Equal(2L, result[1]);
        Assert.Equal(3L, result[2]);
    }

    [Fact]
    public void RestDestructureArray_MorePatternsThanElements_NullForMissing()
    {
        var result = Run("let [a, b, ...rest] = [1]; let result = [a, b, rest];") as List<object?>;
        Assert.NotNull(result);
        Assert.Equal(1L, result[0]);
        Assert.Null(result[1]);
        var rest = result[2] as List<object?>;
        Assert.NotNull(rest);
        Assert.Empty(rest);
    }

    [Fact]
    public void RestDestructureArray_ConstDeclaration_RestIsReadable()
    {
        var result = Run("const [a, ...rest] = [1, 2]; let result = rest;") as List<object?>;
        Assert.NotNull(result);
        var single = Assert.Single(result);
        Assert.Equal(2L, single);
    }

    [Fact]
    public void RestDestructureArray_RestNotLast_ParseError()
    {
        RunExpectingParseError("let [...a, b] = [1, 2];");
    }

    // === Rest in Object Destructuring (#42-46) ===

    [Fact]
    public void RestDestructureDict_CapturesRemainingKeys()
    {
        var result = Run("let d = { a: 1, b: 2, c: 3 }; let { a, ...rest } = d; let result = rest;");
        var dict = Assert.IsType<StashDictionary>(result);
        Assert.Equal(2, dict.Count);
        Assert.Equal(2L, dict.Get("b").ToObject());
        Assert.Equal(3L, dict.Get("c").ToObject());
    }

    [Fact]
    public void RestDestructureDict_AllKeysExtracted_EmptyRest()
    {
        var result = Run("let d = { a: 1 }; let { a, ...rest } = d; let result = rest;");
        var dict = Assert.IsType<StashDictionary>(result);
        Assert.Equal(0, dict.Count);
    }

    [Fact]
    public void RestDestructureDict_RestOnly_CapturesAll()
    {
        var result = Run("let d = { x: 1, y: 2 }; let { ...all } = d; let result = all;");
        var dict = Assert.IsType<StashDictionary>(result);
        Assert.Equal(2, dict.Count);
        Assert.Equal(1L, dict.Get("x").ToObject());
        Assert.Equal(2L, dict.Get("y").ToObject());
    }

    [Fact]
    public void RestDestructureDict_StructSource_CapturesRemainingFields()
    {
        var result = Run("struct S { x, y } let s = S { x: 1, y: 2 }; let { x, ...r } = s; let result = r;");
        var dict = Assert.IsType<StashDictionary>(result);
        Assert.Equal(1, dict.Count);
        Assert.Equal(2L, dict.Get("y").ToObject());
    }

    [Fact]
    public void RestDestructureDict_RestNotLast_ParseError()
    {
        RunExpectingParseError("let { ...a, b } = { a: 1, b: 2 };");
    }

    // === Interaction Tests (#47-49) ===

    [Fact]
    public void SpreadCall_UfcsWithSpread_PassesArg()
    {
        var result = Run("let args = [\", \"]; let result = [1, 2, 3].join(...args);");
        Assert.Equal("1, 2, 3", result);
    }

    [Fact]
    public void RestParam_NestedForward_PassesThroughSpread()
    {
        var result = Run(
            "fn outer(...args) { fn inner(...xs) { return xs; } return inner(...args); } let result = outer(1, 2, 3);")
            as List<object?>;
        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
        Assert.Equal(1L, result[0]);
        Assert.Equal(2L, result[1]);
        Assert.Equal(3L, result[2]);
    }

    [Fact]
    public void SpreadArray_FunctionCallResult_SpreadReturnValue()
    {
        var result = Run("fn get() { return [1, 2, 3]; } let result = [...get(), 4];") as List<object?>;
        Assert.NotNull(result);
        Assert.Equal(4, result.Count);
        Assert.Equal(1L, result[0]);
        Assert.Equal(2L, result[1]);
        Assert.Equal(3L, result[2]);
        Assert.Equal(4L, result[3]);
    }
}
