namespace Stash.Tests.Bytecode;

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Stash.Bytecode;
using Stash.Cli.Shell;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Stdlib;

/// <summary>
/// Tests for <c>shell.lastExitCode()</c> stdlib function (§8.1) and the REPL <c>$?</c>
/// sugar desugaring (§8.2) — both wired through <see cref="ShellRunner"/> and
/// <see cref="ReplLinePreprocessor"/>.
/// </summary>
public class LastExitCodeTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Compiles and runs Stash source against a VM, returning stdout as a string.</summary>
    private static string RunSource(string source, VirtualMachine vm)
    {
        var sw = new StringWriter();
        var previous = vm.Output;
        vm.Output = sw;
        try
        {
            ShellRunner.EvaluateSource(source, vm);
        }
        finally
        {
            vm.Output = previous;
        }
        return sw.ToString().Trim();
    }

    private static VirtualMachine MakeVm()
    {
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        vm.EmbeddedMode = true;
        vm.Output = Console.Out;
        vm.ErrorOutput = Console.Error;
        return vm;
    }

    private static (ShellRunner Runner, VirtualMachine Vm) MakeRunner()
    {
        var vm = MakeVm();

        var ctx = new ShellContext
        {
            Vm = vm,
            PathCache = new PathExecutableCache(),
            Keywords = ShellContext.BuildKeywordSet(),
            Namespaces = new HashSet<string>(StdlibRegistry.NamespaceNames, StringComparer.Ordinal),
            ShellBuiltinNames = ShellContext.BuildShellBuiltinSet(),
        };
        return (new ShellRunner(ctx), vm);
    }

    // ── §8.1: shell.lastExitCode() ────────────────────────────────────────────

    [Fact]
    public void LastExitCode_DefaultZero()
    {
        var vm = MakeVm();
        // Fresh VM — no command has run yet — should return 0.
        string output = RunSource("io.println(shell.lastExitCode());", vm);
        Assert.Equal("0", output);
    }

    [Fact]
    public void LastExitCode_ReflectsManuallySetValue()
    {
        // Directly set LastExitCode on the VM (simulating what ShellRunner does)
        // and verify that shell.lastExitCode() picks it up.
        var vm = MakeVm();
        vm.LastExitCode = 42;
        string output = RunSource("io.println(shell.lastExitCode());", vm);
        Assert.Equal("42", output);
    }

    [Fact]
    public void LastExitCode_ReturnsInt()
    {
        var vm = MakeVm();
        vm.LastExitCode = 1;
        // Verify that the result can participate in arithmetic.
        string output = RunSource("io.println(shell.lastExitCode() + 10);", vm);
        Assert.Equal("11", output);
    }

    [Fact]
    public void LastExitCode_AfterShellCommand_ReflectsExit()
    {
        // POSIX only: run a command that is known to exit with code 1 (false),
        // then verify shell.lastExitCode() reflects that code.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        var (runner, vm) = MakeRunner();

        // 'false' is a POSIX command that always exits with code 1.
        runner.Run("false");

        string output = RunSource("io.println(shell.lastExitCode());", vm);
        Assert.Equal("1", output);
    }

    [Fact]
    public void LastExitCode_AfterSuccessfulCommand_ReturnsZero()
    {
        // POSIX only: run 'true', which always exits 0.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        var (runner, vm) = MakeRunner();

        runner.Run("true");

        string output = RunSource("io.println(shell.lastExitCode());", vm);
        Assert.Equal("0", output);
    }

    // ── Regression: deprecated process.lastExitCode alias still works until N+2 ─────

    [Fact]
    // Regression: deprecated process.lastExitCode alias still works until N+2.
    public void ProcessLastExitCode_DeprecatedAlias_StillWorks()
    {
        var vm = MakeVm();
        vm.LastExitCode = 5;
        string output = RunSource("io.println(process.lastExitCode());", vm);
        Assert.Equal("5", output);
    }

    // ── §8.2: $? preprocessor integration ────────────────────────────────────

    [Fact]
    public void DollarQuestion_Preprocessed_EvaluatesToZeroOnFreshVm()
    {
        var vm = MakeVm();
        // Apply preprocessor then evaluate; fresh VM → 0.
        string desugared = ReplLinePreprocessor.Apply("$?");
        string output = RunSource($"io.println({desugared});", vm);
        Assert.Equal("0", output);
    }

    [Fact]
    public void DollarQuestion_Preprocessed_ReflectsLastExitCode()
    {
        var vm = MakeVm();
        vm.LastExitCode = 7;

        string desugared = ReplLinePreprocessor.Apply("$?");
        string output = RunSource($"io.println({desugared});", vm);
        Assert.Equal("7", output);
    }

    [Fact]
    public void DollarQuestion_InExpression_WorksCorrectly()
    {
        var vm = MakeVm();
        vm.LastExitCode = 2;

        // Desugar "if ($? != 0) { io.println("failed"); }" — Stash requires parens around if condition
        string desugared = ReplLinePreprocessor.Apply("if ($? != 0) { io.println(\"failed\"); }");
        string output = RunSource(desugared, vm);
        Assert.Equal("failed", output);
    }

    [Fact]
    public void DollarQuestion_AfterShellCommand_ReflectsExit()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        var (runner, vm) = MakeRunner();
        runner.Run("false"); // exits 1

        string desugared = ReplLinePreprocessor.Apply("$?");
        string output = RunSource($"io.println({desugared});", vm);
        Assert.Equal("1", output);
    }
}
