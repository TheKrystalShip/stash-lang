using System;
using System.IO;
using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Resolution;
using Stash.Stdlib;

namespace Stash.Tests.Bytecode;

/// <summary>
/// Verifies the no-Console-fallthrough contract for embedded mode and the Console-default
/// contract for CLI mode.
///
/// done_when:
///   #1 — EmbeddedMode=true, streams unset → TextWriter.Null / TextReader.Null (no Console leak).
///   #2 — EmbeddedMode=false (CLI default), streams unset → Console.Out / Console.Error / Console.In.
/// </summary>
[Collection("SystemConsoleTests")]
public class EmbeddedModeTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Chunk CompileScript(string source)
    {
        var lexer = new Lexer(source, "<test>");
        List<Token> tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        List<Stmt> stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        return Compiler.Compile(stmts);
    }

    // ── 1. Embedded mode — no Console fallthrough ─────────────────────────────

    [Fact]
    public void EmbeddedMode_OutputDefault_IsTextWriterNull()
    {
        var vm = new VirtualMachine();
        vm.EmbeddedMode = true;

        // Output must NOT be Console.Out — must be TextWriter.Null (or at least not the real console).
        Assert.Same(TextWriter.Null, vm.Output);
    }

    [Fact]
    public void EmbeddedMode_ErrorOutputDefault_IsTextWriterNull()
    {
        var vm = new VirtualMachine();
        vm.EmbeddedMode = true;

        Assert.Same(TextWriter.Null, vm.ErrorOutput);
    }

    [Fact]
    public void EmbeddedMode_InputDefault_IsTextReaderNull()
    {
        var vm = new VirtualMachine();
        vm.EmbeddedMode = true;

        Assert.Same(TextReader.Null, vm.Input);
    }

    [Fact]
    public void EmbeddedMode_IoPrintln_ProducesNoOutput_WhenOutputUnset()
    {
        // Arrange: embedded VM with stdlib — no Output set.
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        vm.EmbeddedMode = true;
        var chunk = CompileScript("io.println(\"should be suppressed\");");

        // Intercept Console.Out to confirm nothing reaches the real console.
        var consoleSw = new StringWriter();
        TextWriter prevOut = Console.Out;
        Console.SetOut(consoleSw);
        try
        {
            vm.Execute(chunk);
        }
        finally
        {
            Console.SetOut(prevOut);
        }

        // Neither the VM output nor the real console should have received any text.
        Assert.Equal(string.Empty, consoleSw.ToString());
    }

    // ── 2. CLI mode — Console defaults ────────────────────────────────────────

    [Fact]
    public void CliMode_OutputDefault_IsConsoleOut()
    {
        var vm = new VirtualMachine();
        // EmbeddedMode defaults to false — do NOT set it explicitly.

        Assert.Same(Console.Out, vm.Output);
    }

    [Fact]
    public void CliMode_ErrorOutputDefault_IsConsoleError()
    {
        var vm = new VirtualMachine();

        Assert.Same(Console.Error, vm.ErrorOutput);
    }

    [Fact]
    public void CliMode_InputDefault_IsConsoleIn()
    {
        var vm = new VirtualMachine();

        Assert.Same(Console.In, vm.Input);
    }

    [Fact]
    public void CliMode_IoPrintln_CapturesViaConsoleSetOut()
    {
        // Arrange: CLI-mode VM with stdlib — Output is NOT set explicitly.
        // Redirect Console.Out BEFORE creating the VM so the lazy default getter
        // (which evaluates Console.Out at call time, not at construction time)
        // picks up the StringWriter when io.println runs.
        var sw = new StringWriter();
        TextWriter prevOut = Console.Out;
        Console.SetOut(sw);
        try
        {
            var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
            // EmbeddedMode defaults to false — do NOT set it. Do NOT set vm.Output.
            var chunk = CompileScript("io.println(\"hello from cli\");");
            vm.Execute(chunk);
        }
        finally
        {
            Console.SetOut(prevOut);
        }

        // The VM's lazy Console.Out default must have routed output through the redirected stream.
        Assert.Contains("hello from cli", sw.ToString());
    }
}
