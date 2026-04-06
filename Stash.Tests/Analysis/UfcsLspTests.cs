using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Analysis;
using Stash.Stdlib;

namespace Stash.Tests.Analysis;

public class UfcsLspTests : AnalysisTestBase
{
    private static (ScopeTree tree, List<Stmt> stmts) AnalyzeWithInference(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        var collector = new SymbolCollector { IncludeBuiltIns = true };
        var tree = collector.Collect(stmts);
        TypeInferenceEngine.InferTypes(tree, stmts);
        return (tree, stmts);
    }

    // ── UFCS Reference Recording ──────────────────────────────────────────

    [Fact]
    public void UfcsReference_ExplicitStringType_RecordsCallReference()
    {
        const string src = """
            let s: string = "hello";
            s.upper();
            """;
        var tree = Analyze(src);
        Assert.Contains(tree.References, r => r.Name == "str.upper" && r.Kind == ReferenceKind.Call);
    }

    [Fact]
    public void UfcsReference_ExplicitArrayType_RecordsCallReference()
    {
        const string src = """
            let a: array = [1, 2, 3];
            a.push(4);
            """;
        var tree = Analyze(src);
        Assert.Contains(tree.References, r => r.Name == "arr.push" && r.Kind == ReferenceKind.Call);
    }

    [Fact]
    public void UfcsReference_InferredType_NoReference()
    {
        // Inferred types don't have TypeHint set during SymbolCollector — acceptable limitation
        const string src = """
            let s = "hello";
            s.upper();
            """;
        var tree = Analyze(src);
        Assert.DoesNotContain(tree.References, r => r.Name == "str.upper" && r.Kind == ReferenceKind.Call);
    }

    [Fact]
    public void UfcsReference_DictType_NoReference()
    {
        // Dict is NOT UFCS-eligible
        const string src = """
            let d: dict = {};
            d.keys();
            """;
        var tree = Analyze(src);
        Assert.DoesNotContain(tree.References, r => r.Name == "dict.keys" && r.Kind == ReferenceKind.Call);
    }

    [Fact]
    public void UfcsReference_IntType_NoReference()
    {
        const string src = """
            let n: int = 42;
            n.toString();
            """;
        var tree = Analyze(src);
        Assert.DoesNotContain(tree.References, r => r.Kind == ReferenceKind.Call && r.Name.Contains('.'));
    }

    [Fact]
    public void UfcsReference_NonExistentMethod_NoReference()
    {
        const string src = """
            let s: string = "hello";
            s.nonExistentMethod();
            """;
        var tree = Analyze(src);
        Assert.DoesNotContain(tree.References, r => r.Name.StartsWith("str.") && r.Kind == ReferenceKind.Call);
    }

    [Fact]
    public void UfcsReference_MultipleCallsRecorded()
    {
        const string src = """
            let s: string = "hello";
            s.upper();
            s.lower();
            s.trim();
            """;
        var tree = Analyze(src);
        Assert.Contains(tree.References, r => r.Name == "str.upper" && r.Kind == ReferenceKind.Call);
        Assert.Contains(tree.References, r => r.Name == "str.lower" && r.Kind == ReferenceKind.Call);
        Assert.Contains(tree.References, r => r.Name == "str.trim" && r.Kind == ReferenceKind.Call);
    }

    [Fact]
    public void UfcsReference_ArrayMapFilter_RecordsCallReferences()
    {
        const string src = """
            let a: array = [1, 2, 3];
            a.map((x) => x * 2);
            a.filter((x) => x > 1);
            """;
        var tree = Analyze(src);
        Assert.Contains(tree.References, r => r.Name == "arr.map" && r.Kind == ReferenceKind.Call);
        Assert.Contains(tree.References, r => r.Name == "arr.filter" && r.Kind == ReferenceKind.Call);
    }

    // ── UFCS Type Resolution ──────────────────────────────────────────────

    [Fact]
    public void UfcsTypeResolution_StringLiteral_InfersStringType()
    {
        const string src = """let s = "hello";""";
        var (tree, _) = AnalyzeWithInference(src);
        var symbol = tree.FindDefinition("s", 1, 5);
        Assert.NotNull(symbol);
        Assert.Equal("string", symbol!.TypeHint);
    }

    [Fact]
    public void UfcsTypeResolution_ArrayLiteral_InfersArrayType()
    {
        const string src = "let a = [1, 2, 3];";
        var (tree, _) = AnalyzeWithInference(src);
        var symbol = tree.FindDefinition("a", 1, 5);
        Assert.NotNull(symbol);
        Assert.Equal("array", symbol!.TypeHint);
    }

    // ── StdlibRegistry UFCS API Tests ─────────────────────────────────────

    [Fact]
    public void StdlibRegistry_GetUfcsNamespace_StringReturnsStr()
    {
        Assert.Equal("str", StdlibRegistry.GetUfcsNamespace("string"));
    }

    [Fact]
    public void StdlibRegistry_GetUfcsNamespace_ArrayReturnsArr()
    {
        Assert.Equal("arr", StdlibRegistry.GetUfcsNamespace("array"));
    }

    [Fact]
    public void StdlibRegistry_GetUfcsNamespace_DictReturnsNull()
    {
        Assert.Null(StdlibRegistry.GetUfcsNamespace("dict"));
    }

    [Fact]
    public void StdlibRegistry_GetUfcsNamespace_IntReturnsNull()
    {
        Assert.Null(StdlibRegistry.GetUfcsNamespace("int"));
    }

    [Fact]
    public void StdlibRegistry_HasUfcsSupport_TrueForString()
    {
        Assert.True(StdlibRegistry.HasUfcsSupport("string"));
    }

    [Fact]
    public void StdlibRegistry_HasUfcsSupport_TrueForArray()
    {
        Assert.True(StdlibRegistry.HasUfcsSupport("array"));
    }

    [Fact]
    public void StdlibRegistry_HasUfcsSupport_FalseForDict()
    {
        Assert.False(StdlibRegistry.HasUfcsSupport("dict"));
    }

    [Fact]
    public void StdlibRegistry_UfcsNamespaceFunctions_StrHasUpper()
    {
        Assert.True(StdlibRegistry.TryGetNamespaceFunction("str.upper", out var fn));
        Assert.NotNull(fn);
        Assert.True(fn!.Parameters.Length >= 1);
    }

    [Fact]
    public void StdlibRegistry_UfcsNamespaceFunctions_ArrHasMap()
    {
        Assert.True(StdlibRegistry.TryGetNamespaceFunction("arr.map", out var fn));
        Assert.NotNull(fn);
        Assert.True(fn!.Parameters.Length >= 1);
    }
}
