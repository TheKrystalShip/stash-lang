using Stash.Lexing;
using Stash.Parsing;
using Stash.Bytecode;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Runtime.Types;

namespace Stash.Tests.Interpreting;

public class UfcsTests
{
    private static object? Run(string source)
    {
        string full = source + "\nreturn result;";
        var lexer = new Lexer(full, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(TestVM.CreateGlobals());
        return vm.Execute(chunk);
    }

    private static void RunExpectingError(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(TestVM.CreateGlobals());
        Assert.Throws<RuntimeError>(() => vm.Execute(chunk));
    }

    // ── 1. String UFCS — Basic Methods ────────────────────────────────────

    [Fact]
    public void StringUfcs_Upper_ReturnsUppercase()
    {
        var result = Run("let result = \"hello\".upper();");
        Assert.Equal("HELLO", result);
    }

    [Fact]
    public void StringUfcs_Lower_ReturnsLowercase()
    {
        var result = Run("let result = \"HELLO\".lower();");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void StringUfcs_Trim_RemovesWhitespace()
    {
        var result = Run("let result = \"  hello  \".trim();");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void StringUfcs_Capitalize_CapitalizesFirstLetter()
    {
        var result = Run("let result = \"hello world\".capitalize();");
        Assert.Equal("Hello world", result);
    }

    [Fact]
    public void StringUfcs_Title_TitleCasesAllWords()
    {
        var result = Run("let result = \"hello world\".title();");
        Assert.Equal("Hello World", result);
    }

    // ── 2. String UFCS — Methods with Arguments ───────────────────────────

    [Fact]
    public void StringUfcs_Split_ReturnsArray()
    {
        var result = Run("let result = \"hello world\".split(\" \");");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(2, list.Count);
        Assert.Equal("hello", list[0]);
        Assert.Equal("world", list[1]);
    }

    [Fact]
    public void StringUfcs_Contains_ReturnsTrueWhenFound()
    {
        var result = Run("let result = \"hello\".contains(\"ell\");");
        Assert.Equal(true, result);
    }

    [Fact]
    public void StringUfcs_StartsWith_ReturnsTrueWhenMatches()
    {
        var result = Run("let result = \"hello\".startsWith(\"hel\");");
        Assert.Equal(true, result);
    }

    [Fact]
    public void StringUfcs_EndsWith_ReturnsTrueWhenMatches()
    {
        var result = Run("let result = \"hello\".endsWith(\"llo\");");
        Assert.Equal(true, result);
    }

    [Fact]
    public void StringUfcs_IndexOf_ReturnsCorrectIndex()
    {
        var result = Run("let result = \"hello\".indexOf(\"ll\");");
        Assert.Equal(2L, result);
    }

    [Fact]
    public void StringUfcs_Replace_ReplacesSubstring()
    {
        var result = Run("let result = \"hello world\".replace(\"world\", \"stash\");");
        Assert.Equal("hello stash", result);
    }

    [Fact]
    public void StringUfcs_Repeat_RepeatsString()
    {
        var result = Run("let result = \"ha\".repeat(3);");
        Assert.Equal("hahaha", result);
    }

    [Fact]
    public void StringUfcs_PadStart_PadsWithCharacter()
    {
        var result = Run("let result = \"42\".padStart(5, \"0\");");
        Assert.Equal("00042", result);
    }

    [Fact]
    public void StringUfcs_Substring_ExtractsSlice()
    {
        var result = Run("let result = \"hello\".substring(0, 3);");
        Assert.Equal("hel", result);
    }

    // ── 3. String UFCS — Chaining ─────────────────────────────────────────

    [Fact]
    public void StringUfcs_ChainTrimUpper_ReturnsUppercaseTrimmed()
    {
        var result = Run("let result = \"  Hello, World!  \".trim().upper();");
        Assert.Equal("HELLO, WORLD!", result);
    }

    [Fact]
    public void StringUfcs_ChainTrimLower_ReturnsLowercaseTrimmed()
    {
        var result = Run("let result = \"  Hello, World!  \".trim().lower();");
        Assert.Equal("hello, world!", result);
    }

    [Fact]
    public void StringUfcs_ChainUpperSplit_ReturnsUppercaseArray()
    {
        var result = Run("let result = \"hello world\".upper().split(\" \");");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(2, list.Count);
        Assert.Equal("HELLO", list[0]);
        Assert.Equal("WORLD", list[1]);
    }

    [Fact]
    public void StringUfcs_ChainTrimReplaceUpper_ReturnsTransformedString()
    {
        var result = Run("let result = \" hello \".trim().replace(\"hello\", \"world\").upper();");
        Assert.Equal("WORLD", result);
    }

    // ── 4. Array UFCS — Mutating Methods ─────────────────────────────────

    [Fact]
    public void ArrayUfcs_Push_MutatesArrayInPlace()
    {
        var result = Run(@"
let a = [1, 2, 3];
a.push(4);
let result = a;
");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(4, list.Count);
        Assert.Equal(4L, list[3]);
    }

    [Fact]
    public void ArrayUfcs_Pop_ReturnsLastElement()
    {
        var result = Run(@"
let a = [1, 2, 3];
let result = a.pop();
");
        Assert.Equal(3L, result);
    }

    // ── 5. Array UFCS — Functional Methods ───────────────────────────────

    [Fact]
    public void ArrayUfcs_Sort_SortsInPlace()
    {
        var result = Run(@"
            let a = [3, 1, 2];
            a.sort();
            let result = a;
        ");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
        Assert.Equal(1L, list[0]);
        Assert.Equal(2L, list[1]);
        Assert.Equal(3L, list[2]);
    }

    [Fact]
    public void ArrayUfcs_Map_TransformsElements()
    {
        var result = Run("let result = [1, 2, 3].map((x) => x * 2);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
        Assert.Equal(2L, list[0]);
        Assert.Equal(4L, list[1]);
        Assert.Equal(6L, list[2]);
    }

    [Fact]
    public void ArrayUfcs_Filter_FiltersElements()
    {
        var result = Run("let result = [1, 2, 3, 4].filter((x) => x > 2);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(2, list.Count);
        Assert.Equal(3L, list[0]);
        Assert.Equal(4L, list[1]);
    }

    [Fact]
    public void ArrayUfcs_Contains_ReturnsTrueWhenFound()
    {
        var result = Run("let result = [1, 2, 3].contains(2);");
        Assert.Equal(true, result);
    }

    [Fact]
    public void ArrayUfcs_IndexOf_ReturnsCorrectIndex()
    {
        var result = Run("let result = [1, 2, 3].indexOf(2);");
        Assert.Equal(1L, result);
    }

    [Fact]
    public void ArrayUfcs_Reverse_ReversesInPlace()
    {
        var result = Run(@"
            let a = [1, 2, 3];
            a.reverse();
            let result = a;
        ");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
        Assert.Equal(3L, list[0]);
        Assert.Equal(2L, list[1]);
        Assert.Equal(1L, list[2]);
    }

    [Fact]
    public void ArrayUfcs_Join_ReturnsJoinedString()
    {
        var result = Run("let result = [1, 2, 3].join(\", \");");
        Assert.Equal("1, 2, 3", result);
    }

    // ── 6. Array UFCS — Chaining ──────────────────────────────────────────

    [Fact]
    public void ArrayUfcs_ChainSortReverse_SortsAndReversesInPlace()
    {
        var result = Run(@"
            let a = [3, 1, 2];
            a.sort();
            a.reverse();
            let result = a;
        ");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
        Assert.Equal(3L, list[0]);
        Assert.Equal(2L, list[1]);
        Assert.Equal(1L, list[2]);
    }

    [Fact]
    public void ArrayUfcs_ChainFilterMap_TransformsFilteredElements()
    {
        var result = Run("let result = [1, 2, 3, 4, 5].filter((x) => x > 2).map((x) => x * 10);");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
        Assert.Equal(30L, list[0]);
        Assert.Equal(40L, list[1]);
        Assert.Equal(50L, list[2]);
    }

    // ── 7. Mixed Chaining (UFCS + namespace calls) ────────────────────────

    [Fact]
    public void MixedChaining_NamespaceCallThenUfcs_Works()
    {
        var result = Run("let result = str.trim(\"  hello  \").upper();");
        Assert.Equal("HELLO", result);
    }

    [Fact]
    public void MixedChaining_StringUfcsSplitThenArrayUfcsJoin_Works()
    {
        var result = Run("let result = \"hello,world\".split(\",\").join(\" \");");
        Assert.Equal("hello world", result);
    }

    // ── 8. UFCS on Variables ──────────────────────────────────────────────

    [Fact]
    public void UfcsOnVariable_StringVariable_Upper_ReturnsUppercase()
    {
        var result = Run(@"
let s = ""hello"";
let result = s.upper();
");
        Assert.Equal("HELLO", result);
    }

    [Fact]
    public void UfcsOnVariable_ArrayVariable_Sort_SortsInPlace()
    {
        var result = Run(@"
let a = [3, 1, 2];
a.sort();
let result = a;
");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
        Assert.Equal(1L, list[0]);
        Assert.Equal(2L, list[1]);
        Assert.Equal(3L, list[2]);
    }

    // ── 9. UFCS on Expression Results ─────────────────────────────────────

    [Fact]
    public void UfcsOnExpressionResult_StringConcatenation_Upper_ReturnsUppercase()
    {
        var result = Run("let result = (\"hel\" + \"lo\").upper();");
        Assert.Equal("HELLO", result);
    }

    // ── 10. Equivalence Tests ─────────────────────────────────────────────

    [Fact]
    public void Equivalence_UfcsAndNamespaceCallProduceSameResult()
    {
        var result = Run(@"
let a = str.upper(""hello"");
let b = ""hello"".upper();
let result = a == b;
");
        Assert.Equal(true, result);
    }

    // ── 11. Error Cases ───────────────────────────────────────────────────

    [Fact]
    public void ErrorCase_UfcsOnNull_ThrowsRuntimeError()
    {
        RunExpectingError(@"
let x = null;
let result = x.upper();
");
    }

    [Fact]
    public void ErrorCase_UfcsNonexistentMethodOnString_ThrowsRuntimeError()
    {
        RunExpectingError("let result = \"hello\".nonexistent();");
    }

    [Fact]
    public void ErrorCase_DictDotAccess_ResolvesToKeyLookupNotUfcs()
    {
        var result = Run(@"
let d = dict.new();
dict.set(d, ""upper"", ""value"");
let result = d.upper;
");
        Assert.Equal("value", result);
    }

    [Fact]
    public void ErrorCase_UfcsOnInt_ThrowsRuntimeError()
    {
        RunExpectingError(@"
let x = 42;
let result = x.abs();
");
    }

    [Fact]
    public void ErrorCase_UfcsOnBool_ThrowsRuntimeError()
    {
        RunExpectingError(@"
let x = true;
let result = x.toStr();
");
    }

    // ── 12. Optional Chaining with UFCS ───────────────────────────────────

    [Fact]
    public void OptionalChaining_UfcsOnNull_ReturnsNull()
    {
        var result = Run(@"
let x = null;
let result = x?.upper;
");
        Assert.Null(result);
    }

    // ── 13. String UFCS — Regex Methods ───────────────────────────────────

    [Fact]
    public void StringUfcs_IsMatch_ReturnsTrueWhenPatternMatches()
    {
        var result = Run("let result = \"hello123\".isMatch(\"[0-9]+\");");
        Assert.Equal(true, result);
    }

    [Fact]
    public void StringUfcs_Match_ReturnsMatchedString()
    {
        var result = Run("let result = \"hello\".match(\"[a-z]+\");");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void Priority_StructFieldShadowsUfcsMethod()
    {
        var result = Run(@"
            struct MyObj { upper }
            let obj = MyObj { upper: ""custom value"" };
            let result = obj.upper;
        ");
        Assert.Equal("custom value", result);
    }

    [Fact]
    public void ErrorCase_ChainingOnVoidReturningMethod_Throws()
    {
        RunExpectingError(@"
            let a = [3, 1, 2];
            a.sort().reverse();
        ");
    }
}
