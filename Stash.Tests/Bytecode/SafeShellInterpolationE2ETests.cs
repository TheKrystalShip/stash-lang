using System;
using System.Collections.Generic;
using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib;
using Xunit;

namespace Stash.Tests.Bytecode;

/// <summary>
/// End-to-end tests covering the scenarios from the Safe Shell Interpolation
/// spec (§6). All tests are POSIX-only and use <c>printf</c> for deterministic
/// output (no trailing-newline ambiguity from <c>echo</c>).
/// </summary>
public class SafeShellInterpolationE2ETests : BytecodeTestBase
{
    private static object? Exec(string source)
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

    private static string? Stdout(object? result)
    {
        var inst = result as StashInstance;
        if (inst == null) return null;
        inst.VMTryGetField("stdout", out StashValue sv, null);
        return sv.AsObj as string;
    }

    private static long? ExitCode(object? result)
    {
        var inst = result as StashInstance;
        if (inst == null) return null;
        inst.VMTryGetField("exitCode", out StashValue sv, null);
        return sv.AsInt;
    }

    // =========================================================================
    // §6.1 — Motivating bug: user-controlled value with semicolons and tilde
    // =========================================================================

    [Fact]
    public void Sec6_1_MotivatingBug_SemicolonAndTilde_PassedVerbatim()
    {
        if (OperatingSystem.IsWindows()) return;

        // Before safe interpolation: CommandParser would tokenize "; rm -rf ~"
        // into [;, rm, -rf, ~] and tilde-expand ~ → argv injection.
        // After: single literal arg; printf receives the entire string as-is.
        var result = Exec("""
            let userInput = "; rm -rf ~";
            return $(printf "%s" ${userInput});
            """);

        Assert.Equal("; rm -rf ~", Stdout(result));
    }

    // =========================================================================
    // §6.2 — Quote-escape attack: embedded double-quotes in interpolated value
    // =========================================================================

    [Fact]
    public void Sec6_2_QuoteEscapeAttack_EmbeddedQuotes_PassedVerbatim()
    {
        if (OperatingSystem.IsWindows()) return;

        // The attacker supplies a value that contains literal double-quote chars.
        // Before: $(rm "${userPath}") would become rm "foo" "-rf" "/"
        //         after string assembly → argv injection.
        // After:  args = [`foo" "-rf" "/`] — single literal argv entry.
        var result = Exec("""
            let userPath = "foo\" \"-rf\" \"/";
            return $(printf "%s" ${userPath});
            """);

        Assert.Equal("foo\" \"-rf\" \"/", Stdout(result));
    }

    // =========================================================================
    // §6.3 — Pipes with interpolation: variable used as grep pattern
    // =========================================================================

    [Fact]
    public void Sec6_3_PipeWithInterpolation_PatternPassedSafely()
    {
        if (OperatingSystem.IsWindows()) return;

        var result = Exec("""
            let pat = "world";
            return $(printf "%s\n%s\n" "hello" "world" | grep ${pat});
            """);

        Assert.Equal("world\n", Stdout(result));
    }

    // =========================================================================
    // §6.4 — Splat: first slot is program name when array is splatted
    // =========================================================================

    [Fact]
    public void Sec6_4_Splat_ArrayInFirstSlot_RunsCorrectly()
    {
        if (OperatingSystem.IsWindows()) return;

        var result = Exec("""
            let kc = ["printf", "%s\n"];
            return $(${...kc} hello);
            """);

        Assert.Equal("hello\n", Stdout(result));
    }

    // =========================================================================
    // §6.5(a) — Intentional break: whitespace-value is ONE arg, not two
    // =========================================================================

    [Fact]
    public void Sec6_5a_WhitespaceValue_IsOneArgument_LsFails()
    {
        if (OperatingSystem.IsWindows()) return;

        // "ls -la /tmp" as a single interpolated argv entry → ls fails to find
        // a file literally named "-la /tmp".
        var result = Exec("""
            let opts = "-la /tmp";
            return $(ls ${opts});
            """);

        Assert.NotEqual(0L, ExitCode(result));
    }

    [Fact]
    public void Sec6_5a_Migration_SplitAndSplat_LsSucceeds()
    {
        if (OperatingSystem.IsWindows()) return;

        // Migration: split the string, then splat.
        var result = Exec("""
            let opts = "-la /tmp";
            let parts = str.split(opts, " ");
            return $(ls ${...parts});
            """);

        Assert.Equal(0L, ExitCode(result));
    }

    // =========================================================================
    // §6.5(d) — Intentional break: multi-word program name fails at exec
    // =========================================================================

    [Fact]
    public void Sec6_5d_ProgramNameArray_RunsCorrectly()
    {
        if (OperatingSystem.IsWindows()) return;

        // Array form: first element becomes program, rest become args.
        var result = Exec("""
            let cmd = ["printf", "%s", "hi"];
            return $(${...cmd});
            """);

        Assert.Equal("hi", Stdout(result));
    }

    // =========================================================================
    // Bonus: process.exec direct call — same semantics as sugar
    // =========================================================================

    [Fact]
    public void DirectExec_ProcessExec_CapturesStdout()
    {
        if (OperatingSystem.IsWindows()) return;

        var result = Exec("""
            return process.exec("printf", ["%s", "direct"]);
            """);

        Assert.Equal("direct", Stdout(result));
    }

    [Fact]
    public void DirectExec_ProcessPipeline_CapturesLastStage()
    {
        if (OperatingSystem.IsWindows()) return;

        var result = Exec("""
            return process.pipeline([
                PipelineStage{ program: "printf", args: ["%s\n%s\n", "hello", "world"] },
                PipelineStage{ program: "grep",   args: ["world"] }
            ]);
            """);

        Assert.Equal("world\n", Stdout(result));
    }
}
