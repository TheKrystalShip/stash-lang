using System.Linq;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Analysis;
using Stash.Lsp.Analysis;

namespace Stash.Tests.Analysis;

public class TypeDefinitionAndImplementationTests
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

    private static (ScopeTree Tree, System.Collections.Generic.List<Stmt> Stmts) AnalyzeWithStmts(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        var collector = new SymbolCollector();
        var tree = collector.Collect(stmts);
        return (tree, stmts);
    }

    // --- TypeDefinition tests ---

    [Fact]
    public void TypeDefinition_Variable_WithStructType_FindsStructDecl()
    {
        var source = "struct Server { host, port }\nlet srv = Server { host: \"localhost\", port: 8080 };";
        var (tree, stmts) = AnalyzeWithStmts(source);
        TypeInferenceEngine.InferTypes(tree, stmts);

        var srv = tree.FindDefinition("srv", 2, 5);
        Assert.NotNull(srv);
        Assert.Equal("Server", srv!.TypeHint);

        var structSymbol = tree.All.FirstOrDefault(s => s.Name == "Server" && s.Kind == SymbolKind.Struct);
        Assert.NotNull(structSymbol);
    }

    [Fact]
    public void TypeDefinition_Parameter_WithTypeHint_FindsStructDecl()
    {
        var source = "struct Config { path }\nfn setup(cfg: Config) {\n  let x = cfg;\n}";
        var tree = Analyze(source);

        // Find cfg from inside the function body where it is visible
        var cfg = tree.FindDefinition("cfg", 3, 11);
        Assert.NotNull(cfg);
        Assert.Equal(SymbolKind.Parameter, cfg!.Kind);
        Assert.Equal("Config", cfg.TypeHint);

        var structSymbol = tree.All.FirstOrDefault(s => s.Name == "Config" && s.Kind == SymbolKind.Struct);
        Assert.NotNull(structSymbol);
    }

    [Fact]
    public void TypeDefinition_StructSymbol_ReturnsItself()
    {
        var source = "struct Point { x, y }";
        var tree = Analyze(source);

        var point = tree.FindDefinition("Point", 1, 8);
        Assert.NotNull(point);
        Assert.Equal(SymbolKind.Struct, point!.Kind);
        Assert.Equal("Point", point.Name);
    }

    [Fact]
    public void TypeDefinition_EnumSymbol_ReturnsItself()
    {
        var source = "enum Color { Red, Green, Blue }";
        var tree = Analyze(source);

        var color = tree.FindDefinition("Color", 1, 6);
        Assert.NotNull(color);
        Assert.Equal(SymbolKind.Enum, color!.Kind);
        Assert.Equal("Color", color.Name);
    }

    [Fact]
    public void TypeDefinition_Variable_PrimitiveType_NoStructFound()
    {
        var source = "let x = 42;";
        var (tree, stmts) = AnalyzeWithStmts(source);
        TypeInferenceEngine.InferTypes(tree, stmts);

        var x = tree.FindDefinition("x", 1, 5);
        Assert.NotNull(x);
        Assert.Equal("int", x!.TypeHint);

        // Primitive types don't have struct/enum declarations
        var typeSymbol = tree.All.FirstOrDefault(s => s.Name == "int" && s.Kind is SymbolKind.Struct or SymbolKind.Enum);
        Assert.Null(typeSymbol);
    }

    [Fact]
    public void TypeDefinition_Constant_WithStructType()
    {
        var source = "struct Settings { debug }\nconst cfg = Settings { debug: true };";
        var (tree, stmts) = AnalyzeWithStmts(source);
        TypeInferenceEngine.InferTypes(tree, stmts);

        var cfg = tree.FindDefinition("cfg", 2, 7);
        Assert.NotNull(cfg);
        Assert.Equal("Settings", cfg!.TypeHint);

        var structSymbol = tree.All.FirstOrDefault(s => s.Name == "Settings" && s.Kind == SymbolKind.Struct);
        Assert.NotNull(structSymbol);
    }

    // --- Implementation tests ---

    [Fact]
    public void Implementation_Struct_FindsAllInstantiations()
    {
        var source = "struct Point { x, y }\nlet p1 = Point { x: 1, y: 2 };\nlet p2 = Point { x: 3, y: 4 };";
        var tree = Analyze(source);

        var typeUseRefs = tree.References.Where(r => r.Name == "Point" && r.Kind == ReferenceKind.TypeUse).ToList();
        Assert.Equal(2, typeUseRefs.Count);
    }

    [Fact]
    public void Implementation_Struct_NoInstantiations_ReturnsEmpty()
    {
        var source = "struct Empty { }";
        var tree = Analyze(source);

        var typeUseRefs = tree.References.Where(r => r.Name == "Empty" && r.Kind == ReferenceKind.TypeUse).ToList();
        Assert.Empty(typeUseRefs);
    }

    [Fact]
    public void Implementation_Enum_NoTypeUseReferences()
    {
        var source = "enum Status { Active, Inactive }\nlet s = Status.Active;";
        var tree = Analyze(source);

        // Enum member access does not create TypeUse references
        var typeUseRefs = tree.References.Where(r => r.Name == "Status" && r.Kind == ReferenceKind.TypeUse).ToList();
        Assert.Empty(typeUseRefs);
    }

    [Fact]
    public void Implementation_Variable_WithStructType_FindsUsages()
    {
        var source = "struct Vec { x, y }\nlet v1 = Vec { x: 1, y: 2 };\nlet v2 = Vec { x: 3, y: 4 };";
        var (tree, stmts) = AnalyzeWithStmts(source);
        TypeInferenceEngine.InferTypes(tree, stmts);

        var v1 = tree.FindDefinition("v1", 2, 5);
        Assert.NotNull(v1);
        Assert.Equal("Vec", v1!.TypeHint);

        var typeUseRefs = tree.References.Where(r => r.Name == "Vec" && r.Kind == ReferenceKind.TypeUse).ToList();
        Assert.Equal(2, typeUseRefs.Count);
    }
}
