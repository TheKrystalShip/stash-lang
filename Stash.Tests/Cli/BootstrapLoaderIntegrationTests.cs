using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Stash.Bytecode;
using Stash.Cli.Repl;
using Stash.Cli.Shell;
using Stash.Runtime;
using Stash.Stdlib;
using Stash.Stdlib.BuiltIns;

namespace Stash.Tests.Cli;

/// <summary>
/// End-to-end integration tests for <see cref="BootstrapLoader"/>.
/// Extracts bootstrap scripts to a temp dir, loads them into a VM, and
/// verifies that the expected globals and prompt registrations are present.
/// </summary>
[Collection("PromptTests")]
public sealed class BootstrapLoaderIntegrationTests : IDisposable
{
    private readonly string _tempDir;

    public BootstrapLoaderIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"stash-bootstrap-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        // Extract bootstrap files into the temp dir once per test class
        BootstrapExtractor.Extract(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static VirtualMachine MakeVm()
    {
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        vm.Output = TextWriter.Null;
        vm.ErrorOutput = TextWriter.Null;
        vm.EmbeddedMode = true;
        return vm;
    }

    // =========================================================================
    // 1. Global existence after load
    // =========================================================================

    [Fact]
    public void Load_AfterExtract_StarterGlobalExists()
    {
        PromptBuiltIns.ResetAllForTesting();
        var vm = MakeVm();
        BootstrapLoader.Load(_tempDir, vm);

        Assert.True(vm.Globals.ContainsKey("starter"),
            "Expected 'starter' global to be defined after bootstrap load");
    }

    [Fact]
    public void Load_AfterExtract_PromptGlobalIsCallable()
    {
        PromptBuiltIns.ResetAllForTesting();
        var vm = MakeVm();
        BootstrapLoader.Load(_tempDir, vm);

        // The default-prompt.stash calls prompt.set(fn ...) which registers via PromptBuiltIns
        IStashCallable? fn = PromptBuiltIns.GetRegisteredPromptFn();
        Assert.NotNull(fn);
    }

    // =========================================================================
    // 2. Theme registry after load
    // =========================================================================

    [Fact]
    public void Load_AfterExtract_SixThemesRegistered()
    {
        PromptBuiltIns.ResetAllForTesting();
        var vm = MakeVm();
        BootstrapLoader.Load(_tempDir, vm);

        List<string> themes = PromptBuiltIns.GetRegisteredThemeNamesForTesting();
        Assert.Equal(17, themes.Count);
    }

    [Fact]
    public void Load_AfterExtract_ThemeNamesMatchExpected()
    {
        PromptBuiltIns.ResetAllForTesting();
        var vm = MakeVm();
        BootstrapLoader.Load(_tempDir, vm);

        List<string> themes = PromptBuiltIns.GetRegisteredThemeNamesForTesting();

        Assert.Contains("default", themes);
        Assert.Contains("nord", themes);
        Assert.Contains("catppuccin-mocha", themes);
        Assert.Contains("monokai", themes);
        Assert.Contains("dracula", themes);
        Assert.Contains("gruvbox-dark", themes);
    }

    [Fact]
    public void Load_AfterExtract_CurrentThemeIsDefault()
    {
        PromptBuiltIns.ResetAllForTesting();
        var vm = MakeVm();
        BootstrapLoader.Load(_tempDir, vm);

        // bootstrap.stash calls theme.use("default") at the end
        string current = PromptBuiltIns.GetCurrentThemeForTesting();
        Assert.Equal("default", current);
    }

    [Fact]
    public void Load_AfterExtract_PaletteIsNonNull()
    {
        PromptBuiltIns.ResetAllForTesting();
        var vm = MakeVm();
        BootstrapLoader.Load(_tempDir, vm);

        // theme.use("default") sets a palette, so _palette should be non-null
        StashValue palette = PromptBuiltIns.GetPaletteForTesting();
        Assert.False(palette.IsNull, "Expected prompt.palette() to be non-null after loading default theme");
    }

    // =========================================================================
    // 3. Starter registry after load
    // =========================================================================

    [Fact]
    public void Load_AfterExtract_SixStartersRegistered()
    {
        PromptBuiltIns.ResetAllForTesting();
        var vm = MakeVm();
        BootstrapLoader.Load(_tempDir, vm);

        List<string> starters = PromptBuiltIns.GetRegisteredStarterNamesForTesting();
        Assert.Equal(14, starters.Count);
    }

    [Fact]
    public void Load_AfterExtract_StarterNamesMatchExpected()
    {
        PromptBuiltIns.ResetAllForTesting();
        var vm = MakeVm();
        BootstrapLoader.Load(_tempDir, vm);

        List<string> starters = PromptBuiltIns.GetRegisteredStarterNamesForTesting();

        Assert.Contains("minimal", starters);
        Assert.Contains("bash-classic", starters);
        Assert.Contains("pure", starters);
        Assert.Contains("developer", starters);
        Assert.Contains("pwsh-style", starters);
        Assert.Contains("powerline-lite", starters);
    }
}
