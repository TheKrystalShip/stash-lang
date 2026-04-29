using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Stash.Bytecode;
using Stash.Cli.Shell;
using Stash.Runtime;
using Stash.Stdlib;

namespace Stash.Tests.Cli;

/// <summary>
/// Integration tests for shell-mode strict execution (Phase 5, §4.3).
/// Tests exercise the <c>!</c> prefix and verify that non-zero exit codes raise
/// <see cref="StashErrorTypes.CommandError"/> while zero-exit runs silently.
///
/// POSIX-only tests skip on Windows.
/// </summary>
public class ShellStrictModeTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ShellRunner MakeRunner()
    {
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        var ctx = new ShellContext
        {
            Vm = vm,
            PathCache = new PathExecutableCache(_ => true),
            Keywords = ShellContext.BuildKeywordSet(),
            Namespaces = new HashSet<string>(StdlibRegistry.NamespaceNames, StringComparer.Ordinal),
            ShellBuiltinNames = ShellContext.BuildShellBuiltinSet(),
        };
        return new ShellRunner(ctx);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Strict_NonZeroExit_ThrowsCommandError()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        var runner = MakeRunner();
        var ex = Assert.Throws<RuntimeError>(() =>
            runner.Run("!sh -c 'exit 1'"));
        Assert.Equal(StashErrorTypes.CommandError, ex.ErrorType);
        Assert.Contains("1", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Strict_ZeroExit_NoError()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        // No exception should be thrown when the command exits 0.
        MakeRunner().Run("!sh -c 'exit 0'");
    }

    [Fact]
    public void Strict_PipelineAnyNonZero_Throws()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        // Pipeline where the last stage exits non-zero → throws.
        var runner = MakeRunner();
        var ex = Assert.Throws<RuntimeError>(() =>
            runner.Run("!echo hello | sh -c 'exit 2'"));
        Assert.Equal(StashErrorTypes.CommandError, ex.ErrorType);
    }

    [Fact]
    public void Strict_PipelineAllZero_Succeeds()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        // Both stages exit 0 → no error.
        MakeRunner().Run("!echo hello | cat");
    }

    [Fact]
    public void NonStrict_NonZero_Silent()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        // Without '!' prefix, a non-zero exit is silent; LastExitCode is updated.
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        var ctx = new ShellContext
        {
            Vm = vm,
            PathCache = new PathExecutableCache(_ => true),
            Keywords = ShellContext.BuildKeywordSet(),
            Namespaces = new HashSet<string>(StdlibRegistry.NamespaceNames, StringComparer.Ordinal),
            ShellBuiltinNames = ShellContext.BuildShellBuiltinSet(),
        };
        var runner = new ShellRunner(ctx);
        runner.Run("sh -c 'exit 1'");
        Assert.Equal(1, vm.LastExitCode);
    }

    [Fact]
    public void Strict_ForcedPrefix_AlsoEnforcesStrict()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        // !\cmd sets both strict and forced; a non-zero exit still throws.
        var runner = MakeRunner();
        var ex = Assert.Throws<RuntimeError>(() =>
            runner.Run("!\\sh -c 'exit 3'"));
        Assert.Equal(StashErrorTypes.CommandError, ex.ErrorType);
    }
}
