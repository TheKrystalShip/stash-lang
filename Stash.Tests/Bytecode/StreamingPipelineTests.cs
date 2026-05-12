using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Runtime.Errors;
using Stash.Stdlib;

namespace Stash.Tests.Bytecode;

/// <summary>
/// Tests for the streaming pipe chain feature: <c>$&lt;(stage1 | stage2 | ... | stageN)</c>
/// and <c>$!&lt;(stage1 | ... | stageN)</c>. Intermediate stages run captured-piped together,
/// the last stage's stdout feeds the StreamingProcess handle, and only the last stage's
/// exit code is observed (matching <c>${PIPESTATUS[-1]}</c> semantics).
///
/// All tests are POSIX-only (Windows guarded). Each test uses a 10-second timeout so a
/// hang surfaces as a failure, not an indefinitely blocked CI run.
/// </summary>
public class StreamingPipelineTests : BytecodeTestBase
{
    // Override Execute to inject stdlib globals (arr, str, etc.).
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

    [Fact(Timeout = 10000)]
    public async Task Pipeline_TwoStage_StreamsLastStageOutput()
    {
        if (OperatingSystem.IsWindows()) return;
        object? result = await Task.Run(() => Execute(@"
            let lines = [];
            for (let line in $<(printf 'a\nb\nc\n' | grep b)) {
                arr.push(lines, line);
            }
            return lines;
        "));
        Assert.Equal(new List<object?> { "b" }, result);
    }

    [Fact(Timeout = 10000)]
    public async Task Pipeline_ThreeStage_StreamsCorrectly()
    {
        if (OperatingSystem.IsWindows()) return;
        // seq 1 100 | head -20 | tail -5 yields 16..20.
        object? result = await Task.Run(() => Execute(@"
            let lines = [];
            for (let line in $<(seq 1 100 | head -20 | tail -5)) {
                arr.push(lines, line);
            }
            return lines;
        "));
        Assert.Equal(new List<object?> { "16", "17", "18", "19", "20" }, result);
    }

    [Fact(Timeout = 10000)]
    public async Task Pipeline_LastStageExitCode_Surfaced_TrueFalse()
    {
        if (OperatingSystem.IsWindows()) return;
        object? result = await Task.Run(() => Execute(@"
            let s = $<(true | false);
            for (let line in s) { }
            return s.exitCode;
        "));
        Assert.Equal(1L, result);
    }

    [Fact(Timeout = 10000)]
    public async Task Pipeline_LastStageExitCode_Surfaced_FalseTrue()
    {
        if (OperatingSystem.IsWindows()) return;
        object? result = await Task.Run(() => Execute(@"
            let s = $<(false | true);
            for (let line in s) { }
            return s.exitCode;
        "));
        Assert.Equal(0L, result);
    }

    [Fact(Timeout = 10000)]
    public async Task Pipeline_Strict_LastStageNonZero_ThrowsCommandError()
    {
        if (OperatingSystem.IsWindows()) return;
        var ex = await Task.Run(() => Assert.Throws<CommandError>(() => Execute(@"
            let s = $!<(true | false);
            for (let line in s) { }
        ")));
        Assert.Equal(1L, ex.ExitCode);
    }

    [Fact(Timeout = 10000)]
    public async Task Pipeline_Strict_IntermediateNonZero_DoesNotThrow()
    {
        if (OperatingSystem.IsWindows()) return;
        // false | true: intermediate fails, last succeeds — no throw.
        object? result = await Task.Run(() => Execute(@"
            let s = $!<(false | true);
            for (let line in s) { }
            return s.exitCode;
        "));
        Assert.Equal(0L, result);
    }

    [Fact(Timeout = 10000)]
    public async Task Pipeline_PidIsLastStage()
    {
        if (OperatingSystem.IsWindows()) return;
        // The handle's pid is the last stage's PID. We exercise pids[] to verify there
        // are 2 stages and that pid == pids[-1].
        object? result = await Task.Run(() => Execute(@"
            let s = $<(printf 'a\nb\n' | cat);
            let pidsLen = len(s.pids);
            let lastPidEq = (s.pids[pidsLen - 1] == s.pid);
            for (let line in s) { }
            return [pidsLen, lastPidEq];
        "));
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(2L, list[0]);
        Assert.Equal(true, list[1]);
    }

    [Fact(Timeout = 10000)]
    public async Task Pipeline_Cleanup_Break_KillsAllStages()
    {
        if (OperatingSystem.IsWindows()) return;
        // yes | cat | cat — break after one line; verify ALL three stages are reaped.
        object? result = await Task.Run(() => Execute(@"
            let s = $<(yes | cat | cat);
            let captured = s.pids;
            for (let line in s) { break; }
            // After the for-loop's IterClose hook fires, all stages have been signalled.
            let alive = 0;
            for (let p in captured) {
                let r = $(kill -0 ${p});
                if (r.exitCode == 0) { alive = alive + 1; }
            }
            return alive;
        "));
        Assert.Equal(0L, result);
    }

    [Fact(Timeout = 10000)]
    public async Task Pipeline_Cleanup_OnException_KillsAllStages()
    {
        if (OperatingSystem.IsWindows()) return;
        // The throw happens inside a function so the frame's ActiveIterators are
        // disposed during stack unwinding (matching Cleanup_Throw_KillsChild for
        // single-stage streams in StreamingCommandCleanupTests).
        object? result = await Task.Run(() => Execute(@"
            let captured = [];
            fn run() {
                let s = $<(yes | cat | cat);
                captured = s.pids;
                for (let line in s) { throw ""boom""; }
            }
            try { run(); } catch (e) { }
            let alive = 0;
            for (let p in captured) {
                let r = $(kill -0 ${p});
                if (r.exitCode == 0) { alive = alive + 1; }
            }
            return alive;
        "));
        Assert.Equal(0L, result);
    }

    [Fact(Timeout = 10000)]
    public async Task Pipeline_DualIteration_InterleavesAllStagesStderr()
    {
        if (OperatingSystem.IsWindows()) return;
        // Stage 1 emits to its own stderr; stage 2 (cat) passes stage 1's stdout through and
        // also emits its own stderr. Both stderr lines must reach the dual iterator.
        object? result = await Task.Run(() => Execute(@"
            let errs = [];
            let outs = [];
            for (let out, err in $<(sh -c ""echo stage1-err 1>&2; echo stage1-out"" | sh -c ""echo stage2-err 1>&2; cat"")) {
                if (out != null) { arr.push(outs, out); }
                if (err != null) { arr.push(errs, err); }
            }
            return [len(outs), len(errs)];
        "));
        var list = Assert.IsType<List<object?>>(result);
        // Last stage's stdout: at least the passed-through "stage1-out".
        Assert.True((long)list[0]! >= 1);
        // Both stages' stderr: 2 lines.
        Assert.Equal(2L, list[1]);
    }

    [Fact(Timeout = 10000)]
    public async Task Pipeline_Json_OnLastStage()
    {
        if (OperatingSystem.IsWindows()) return;
        // Note: the spec form `$<(a | b).json()` is grammatically ambiguous because
        // `.json()` binds tighter than `|`. Bind the handle first, then frame.
        object? result = await Task.Run(() => Execute(@"
            let s = $<(printf '{""a"":1}\n' | cat);
            let events = [];
            for (let e in s.json()) {
                arr.push(events, e.a);
            }
            return events;
        "));
        Assert.Equal(new List<object?> { 1L }, result);
    }
}
