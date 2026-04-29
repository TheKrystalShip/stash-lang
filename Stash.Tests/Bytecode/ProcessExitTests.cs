using System;
using System.IO;
using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Stdlib;

namespace Stash.Tests.Bytecode;

/// <summary>
/// Integration tests for defer-aware, catch-immune <c>process.exit</c> semantics.
/// All tests run with <c>EmbeddedMode = true</c> so the VM throws <see cref="ExitException"/>
/// rather than calling <see cref="System.Environment.Exit"/>.
/// </summary>
public class ProcessExitTests : Stash.Tests.Interpreting.StashTestBase
{
    private static (Chunk chunk, VirtualMachine vm) BuildVM(string source, StringWriter? stdout = null)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        vm.EmbeddedMode = true;
        if (stdout is not null)
        {
            vm.Output = stdout;
        }
        return (chunk, vm);
    }

    // =========================================================================
    // 1. Defers run before exit
    // =========================================================================

    [Fact]
    public void Exit_RunsDefersBeforeTermination()
    {
        string markerFile = Path.Combine(Path.GetTempPath(), $"stash_exit_defer_{Guid.NewGuid():N}.txt");
        try
        {
            var (chunk, vm) = BuildVM($$"""
                defer fs.writeFile("{{markerFile}}", "deferred");
                process.exit(0);
                """);
            var ex = Assert.Throws<ExitException>(() => vm.Execute(chunk));
            Assert.Equal(0, ex.ExitCode);
            Assert.True(File.Exists(markerFile), "Defer should have written the marker file before exit.");
            Assert.Equal("deferred", File.ReadAllText(markerFile));
        }
        finally
        {
            File.Delete(markerFile);
        }
    }

    [Fact]
    public void Exit_NestedDefers_RunsLifo()
    {
        // Defers registered in order A, B should run in reverse: B then A.
        var sw = new StringWriter();
        var (chunk, vm) = BuildVM("""
            defer io.println("A");
            defer io.println("B");
            process.exit(0);
            """, sw);
        var ex = Assert.Throws<ExitException>(() => vm.Execute(chunk));
        Assert.Equal(0, ex.ExitCode);
        string output = sw.ToString();
        int posA = output.IndexOf("A", StringComparison.Ordinal);
        int posB = output.IndexOf("B", StringComparison.Ordinal);
        Assert.True(posB < posA, $"Expected B before A (LIFO order); got output: {output}");
    }

    [Fact]
    public void Exit_DeferThrows_StillExits()
    {
        // A defer that itself throws should not prevent exit from propagating.
        var (chunk, vm) = BuildVM("""
            defer { throw "oops"; }
            process.exit(42);
            """);
        var ex = Assert.Throws<ExitException>(() => vm.Execute(chunk));
        Assert.Equal(42, ex.ExitCode);
        // The defer error should be recorded as suppressed.
        Assert.NotNull(ex.SuppressedErrors);
        Assert.True(ex.SuppressedErrors!.Count > 0, "Expected at least one suppressed defer error.");
    }

    // =========================================================================
    // 2. Catch-immunity
    // =========================================================================

    [Fact]
    public void Exit_NotMatchedByCatchAll()
    {
        // A Stash try/catch(e) must NOT intercept ExitException.
        var (chunk, vm) = BuildVM("""
            try {
                process.exit(7);
            } catch (e) {
                io.println("caught");
            }
            """);
        // ExitException must propagate out to C# — not be swallowed by the Stash catch.
        var ex = Assert.Throws<ExitException>(() => vm.Execute(chunk));
        Assert.Equal(7, ex.ExitCode);
    }

    [Fact]
    public void Exit_NotMatchedByErrorBaseCatch()
    {
        // catch (Error e) likewise must not intercept ExitException.
        var (chunk, vm) = BuildVM("""
            try {
                process.exit(3);
            } catch (Error e) {
                io.println("caught as Error");
            }
            """);
        var ex = Assert.Throws<ExitException>(() => vm.Execute(chunk));
        Assert.Equal(3, ex.ExitCode);
    }

    // =========================================================================
    // 3. Exit code
    // =========================================================================

    [Fact]
    public void Exit_DefaultCode_Zero()
    {
        // process.exit() with no argument must exit with code 0.
        var (chunk, vm) = BuildVM("""
            process.exit();
            """);
        var ex = Assert.Throws<ExitException>(() => vm.Execute(chunk));
        Assert.Equal(0, ex.ExitCode);
    }

    [Fact]
    public void Exit_NonzeroCode_Preserved()
    {
        var (chunk, vm) = BuildVM("""
            process.exit(99);
            """);
        var ex = Assert.Throws<ExitException>(() => vm.Execute(chunk));
        Assert.Equal(99, ex.ExitCode);
    }

    // =========================================================================
    // 4. Defers across call stack frames
    // =========================================================================

    [Fact]
    public void Exit_RunsDefersAcrossCallStackFrames()
    {
        // Defers registered in a nested function must also run on exit.
        string markerFile = Path.Combine(Path.GetTempPath(), $"stash_exit_frame_{Guid.NewGuid():N}.txt");
        try
        {
            var (chunk, vm) = BuildVM($$"""
                fn inner() {
                    defer fs.writeFile("{{markerFile}}", "inner-deferred");
                    process.exit(5);
                }
                inner();
                """);
            var ex = Assert.Throws<ExitException>(() => vm.Execute(chunk));
            Assert.Equal(5, ex.ExitCode);
            Assert.True(File.Exists(markerFile), "Defer inside called function should have run.");
        }
        finally
        {
            File.Delete(markerFile);
        }
    }
}
