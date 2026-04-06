using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Stdlib;

namespace Stash.Tests.Interpreting;

public class JsonBuiltInsTests
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
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
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
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        Assert.Throws<RuntimeError>(() => vm.Execute(chunk));
    }

    // ── json.valid ────────────────────────────────────────────────────────

    [Fact]
    public void Valid_ValidObject()
    {
        var result = Run("let result = json.valid(\"{}\");");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Valid_ValidArray()
    {
        var result = Run("let result = json.valid(\"[1, 2, 3]\");");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Valid_ValidString()
    {
        var result = Run("let result = json.valid(\"\\\"hello\\\"\");");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Valid_ValidNumber()
    {
        var result = Run("let result = json.valid(\"42\");");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Valid_InvalidJson()
    {
        var result = Run("let result = json.valid(\"not json at all\");");
        Assert.Equal(false, result);
    }

    [Fact]
    public void Valid_InvalidBraces()
    {
        var result = Run("let result = json.valid(\"{ bad }\");");
        Assert.Equal(false, result);
    }

    [Fact]
    public void Valid_EmptyString()
    {
        var result = Run("let result = json.valid(\"\");");
        Assert.Equal(false, result);
    }

    [Fact]
    public void Valid_NonStringThrows()
    {
        RunExpectingError("json.valid(42);");
    }
}
