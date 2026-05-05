using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Stdlib;

namespace Stash.Tests.Bytecode;

public class StreamingCommandTests : BytecodeTestBase
{
    // Override Execute to inject stdlib globals (arr, etc.)
    protected new static object? Execute(string source)
    {
        var lexer = new Lexer(source, "<test>");
        List<Token> tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        List<Stmt> stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        Chunk chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        return Normalize(vm.Execute(chunk));
    }
    [Fact]
    public void Streaming_PidIsNonZeroImmediately()
    {
        if (OperatingSystem.IsWindows()) return;
        object? result = Execute(@"
            let s = $<(printf 'a\nb\n');
            return s.pid > 0;
        ");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Streaming_IteratesLines()
    {
        if (OperatingSystem.IsWindows()) return;
        object? result = Execute(@"
            let s = $<(printf 'a\nb\nc\n');
            let lines = [];
            for (let line in s) {
                arr.push(lines, line);
            }
            return lines;
        ");
        Assert.Equal(new List<object?> { "a", "b", "c" }, result);
    }

    [Fact]
    public void Streaming_ExitCodeAfterIteration()
    {
        if (OperatingSystem.IsWindows()) return;
        object? result = Execute(@"
            let s = $<(printf 'a\nb\n');
            for (let line in s) { }
            return s.exitCode;
        ");
        Assert.Equal(0L, result);
    }

    [Fact]
    public void StreamingStrict_NonZeroExit_ThrowsCommandError()
    {
        if (OperatingSystem.IsWindows()) return;
        var ex = Assert.Throws<RuntimeError>(() => Execute(@"
            let s = $!<(false);
            for (let line in s) { }
        "));
        Assert.Equal("CommandError", ex.ErrorType);
        Assert.NotNull(ex.Properties);
        Assert.True(ex.Properties!.ContainsKey("exitCode"));
        long exitCode = Assert.IsType<long>(ex.Properties["exitCode"]);
        Assert.NotEqual(0L, exitCode);
    }

    [Fact]
    public void StreamingLenient_NonZeroExit_DoesNotThrow()
    {
        if (OperatingSystem.IsWindows()) return;
        object? result = Execute(@"
            let s = $<(false);
            for (let line in s) { }
            return s.exitCode;
        ");
        Assert.Equal(1L, result);
    }

    [Fact]
    public void Streaming_DoubleIteration_ThrowsStateError()
    {
        if (OperatingSystem.IsWindows()) return;
        var ex = Assert.Throws<RuntimeError>(() => Execute(@"
            let s = $<(printf 'a\nb\n');
            for (let line in s) { }
            for (let line in s) { }
        "));
        Assert.Equal("StateError", ex.ErrorType);
    }

    // ── Timeout × streaming tests ────────────────────────────────────────────

    [Fact]
    public void Timeout_OverStreamingNeverTerminating_FiresAndKillsChild()
    {
        if (OperatingSystem.IsWindows()) return;
        var sw = Stopwatch.StartNew();
        var result = (List<object?>)Execute(@"
            let s = $<(yes hi);
            let pid = s.pid;
            try {
                timeout 1s { for (let line in s) {} }
            } catch (e) {
                return [pid, e.type];
            }
            return [0, ""no-error""];
        ")!;
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 8000, $"Timeout took too long: {sw.ElapsedMilliseconds} ms");
        int pid = Convert.ToInt32(result[0]);
        Assert.Equal("TimeoutError", result[1]);
        Assert.True(pid > 0);
        Thread.Sleep(200);
        Assert.False(IsProcessAlive(pid), $"Child process {pid} should be dead after timeout");
    }

    [Fact]
    public void Timeout_OverStreamingSilentChild_FiresAndKillsChild()
    {
        if (OperatingSystem.IsWindows()) return;
        var sw = Stopwatch.StartNew();
        var result = (List<object?>)Execute(@"
            let s = $<(sleep 60);
            let pid = s.pid;
            try {
                timeout 1s { for (let line in s) {} }
            } catch (e) {
                return [pid, e.type];
            }
            return [0, ""no-error""];
        ")!;
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 8000, $"Timeout took too long: {sw.ElapsedMilliseconds} ms");
        int pid = Convert.ToInt32(result[0]);
        Assert.Equal("TimeoutError", result[1]);
        Assert.True(pid > 0);
        Thread.Sleep(200);
        Assert.False(IsProcessAlive(pid), $"Child process {pid} should be dead after timeout");
    }

    [Fact]
    public void Timeout_OverDualIteration_FiresAndKillsChild()
    {
        if (OperatingSystem.IsWindows()) return;
        var sw = Stopwatch.StartNew();
        var result = (List<object?>)Execute(@"
            let s = $<(sleep 60);
            let pid = s.pid;
            try {
                timeout 1s { for (let out, err in s) {} }
            } catch (e) {
                return [pid, e.type];
            }
            return [0, ""no-error""];
        ")!;
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 8000, $"Timeout took too long: {sw.ElapsedMilliseconds} ms");
        int pid = Convert.ToInt32(result[0]);
        Assert.Equal("TimeoutError", result[1]);
        Assert.True(pid > 0);
        Thread.Sleep(200);
        Assert.False(IsProcessAlive(pid), $"Child process {pid} should be dead after timeout");
    }

    [Fact]
    public void Timeout_OverBytesIteration_FiresAndKillsChild()
    {
        if (OperatingSystem.IsWindows()) return;
        var sw = Stopwatch.StartNew();
        var result = (List<object?>)Execute(@"
            let s = $<(sleep 60);
            let pid = s.pid;
            try {
                timeout 1s { for (let chunk in s.bytes(1024)) {} }
            } catch (e) {
                return [pid, e.type];
            }
            return [0, ""no-error""];
        ")!;
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 8000, $"Timeout took too long: {sw.ElapsedMilliseconds} ms");
        int pid = Convert.ToInt32(result[0]);
        Assert.Equal("TimeoutError", result[1]);
        Assert.True(pid > 0);
        Thread.Sleep(200);
        Assert.False(IsProcessAlive(pid), $"Child process {pid} should be dead after timeout");
    }

    [Fact]
    public void Timeout_DoesNotFire_NaturalCompletion()
    {
        if (OperatingSystem.IsWindows()) return;
        object? result = Execute(@"
            let s = $<(printf 'a\n');
            timeout 5s { for (let line in s) {} }
            return s.exitCode;
        ");
        Assert.Equal(0L, result);
    }

    [Fact]
    public void Timeout_NestedTimeoutsRespectInner()
    {
        if (OperatingSystem.IsWindows()) return;
        var sw = Stopwatch.StartNew();
        var result = (List<object?>)Execute(@"
            let s = $<(yes hi);
            let pid = s.pid;
            try {
                timeout 10s {
                    timeout 1s { for (let line in s) {} }
                }
            } catch (e) {
                return [pid, e.type];
            }
            return [0, ""no-error""];
        ")!;
        sw.Stop();

        // Inner 1s timeout should fire well before the outer 10s
        Assert.True(sw.ElapsedMilliseconds < 8000, $"Inner timeout took too long: {sw.ElapsedMilliseconds} ms");
        int pid = Convert.ToInt32(result[0]);
        Assert.Equal("TimeoutError", result[1]);
        Assert.True(pid > 0);
        Thread.Sleep(200);
        Assert.False(IsProcessAlive(pid), $"Child process {pid} should be dead after timeout");
    }

    // TODO: ExternalCancel_DuringStreaming_KillsChild is omitted — requires a script-execution
    // entry point that accepts an external CancellationTokenSource, which the current test
    // scaffolding (Execute helper via StdlibDefinitions.CreateVMGlobals + VirtualMachine.Execute)
    // does not expose cleanly. Implement when the test infrastructure adds a cancellation-aware
    // execution path.

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            var p = System.Diagnostics.Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch
        {
            return false;
        }
    }
}
