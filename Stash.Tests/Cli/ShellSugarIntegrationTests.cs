using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Stash.Bytecode;
using Stash.Cli.Shell;
using Stash.Runtime;
using Stash.Stdlib;

namespace Stash.Tests.Cli;

/// <summary>
/// Integration tests for §11.2 shell built-in desugaring: exercises the full runner pipeline
/// (arg expansion → desugaring → VM execution) for cd / pwd / exit / quit.
/// </summary>
public class ShellSugarIntegrationTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (ShellRunner Runner, VirtualMachine Vm) MakeRunner(StringWriter? output = null)
    {
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        vm.Output = output ?? Console.Out;
        vm.ErrorOutput = Console.Error;
        // EmbeddedMode ensures process.exit() throws ExitException instead of calling
        // Environment.Exit — critical for the exit integration test.
        vm.EmbeddedMode = true;

        var ctx = new ShellContext
        {
            Vm = vm,
            PathCache = new PathExecutableCache(_ => true),
            Keywords = ShellContext.BuildKeywordSet(),
            Namespaces = new HashSet<string>(StdlibRegistry.NamespaceNames, StringComparer.Ordinal),
            ShellBuiltinNames = ShellContext.BuildShellBuiltinSet(),
        };
        return (new ShellRunner(ctx), vm);
    }

    // ── cd ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Sugar_CdAffectsCwd()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        string original = Environment.CurrentDirectory;
        var (runner, vm) = MakeRunner();
        try
        {
            runner.Run("cd /tmp");
            // process.chdir pushes onto the directory stack, so depth should be ≥ 1.
            Assert.Equal("/tmp", Environment.CurrentDirectory,
                StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.CurrentDirectory = original;
        }
    }

    [Fact]
    public void Sugar_PwdPrintsCwd()
    {
        var sw = new StringWriter();
        var (runner, _) = MakeRunner(sw);

        runner.Run("pwd");

        string output = sw.ToString().Trim();
        Assert.Equal(Environment.CurrentDirectory, output,
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sugar_CdDashAfterPush_RestoresCwd()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        string original = Environment.CurrentDirectory;
        var sw = new StringWriter();
        var (runner, _) = MakeRunner(sw);
        try
        {
            // Push /tmp onto the stack.
            runner.Run("cd /tmp");
            Assert.Equal("/tmp", Environment.CurrentDirectory,
                StringComparer.OrdinalIgnoreCase);

            // cd - should restore to the original directory and print it.
            runner.Run("cd -");
            Assert.Equal(original, Environment.CurrentDirectory,
                StringComparer.OrdinalIgnoreCase);

            // The pop should have printed the restored directory.
            string printed = sw.ToString().Trim();
            Assert.Contains(original, printed, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.CurrentDirectory = original;
        }
    }

    [Fact]
    public void Sugar_ExitPropagatesExitException()
    {
        var (runner, _) = MakeRunner();

        // process.exit(7) → ExitException(7) propagates out of EvaluateSource → out of Run.
        var ex = Assert.Throws<ExitException>(() => runner.Run("exit 7"));
        Assert.Equal(7, ex.ExitCode);
    }

    [Fact]
    public void Sugar_QuitAliasOfExit_PropagatesExitException()
    {
        var (runner, _) = MakeRunner();

        var ex = Assert.Throws<ExitException>(() => runner.Run("quit 0"));
        Assert.Equal(0, ex.ExitCode);
    }

    // ── piped pwd is NOT desugared ────────────────────────────────────────────

    [Fact]
    public void Sugar_PipedPwdNotDesugared()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        // `pwd | cat` has two stages → no desugaring. It runs via the process pipeline.
        // On POSIX, /usr/bin/pwd exists, so the pipeline should succeed without throwing
        // the sugar arity error ("pwd: too many arguments").
        var (runner, _) = MakeRunner();
        // No exception means we didn't accidentally apply the sugar code path.
        runner.Run("pwd | cat");
    }

    // ── strict-mode interaction ───────────────────────────────────────────────

    [Fact]
    public void Sugar_CdSetsExitCodeToZero()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        string original = Environment.CurrentDirectory;
        var (runner, vm) = MakeRunner();
        try
        {
            // Ensure the exit code is set to 0 after a successful sugar command.
            vm.LastExitCode = 42;
            runner.Run("cd /tmp");
            Assert.Equal(0, vm.LastExitCode);
        }
        finally
        {
            Environment.CurrentDirectory = original;
        }
    }

    [Fact]
    public void Sugar_PwdSetsExitCodeToZero()
    {
        var (runner, vm) = MakeRunner();
        vm.LastExitCode = 99;
        runner.Run("pwd");
        Assert.Equal(0, vm.LastExitCode);
    }

    // ── arity errors propagate as RuntimeError ────────────────────────────────

    [Fact]
    public void Sugar_CdTooManyArgs_ThrowsRuntimeError()
    {
        var (runner, _) = MakeRunner();
        var ex = Assert.Throws<RuntimeError>(() => runner.Run("cd /a /b"));
        Assert.Equal(StashErrorTypes.CommandError, ex.ErrorType);
        Assert.Equal("cd: too many arguments", ex.Message);
    }

    [Fact]
    public void Sugar_PwdWithArg_ThrowsRuntimeError()
    {
        var (runner, _) = MakeRunner();
        var ex = Assert.Throws<RuntimeError>(() => runner.Run("pwd /tmp"));
        Assert.Equal(StashErrorTypes.CommandError, ex.ErrorType);
        Assert.Equal("pwd: too many arguments", ex.Message);
    }

    [Fact]
    public void Sugar_ExitNonNumericArg_ThrowsRuntimeError()
    {
        var (runner, _) = MakeRunner();
        var ex = Assert.Throws<RuntimeError>(() => runner.Run("exit notanumber"));
        Assert.Equal(StashErrorTypes.CommandError, ex.ErrorType);
        Assert.Equal("exit: numeric argument required", ex.Message);
    }
}
