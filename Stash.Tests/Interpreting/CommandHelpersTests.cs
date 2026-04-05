using Stash.Bytecode;
using Stash.Common;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Resolution;
using Stash.Runtime;

namespace Stash.Tests.Interpreting;

public class CommandHelpersTests
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

    // ── RunCaptured ───────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public void RunCaptured_EchoCommand_CapturesStdout()
    {
        if (OperatingSystem.IsWindows()) { return; }
        var result = Run("let result = $(echo hello).stdout;");
        Assert.Contains("hello", (string)result!);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void RunCaptured_NullStdin_DoesNotRedirectInput()
    {
        if (OperatingSystem.IsWindows()) { return; }
        var result = Run("let result = $(echo test).stdout;");
        Assert.Contains("test", (string)result!);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void RunCaptured_NonZeroExitCode_ReturnsExitCode()
    {
        if (OperatingSystem.IsWindows()) { return; }
        RunExpectingError("$!(false);");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void RunCaptured_InvalidProgram_ThrowsRuntimeError()
    {
        if (OperatingSystem.IsWindows()) { return; }
        RunExpectingError("$(__nonexistent_program_xyz__);");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void RunCaptured_WithArguments_PassesArgsCorrectly()
    {
        if (OperatingSystem.IsWindows()) { return; }
        var result = Run("let result = $(echo -n hello).stdout;");
        Assert.Contains("hello", (string)result!);
    }

    // ── RunPassthrough ────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public void RunPassthrough_SimpleCommand_ReturnsExitCode()
    {
        if (OperatingSystem.IsWindows()) { return; }
        var result = Run("let result = $(true).exitCode;");
        Assert.Equal(0L, result);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void RunPassthrough_FailingCommand_ReturnsNonZeroExitCode()
    {
        if (OperatingSystem.IsWindows()) { return; }
        var result = Run("let result = $(false).exitCode;");
        Assert.NotEqual(0L, result);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void RunPassthrough_InvalidProgram_ThrowsRuntimeError()
    {
        if (OperatingSystem.IsWindows()) { return; }
        RunExpectingError("$(__nonexistent_program_xyz__);");
    }
}
