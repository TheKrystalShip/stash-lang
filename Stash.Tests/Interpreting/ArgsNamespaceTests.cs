using Stash.Lexing;
using Stash.Parsing;
using Stash.Bytecode;
using Stash.Resolution;
using Stash.Stdlib;

namespace Stash.Tests.Interpreting;

public class ArgsNamespaceTests
{
    private static object? RunWithArgs(string source, string[] scriptArgs)
    {
        string full = source + "\nreturn result;";
        var lexer = new Lexer(full, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        vm.ScriptArgs = scriptArgs;
        return vm.Execute(chunk);
    }

    // =========================================================================
    // args.list()
    // =========================================================================

    [Fact]
    public void List_WithNoArgs_ReturnsEmptyArray()
    {
        var source = """
            let result = len(args.list());
            """;
        Assert.Equal(0L, RunWithArgs(source, []));
    }

    [Fact]
    public void List_WithMultipleArgs_ReturnsAllArgs()
    {
        var source = """
            let result = len(args.list());
            """;
        Assert.Equal(2L, RunWithArgs(source, ["hello", "world"]));
    }

    [Fact]
    public void List_ReturnsCorrectValues()
    {
        var source = """
            let a = args.list();
            let result = a[0];
            """;
        Assert.Equal("hello", RunWithArgs(source, ["hello", "world"]));
    }

    [Fact]
    public void List_ReturnsSecondArg()
    {
        var source = """
            let a = args.list();
            let result = a[1];
            """;
        Assert.Equal("world", RunWithArgs(source, ["hello", "world"]));
    }

    [Fact]
    public void List_ReturnsCopy_MutationDoesNotAffectOriginal()
    {
        var source = """
            let a = args.list();
            arr.push(a, "extra");
            let b = args.list();
            let result = len(b);
            """;
        Assert.Equal(2L, RunWithArgs(source, ["hello", "world"]));
    }

    // =========================================================================
    // args.count()
    // =========================================================================

    [Fact]
    public void Count_WithNoArgs_ReturnsZero()
    {
        var source = """
            let result = args.count();
            """;
        Assert.Equal(0L, RunWithArgs(source, []));
    }

    [Fact]
    public void Count_WithArgs_ReturnsCorrectCount()
    {
        var source = """
            let result = args.count();
            """;
        Assert.Equal(3L, RunWithArgs(source, ["a", "b", "c"]));
    }

    // =========================================================================
    // args.parse()
    // =========================================================================

    [Fact]
    public void Parse_BasicFlag_SetsTrue()
    {
        var source = """
            let parsed = args.parse({
                flags: { verbose: { description: "Verbose mode" } }
            });
            let result = parsed.verbose;
            """;
        Assert.Equal(true, RunWithArgs(source, ["--verbose"]));
    }

    [Fact]
    public void Parse_BasicOption_SetsValue()
    {
        var source = """
            let parsed = args.parse({
                options: { port: { type: "int", description: "Port" } }
            });
            let result = parsed.port;
            """;
        Assert.Equal(8080L, RunWithArgs(source, ["--port", "8080"]));
    }

    // =========================================================================
    // Namespace accessibility
    // =========================================================================

    [Fact]
    public void Namespace_IsAccessible()
    {
        var source = """
            let result = typeof(args);
            """;
        Assert.Equal("namespace", RunWithArgs(source, []));
    }
}
