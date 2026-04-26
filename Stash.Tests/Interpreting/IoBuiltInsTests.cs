using Stash.Lexing;
using Stash.Parsing;
using Stash.Bytecode;
using Stash.Resolution;
using Stash.Runtime.Types;
using Stash.Stdlib;

namespace Stash.Tests.Interpreting;

public class IoBuiltInsTests : StashTestBase
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static (string stdout, string stderr) CaptureBoth(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        var outSw = new StringWriter();
        var errSw = new StringWriter();
        vm.Output = outSw;
        vm.ErrorOutput = errSw;
        vm.Execute(chunk);
        return (outSw.ToString(), errSw.ToString());
    }

    private static object? RunReturningValue(string source)
    {
        string full = source + "\nreturn result;";
        var lexer = new Lexer(full, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        var sw = new StringWriter();
        vm.Output = sw;
        vm.ErrorOutput = sw;
        return vm.Execute(chunk);
    }

    // ── 1. io.eprintln writes to stderr ──────────────────────────────────────

    [Fact]
    public void Eprintln_WritesToStderr()
    {
        var stderr = RunCapturingStderr("io.eprintln(\"hello\");");
        Assert.Equal("hello" + System.Environment.NewLine, stderr);
    }

    // ── 2. io.eprint writes to stderr (no newline) ───────────────────────────

    [Fact]
    public void Eprint_WritesToStderr()
    {
        var stderr = RunCapturingStderr("io.eprint(\"hello\");");
        Assert.Equal("hello", stderr);
    }

    // ── 3. io.eprintln does not write to stdout ───────────────────────────────

    [Fact]
    public void Eprintln_DoesNotWriteToStdout()
    {
        var (stdout, _) = CaptureBoth("io.eprintln(\"x\");");
        Assert.Equal("", stdout);
    }

    // ── 4. io.eprint does not write to stdout ────────────────────────────────

    [Fact]
    public void Eprint_DoesNotWriteToStdout()
    {
        var (stdout, _) = CaptureBoth("io.eprint(\"x\");");
        Assert.Equal("", stdout);
    }

    // ── 5. io.eprintln stringifies non-string values ─────────────────────────

    [Fact]
    public void Eprintln_StringifiesValue()
    {
        var stderr = RunCapturingStderr("io.eprintln(42);");
        Assert.Equal("42" + System.Environment.NewLine, stderr);
    }

    // ── 6. io.eprint stringifies non-string values ───────────────────────────

    [Fact]
    public void Eprint_StringifiesValue()
    {
        var stderr = RunCapturingStderr("io.eprint(true);");
        Assert.Equal("true", stderr);
    }

    // ── 7. io.eprintln with null value ───────────────────────────────────────

    [Fact]
    public void Eprintln_NullValue()
    {
        var stderr = RunCapturingStderr("io.eprintln(null);");
        Assert.Equal("null" + System.Environment.NewLine, stderr);
    }

    // ── 8. Multiple io.eprintln calls append output ──────────────────────────

    [Fact]
    public void Eprintln_MultipleCallsAppend()
    {
        var stderr = RunCapturingStderr("io.eprintln(\"first\"); io.eprintln(\"second\");");
        Assert.Equal("first" + System.Environment.NewLine + "second" + System.Environment.NewLine, stderr);
    }

    // ── 9. io.eprintln returns null ───────────────────────────────────────────

    [Fact]
    public void Eprintln_ReturnsNull()
    {
        var result = RunReturningValue("let result = io.eprintln(\"x\");");
        Assert.Null(result);
    }

    // ── 10. io.eprint returns null ────────────────────────────────────────────

    [Fact]
    public void Eprint_ReturnsNull()
    {
        var result = RunReturningValue("let result = io.eprint(\"x\");");
        Assert.Null(result);
    }

    // ── io.confirm ────────────────────────────────────────────────────────

    [Fact]
    public void Confirm_YesReturnsTrue()
    {
        string src = "let result = io.confirm(\"Continue?\");\nreturn result;";
        var lexer = new Lexer(src, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        var outSw = new System.IO.StringWriter();
        vm.Output = outSw;
        vm.Input = new System.IO.StringReader("y\n");
        var result = vm.Execute(chunk);
        Assert.Equal(true, result);
        Assert.Contains("[y/N]", outSw.ToString());
    }

    [Fact]
    public void Confirm_NoReturnsFalse()
    {
        string src = "let result = io.confirm(\"Continue?\");\nreturn result;";
        var lexer = new Lexer(src, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        var outSw = new System.IO.StringWriter();
        vm.Output = outSw;
        vm.Input = new System.IO.StringReader("n\n");
        var result = vm.Execute(chunk);
        Assert.Equal(false, result);
    }

    [Fact]
    public void Confirm_EmptyInputReturnsFalse()
    {
        string src = "let result = io.confirm(\"Continue?\");\nreturn result;";
        var lexer = new Lexer(src, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        var outSw = new System.IO.StringWriter();
        vm.Output = outSw;
        vm.Input = new System.IO.StringReader("\n");
        var result = vm.Execute(chunk);
        Assert.Equal(false, result);
    }

    [Fact]
    public void Confirm_YesFullWordReturnsTrue()
    {
        string src = "let result = io.confirm(\"Continue?\");\nreturn result;";
        var lexer = new Lexer(src, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        var outSw = new System.IO.StringWriter();
        vm.Output = outSw;
        vm.Input = new System.IO.StringReader("yes\n");
        var result = vm.Execute(chunk);
        Assert.Equal(true, result);
    }

    // ── io.readPassword ───────────────────────────────────────────────────

    [Fact]
    public void ReadPassword_NonInteractiveMode_ReturnsSecret()
    {
        // Console.IsInputRedirected is true in test environments
        string src = "let result = io.readPassword(\"Password: \");\nreturn result;";
        var lexer = new Lexer(src, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        vm.Output = new System.IO.StringWriter();
        vm.Input = new System.IO.StringReader("mysecret\n");
        var result = vm.Execute(chunk);
        Assert.IsType<StashSecret>(result);
    }

    [Fact]
    public void ReadPassword_EmptyInput_ReturnsSecret()
    {
        string src = "let result = io.readPassword();\nreturn result;";
        var lexer = new Lexer(src, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        vm.Output = new System.IO.StringWriter();
        vm.Input = new System.IO.StringReader("\n");
        var result = vm.Execute(chunk);
        Assert.IsType<StashSecret>(result);
    }

    [Fact]
    public void ReadPassword_PromptIsPrinted()
    {
        string src = "let result = io.readPassword(\"Password: \");\nreturn result;";
        var lexer = new Lexer(src, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        var outSw = new System.IO.StringWriter();
        vm.Output = outSw;
        vm.Input = new System.IO.StringReader("value\n");
        vm.Execute(chunk);
        Assert.Contains("Password: ", outSw.ToString());
    }

    [Fact]
    public void ReadPassword_RevealedValueMatchesInput()
    {
        string src = "let result = io.readPassword();\nreturn result;";
        var lexer = new Lexer(src, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        vm.Output = new System.IO.StringWriter();
        vm.Input = new System.IO.StringReader("hunter2\n");
        var result = vm.Execute(chunk);
        var secret = Assert.IsType<StashSecret>(result);
        Assert.Equal("hunter2", secret.Reveal().AsObj);
    }
}
