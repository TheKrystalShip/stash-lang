using System.Diagnostics;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Bytecode;
using Stash.Resolution;

namespace Stash.Tests.Interpreting;

public class CliExecutionTests
{
    // =========================================================================
    // Pipeline helpers (mirror RunSource() in Program.cs)
    // =========================================================================

    private static object? Run(string source, string sourceName, string[] scriptArgs)
    {
        string full = source + "\nreturn result;";
        var lexer = new Lexer(full, sourceName);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(TestVM.CreateGlobals());
        vm.ScriptArgs = scriptArgs;
        vm.CurrentFile = sourceName;
        return vm.Execute(chunk);
    }

    // =========================================================================
    // Process-level helpers
    // =========================================================================

    private static string GetCliBinaryPath()
    {
        string testDir = AppContext.BaseDirectory;
        string cliDir = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(testDir, "..", "..", "..", "..", "Stash.Cli", "bin", "Debug", "net10.0"));
        string binary = System.IO.Path.Combine(cliDir, "Stash");
        if (!System.IO.File.Exists(binary))
        {
            binary = System.IO.Path.Combine(cliDir, "Stash.exe"); // Windows
        }

        return binary;
    }

    private static (string stdout, string stderr, int exitCode) RunCli(string arguments, string? stdinInput = null)
    {
        string binary = GetCliBinaryPath();
        var psi = new ProcessStartInfo
        {
            FileName = binary,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdinInput is not null,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)!;

        if (stdinInput is not null)
        {
            process.StandardInput.Write(stdinInput);
            process.StandardInput.Close();
        }

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (stdout.TrimEnd('\n', '\r'), stderr.TrimEnd('\n', '\r'), process.ExitCode);
    }

    // =========================================================================
    // Pipeline Tests (RunSource equivalent)
    // =========================================================================

    [Fact]
    public void RunSource_WithCommandName_ExecutesSuccessfully()
    {
        var source = "let result = 1 + 1;";
        object? value = Run(source, "<command>", []);
        Assert.Equal(2L, value);
    }

    [Fact]
    public void RunSource_WithStdinName_ExecutesSuccessfully()
    {
        var source = "let result = 10 * 3;";
        object? value = Run(source, "<stdin>", []);
        Assert.Equal(30L, value);
    }

    [Fact]
    public void RunSource_WithArgs_PassesArgsCorrectly()
    {
        var source = """
            let a = args.list();
            let result = a[0];
            """;
        object? value = Run(source, "<command>", ["hello"]);
        Assert.Equal("hello", value);
    }

    [Fact]
    public void RunSource_MultipleStatements_ExecutesAll()
    {
        var source = """
            let x = 5;
            let y = x * 2;
            let result = y + 1;
            """;
        object? value = Run(source, "<command>", []);
        Assert.Equal(11L, value);
    }

    [Fact]
    public void RunSource_WithEmptySource_NoError()
    {
        // Empty source should parse and interpret without throwing
        var lexer = new Lexer("", "<command>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        SemanticResolver.Resolve(statements);
        var chunk = Compiler.Compile(statements);
        var vm = new VirtualMachine(TestVM.CreateGlobals());
        vm.ScriptArgs = [];
        var ex = Record.Exception(() => { vm.Execute(chunk); });
        Assert.Null(ex);
    }

    // =========================================================================
    // -c / --command (Process-level integration)
    // =========================================================================

    [Fact]
    [Trait("Category", "Integration")]
    public void Command_SimpleExpression_PrintsResult()
    {
        var (stdout, _, exitCode) = RunCli("-c \"io.println(42);\"");
        Assert.Equal(0, exitCode);
        Assert.Equal("42", stdout);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Command_WithScriptArgs_PassesArgs()
    {
        var (stdout, _, exitCode) = RunCli("-c \"io.println(args.list()[0]);\" myarg");
        Assert.Equal(0, exitCode);
        Assert.Equal("myarg", stdout);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Command_LongFlag_WorksSameAsShort()
    {
        var (stdout, _, exitCode) = RunCli("--command \"io.println(99);\"");
        Assert.Equal(0, exitCode);
        Assert.Equal("99", stdout);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Command_MissingArgument_ExitsWithError()
    {
        var (_, stderr, exitCode) = RunCli("-c");
        Assert.Equal(64, exitCode);
        Assert.Contains("-c", stderr);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Command_SyntaxError_ExitsWithCode65()
    {
        var (_, stderr, exitCode) = RunCli("-c \"let x = ;\"");
        Assert.Equal(65, exitCode);
        Assert.NotEmpty(stderr);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Command_RuntimeError_ExitsWithCode70()
    {
        var (_, stderr, exitCode) = RunCli("-c \"undefinedVar;\"");
        Assert.Equal(70, exitCode);
        Assert.NotEmpty(stderr);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Command_WithDebugFlag_ExitsWithError()
    {
        var (_, stderr, exitCode) = RunCli("--debug -c \"io.println(1);\"");
        Assert.Equal(64, exitCode);
        Assert.NotEmpty(stderr);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Command_WithTestFlag_ExitsWithError()
    {
        var (_, stderr, exitCode) = RunCli("--test -c \"io.println(1);\"");
        Assert.Equal(64, exitCode);
        Assert.NotEmpty(stderr);
    }

    // =========================================================================
    // Stdin Piping (Process-level integration)
    // =========================================================================

    [Fact]
    [Trait("Category", "Integration")]
    public void Stdin_SimplePrint_PrintsOutput()
    {
        var (stdout, _, exitCode) = RunCli("", stdinInput: "io.println(123);");
        Assert.Equal(0, exitCode);
        Assert.Equal("123", stdout);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Stdin_WithArgs_PassesArgs()
    {
        var (stdout, _, exitCode) = RunCli("-- firstarg", stdinInput: "io.println(args.list()[0]);");
        Assert.Equal(0, exitCode);
        Assert.Equal("firstarg", stdout);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Stdin_MultilineScript_ExecutesAll()
    {
        string script = """
            let x = 10;
            let y = 20;
            io.println(x + y);
            """;
        var (stdout, _, exitCode) = RunCli("", stdinInput: script);
        Assert.Equal(0, exitCode);
        Assert.Equal("30", stdout);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Stdin_SyntaxError_ExitsWithCode65()
    {
        var (_, stderr, exitCode) = RunCli("", stdinInput: "let x = ;");
        Assert.Equal(65, exitCode);
        Assert.NotEmpty(stderr);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Stdin_RuntimeError_ExitsWithCode70()
    {
        var (_, stderr, exitCode) = RunCli("", stdinInput: "undefinedVar;");
        Assert.Equal(70, exitCode);
        Assert.NotEmpty(stderr);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Stdin_WithDebugFlag_ExitsWithError()
    {
        var (_, stderr, exitCode) = RunCli("--debug", stdinInput: "io.println(1);");
        Assert.Equal(64, exitCode);
        Assert.NotEmpty(stderr);
    }
}
