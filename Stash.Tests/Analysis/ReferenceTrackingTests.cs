using Stash.Lexing;
using Stash.Parsing;
using Stash.Analysis;
using Stash.Lsp.Analysis;

namespace Stash.Tests.Analysis;

public class ReferenceTrackingTests
{
    private static ScopeTree Analyze(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        var collector = new SymbolCollector();
        return collector.Collect(stmts);
    }

    [Fact]
    public void IdentifierExpr_RecordsReadReference()
    {
        var tree = Analyze("let x = 1; let y = x;");

        Assert.Contains(tree.References, r => r.Name == "x" && r.Kind == ReferenceKind.Read);
    }

    [Fact]
    public void AssignExpr_RecordsWriteReference()
    {
        var tree = Analyze("let x = 1; x = 2;");

        Assert.Contains(tree.References, r => r.Name == "x" && r.Kind == ReferenceKind.Write);
    }

    [Fact]
    public void CallExpr_RecordsCallReference()
    {
        var tree = Analyze("fn greet() {} greet();");

        Assert.Contains(tree.References, r => r.Name == "greet" && r.Kind == ReferenceKind.Call);
    }

    [Fact]
    public void StructInit_RecordsTypeUseReference()
    {
        var tree = Analyze("struct Point { x, y } let p = Point { x: 1, y: 2 };");

        Assert.Contains(tree.References, r => r.Name == "Point" && r.Kind == ReferenceKind.TypeUse);
    }

    [Fact]
    public void UpdateExpr_RecordsWriteReference()
    {
        var tree = Analyze("let x = 1; x++;");

        Assert.Contains(tree.References, r => r.Name == "x" && r.Kind == ReferenceKind.Write);
    }

    [Fact]
    public void References_ResolveToCorrectDeclaration()
    {
        // The Read reference for x in "let y = x" should resolve to the declaration of x.
        var tree = Analyze("let x = 1; let y = x;");

        var readRef = tree.References.Single(r => r.Name == "x" && r.Kind == ReferenceKind.Read);
        Assert.NotNull(readRef.ResolvedSymbol);
        Assert.Equal("x", readRef.ResolvedSymbol!.Name);
        Assert.Equal(1, readRef.ResolvedSymbol!.Span.StartLine);
    }

    [Fact]
    public void FindReferences_ReturnsAllUsages()
    {
        // Line 1: let x = 1;
        // Line 2: let y = x;   <- Read reference
        // Line 3: x = 3;       <- Write reference
        const string src = "let x = 1;\nlet y = x;\nx = 3;";
        var tree = Analyze(src);

        // x is declared at line 1, col 5 (1-based: "let " = 4 chars, x at col 5)
        var refs = tree.FindReferences("x", 1, 5);

        // declaration (as Write) + 1 Read + 1 Write = 3
        Assert.Equal(3, refs.Count);
    }

    [Fact]
    public void FindReferences_RespectsShadowing()
    {
        // Line 1: let x = 1;
        // Line 2: fn foo() {
        // Line 3:     let x = 2;   <- inner x, col 9
        // Line 4:     let y = x;   <- resolves to inner x
        // Line 5: }
        const string src = "let x = 1;\nfn foo() {\n    let x = 2;\n    let y = x;\n}";
        var tree = Analyze(src);

        // FindReferences from inner x position (line 3, col 9)
        var refs = tree.FindReferences("x", 3, 9);

        // inner x declaration (as Write) + 1 Read reference = 2
        Assert.Equal(2, refs.Count);
        // None of the references should resolve to the outer x (declared at line 1)
        Assert.DoesNotContain(refs, r => r.ResolvedSymbol != null && r.ResolvedSymbol.Span.StartLine == 1);
    }

    [Fact]
    public void NestedFunctionCall_RecordsCallReference()
    {
        var tree = Analyze("fn add(a, b) { return a + b; } let result = add(1, 2);");

        Assert.Contains(tree.References, r => r.Name == "add" && r.Kind == ReferenceKind.Call);
        Assert.Contains(tree.References, r => r.Name == "a" && r.Kind == ReferenceKind.Read);
        Assert.Contains(tree.References, r => r.Name == "b" && r.Kind == ReferenceKind.Read);
    }

    [Fact]
    public void UnresolvedReferences_DetectedForUndefinedVariables()
    {
        var tree = Analyze("let x = y;");

        var unresolved = tree.GetUnresolvedReferences();
        Assert.Contains(unresolved, r => r.Name == "y");
    }

    [Fact]
    public void UnresolvedReferences_ExcludesKnownBuiltIns()
    {
        var tree = Analyze("let x = typeof(1);");

        var unresolved = tree.GetUnresolvedReferences(new HashSet<string> { "typeof" });
        Assert.Empty(unresolved);
    }

    [Fact]
    public void BinaryExpr_RecordsReferencesInBothSides()
    {
        var tree = Analyze("let a = 1; let b = 2; let c = a + b;");

        Assert.Contains(tree.References, r => r.Name == "a" && r.Kind == ReferenceKind.Read);
        Assert.Contains(tree.References, r => r.Name == "b" && r.Kind == ReferenceKind.Read);
    }

    [Fact]
    public void ForInLoop_RecordsIterableReference()
    {
        var tree = Analyze("let items = [1, 2, 3]; for (let item in items) {}");

        Assert.Contains(tree.References, r => r.Name == "items" && r.Kind == ReferenceKind.Read);
    }

    [Fact]
    public void InterpolatedString_RecordsReferences()
    {
        var tree = Analyze("let name = \"world\"; let msg = \"hello ${name}\";");

        Assert.Contains(tree.References, r => r.Name == "name" && r.Kind == ReferenceKind.Read);
    }
}
