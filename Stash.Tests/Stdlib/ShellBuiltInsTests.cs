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

namespace Stash.Tests.Stdlib;

/// <summary>
/// Tests for the <c>shell</c> namespace added in Phase A of the Process Namespace
/// Decomposition: <c>shell.lastExitCode()</c> and capability-gating.
/// </summary>
public class ShellBuiltInsTests
{
    private static VirtualMachine MakeVm(StashCapabilities capabilities = StashCapabilities.All)
    {
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals(capabilities));
        vm.EmbeddedMode = true;
        vm.Output = Console.Out;
        vm.ErrorOutput = Console.Error;
        return vm;
    }

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

    // =========================================================================
    // shell.lastExitCode
    // =========================================================================

    [Fact]
    public void Shell_LastExitCode_DefaultZero()
    {
        var vm = MakeVm();
        // Fresh VM — no command has run — defaults to 0.
        string output = RunSource("io.println(shell.lastExitCode());", vm);
        Assert.Equal("0", output);
    }

    [Fact]
    public void Shell_LastExitCode_AfterCommand_ReflectsExitCode()
    {
        // POSIX only: run a command that exits with 1, then verify shell.lastExitCode().
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        var (runner, vm) = MakeRunner();

        // 'false' always exits with 1.
        runner.Run("false");

        string output = RunSource("io.println(shell.lastExitCode());", vm);
        Assert.Equal("1", output);
    }

    // =========================================================================
    // Capability gating
    // =========================================================================

    [Fact]
    public void Shell_NotRegistered_WhenCapabilityDisabled()
    {
        // Build a VM without the Shell capability — the 'shell' global must not exist.
        var caps = StashCapabilities.All & ~StashCapabilities.Shell;
        var globals = StdlibDefinitions.CreateVMGlobals(caps);
        Assert.False(globals.ContainsKey("shell"),
            "The 'shell' namespace should not be registered when StashCapabilities.Shell is absent.");
    }
}
