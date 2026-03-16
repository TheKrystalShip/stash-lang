using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Lsp.Analysis;

namespace Stash.Tests.Analysis;

public class TypeInferenceTests
{
    private static (ScopeTree tree, List<Stmt> stmts) AnalyzeWithStatements(string source)
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

    [Fact]
    public void InfersType_FromStructInitialization()
    {
        const string src = "struct Server { host, port }\nlet s = Server { host: \"localhost\", port: 80 };";
        var (tree, _) = AnalyzeWithStatements(src);
        var symbol = tree.FindDefinition("s", 2, 5);
        Assert.NotNull(symbol);
        Assert.Equal("Server", symbol.TypeHint);
    }

    [Fact]
    public void InfersType_FromCommandExpression()
    {
        const string src = "let result = $(ls -la);";
        var (tree, _) = AnalyzeWithStatements(src);
        var symbol = tree.FindDefinition("result", 1, 5);
        Assert.NotNull(symbol);
        Assert.Equal("CommandResult", symbol.TypeHint);
    }

    [Fact]
    public void InfersType_FromFunctionReturnType()
    {
        const string src = "fn getServer() -> Server {\n}\nlet s = getServer();";
        var (tree, _) = AnalyzeWithStatements(src);
        var symbol = tree.FindDefinition("s", 3, 5);
        Assert.NotNull(symbol);
        Assert.Equal("Server", symbol.TypeHint);
    }

    [Fact]
    public void InfersType_FromBuiltInNamespaceFunction()
    {
        // http.get returns HttpResponse according to BuiltInRegistry
        const string src = "let resp = http.get(\"https://example.com\");";
        var (tree, _) = AnalyzeWithStatements(src);
        var symbol = tree.FindDefinition("resp", 1, 5);
        Assert.NotNull(symbol);
        Assert.Equal("HttpResponse", symbol.TypeHint);
    }

    [Fact]
    public void InfersType_FromProcessSpawn()
    {
        // process.spawn returns Process
        const string src = "let proc = process.spawn(\"sleep 10\");";
        var (tree, _) = AnalyzeWithStatements(src);
        var symbol = tree.FindDefinition("proc", 1, 5);
        Assert.NotNull(symbol);
        Assert.Equal("Process", symbol.TypeHint);
    }

    [Fact]
    public void InfersType_FromTypedVariable()
    {
        const string src = "struct Config { name }\nlet a = Config { name: \"test\" };\nlet b = a;";
        var (tree, _) = AnalyzeWithStatements(src);
        var symbolB = tree.FindDefinition("b", 3, 5);
        Assert.NotNull(symbolB);
        Assert.Equal("Config", symbolB.TypeHint);
    }

    [Fact]
    public void InfersType_ThroughTryExpression()
    {
        const string src = "let result = try $(echo hello);";
        var (tree, _) = AnalyzeWithStatements(src);
        var symbol = tree.FindDefinition("result", 1, 5);
        Assert.NotNull(symbol);
        Assert.Equal("CommandResult", symbol.TypeHint);
    }

    [Fact]
    public void InfersType_IntLiteral()
    {
        const string src = "let x = 42;";
        var (tree, _) = AnalyzeWithStatements(src);
        var symbol = tree.FindDefinition("x", 1, 5);
        Assert.NotNull(symbol);
        Assert.Equal("int", symbol.TypeHint);
    }

    [Fact]
    public void InfersType_FloatLiteral()
    {
        const string src = "let x = 3.14;";
        var (tree, _) = AnalyzeWithStatements(src);
        var symbol = tree.FindDefinition("x", 1, 5);
        Assert.NotNull(symbol);
        Assert.Equal("float", symbol.TypeHint);
    }

    [Fact]
    public void InfersType_StringLiteral()
    {
        const string src = "let x = \"hello\";";
        var (tree, _) = AnalyzeWithStatements(src);
        var symbol = tree.FindDefinition("x", 1, 5);
        Assert.NotNull(symbol);
        Assert.Equal("string", symbol.TypeHint);
    }

    [Fact]
    public void InfersType_BoolLiteral()
    {
        const string src = "let x = true;";
        var (tree, _) = AnalyzeWithStatements(src);
        var symbol = tree.FindDefinition("x", 1, 5);
        Assert.NotNull(symbol);
        Assert.Equal("bool", symbol.TypeHint);
    }

    [Fact]
    public void InfersType_NullLiteral()
    {
        const string src = "let x = null;";
        var (tree, _) = AnalyzeWithStatements(src);
        var symbol = tree.FindDefinition("x", 1, 5);
        Assert.NotNull(symbol);
        Assert.Equal("null", symbol.TypeHint);
    }

    [Fact]
    public void InfersType_ArrayLiteral()
    {
        const string src = "let items = [1, 2, 3];";
        var (tree, _) = AnalyzeWithStatements(src);
        var symbol = tree.FindDefinition("items", 1, 5);
        Assert.NotNull(symbol);
        Assert.Equal("array", symbol.TypeHint);
    }

    [Fact]
    public void InfersType_ConstDeclaration()
    {
        const string src = "const MAX = 100;";
        var (tree, _) = AnalyzeWithStatements(src);
        var symbol = tree.FindDefinition("MAX", 1, 7);
        Assert.NotNull(symbol);
        Assert.Equal("int", symbol.TypeHint);
    }

    [Fact]
    public void DoesNotOverride_ExplicitTypeHint()
    {
        const string src = "let x: string = 42;";  // explicit type wins even if "wrong"
        var (tree, _) = AnalyzeWithStatements(src);
        var symbol = tree.FindDefinition("x", 1, 5);
        Assert.NotNull(symbol);
        Assert.Equal("string", symbol.TypeHint);  // Explicit is preserved
    }

    [Fact]
    public void InfersType_InsideFunctionBody()
    {
        const string src = "fn test() {\n  let result = $(whoami);\n}";
        var (tree, _) = AnalyzeWithStatements(src);
        // result is on line 2, col 7 (inside function scope)
        var symbol = tree.FindDefinition("result", 2, 7);
        Assert.NotNull(symbol);
        Assert.Equal("CommandResult", symbol.TypeHint);
    }

    [Fact]
    public void NoInference_WithoutInitializer()
    {
        const string src = "let x;";
        var (tree, _) = AnalyzeWithStatements(src);
        var symbol = tree.FindDefinition("x", 1, 5);
        Assert.NotNull(symbol);
        Assert.Null(symbol.TypeHint);
    }

    [Fact]
    public void InfersType_ChainedVariableAssignment()
    {
        const string src = "let a = 42;\nlet b = a;\nlet c = b;";
        var (tree, _) = AnalyzeWithStatements(src);
        var symbolA = tree.FindDefinition("a", 1, 5);
        var symbolB = tree.FindDefinition("b", 2, 5);
        var symbolC = tree.FindDefinition("c", 3, 5);
        Assert.Equal("int", symbolA!.TypeHint);
        Assert.Equal("int", symbolB!.TypeHint);
        Assert.Equal("int", symbolC!.TypeHint);
    }

    [Fact]
    public void InfersType_FromBuiltInFunction()
    {
        // typeof() returns "string" per BuiltInRegistry
        const string src = "let t = typeof(42);";
        var (tree, _) = AnalyzeWithStatements(src);
        var symbol = tree.FindDefinition("t", 1, 5);
        Assert.NotNull(symbol);
        Assert.Equal("string", symbol.TypeHint);
    }

    [Fact]
    public void InfersType_FromLenFunction()
    {
        // len() returns "int" per BuiltInRegistry
        const string src = "let n = len(\"hello\");";
        var (tree, _) = AnalyzeWithStatements(src);
        var symbol = tree.FindDefinition("n", 1, 5);
        Assert.NotNull(symbol);
        Assert.Equal("int", symbol.TypeHint);
    }

    [Fact]
    public void InfersType_ProcessWaitReturnsCommandResult()
    {
        const string src = "let proc = process.spawn(\"sleep 1\");\nlet result = process.wait(proc);";
        var (tree, _) = AnalyzeWithStatements(src);
        var symbol = tree.FindDefinition("result", 2, 5);
        Assert.NotNull(symbol);
        Assert.Equal("CommandResult", symbol.TypeHint);
    }
}
