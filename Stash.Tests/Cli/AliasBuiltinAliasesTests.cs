using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Stash.Bytecode;
using Stash.Cli.Shell;
using Stash.Runtime;
using Stash.Stdlib;
using Stash.Stdlib.BuiltIns;
using Xunit;

namespace Stash.Tests.Cli;

/// <summary>
/// Phase D tests: five shell commands (cd, pwd, exit, quit, history) are now
/// registered as built-in aliases at startup via <see cref="BuiltinAliases.RegisterBuiltins"/>.
/// </summary>
public sealed class AliasBuiltinAliasesTests : IDisposable
{
    // ── Fixture ───────────────────────────────────────────────────────────────

    private readonly StringWriter _output = new();
    private readonly VirtualMachine _vm;
    private readonly ShellRunner _runner;

    public AliasBuiltinAliasesTests()
    {
        _vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        _vm.Output      = _output;
        _vm.ErrorOutput = Console.Error;
        _vm.EmbeddedMode = true;

        var ctx = new ShellContext
        {
            Vm              = _vm,
            PathCache       = new PathExecutableCache(_ => true),
            Keywords        = ShellContext.BuildKeywordSet(),
            Namespaces      = new HashSet<string>(StdlibRegistry.NamespaceNames, StringComparer.Ordinal),
            ShellBuiltinNames = ShellContext.BuildShellBuiltinSet(),
        };

        _runner = new ShellRunner(ctx);

        // Wire also calls BuiltinAliases.RegisterBuiltins.
        AliasDispatcher.Wire(_runner, _vm);
    }

    public void Dispose() => _output.Dispose();

    // ── 1. Five builtins are registered with source == "builtin" ─────────────

    [Fact]
    public void Builtins_CdExistsWithBuiltinSource()
    {
        Assert.True(_vm.AliasRegistry.Exists("cd"));
        Assert.True(_vm.AliasRegistry.TryGet("cd", out var entry) && entry is not null);
        Assert.Equal(AliasRegistry.AliasSource.Builtin, entry!.Source);
    }

    [Theory]
    [InlineData("pwd")]
    [InlineData("exit")]
    [InlineData("quit")]
    [InlineData("history")]
    public void Builtins_AllFiveExistWithBuiltinSource(string name)
    {
        Assert.True(_vm.AliasRegistry.Exists(name));
        Assert.True(_vm.AliasRegistry.TryGet(name, out var entry) && entry is not null);
        Assert.Equal(AliasRegistry.AliasSource.Builtin, entry!.Source);
    }

    // ── 2. All five appear in alias.list() ────────────────────────────────────

    [Fact]
    public void Builtins_AllFiveInAliasList()
    {
        var names = new HashSet<string>(_vm.AliasRegistry.Names(), StringComparer.Ordinal);
        Assert.Contains("cd",      names);
        Assert.Contains("pwd",     names);
        Assert.Contains("exit",    names);
        Assert.Contains("quit",    names);
        Assert.Contains("history", names);
    }

    // ── 3. Bare-word pwd prints cwd ───────────────────────────────────────────

    [Fact]
    public void Builtins_Pwd_PrintsCwd()
    {
        _runner.Run("pwd");
        Assert.Equal(Environment.CurrentDirectory, _output.ToString().Trim(),
            StringComparer.OrdinalIgnoreCase);
    }

    // ── 4. Bare-word cd changes directory ─────────────────────────────────────

    [Fact]
    public void Builtins_Cd_ChangesDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        string original = Environment.CurrentDirectory;
        try
        {
            _runner.Run("cd /tmp");
            Assert.Equal("/tmp", Environment.CurrentDirectory,
                StringComparer.OrdinalIgnoreCase);

            _output.GetStringBuilder().Clear();
            _runner.Run("pwd");
            Assert.Equal("/tmp", _output.ToString().Trim(),
                StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.CurrentDirectory = original;
        }
    }

    // ── 5. alias.define without override=true → AliasError ───────────────────

    [Fact]
    public void Builtins_DefineWithoutOverride_ThrowsAliasError()
    {
        var ex = Assert.Throws<RuntimeError>(() =>
            ShellRunner.EvaluateSource("alias.define(\"cd\", \"echo hi\");", _vm));

        Assert.Equal(StashErrorTypes.AliasError, ex.ErrorType);
        Assert.Contains("override", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── 6. alias.define with override=true → succeeds; cd no longer changes cwd

    [Fact]
    public void Builtins_DefineWithOverride_OverridesBuiltin()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        string savedCwd = Environment.CurrentDirectory;

        // Override cd with a function alias that just prints to vm.Output
        ShellRunner.EvaluateSource(
            "alias.define(\"cd\", (path) => { io.println(\"overridden:\" + path); }, AliasOptions { override: true });",
            _vm);

        // Running "cd /tmp" should call the override, not env.chdir
        _runner.Run("cd /tmp");

        // cwd unchanged
        Assert.Equal(savedCwd, Environment.CurrentDirectory);

        // Override ran and printed to output
        Assert.Contains("overridden:", _output.ToString(), StringComparison.Ordinal);
    }

    // ── 7. unalias cd → AliasError (builtin cannot be removed) ───────────────

    [Fact]
    public void Builtins_UnaliasCd_ThrowsAliasError()
    {
        var ex = Assert.Throws<RuntimeError>(() =>
            ShellRunner.EvaluateSource("alias.remove(\"cd\");", _vm));

        Assert.Equal(StashErrorTypes.AliasError, ex.ErrorType);
        Assert.Contains("cannot remove built-in", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── 8. unalias --force cd → disables; cd invisible; cd falls through ─────

    [Fact]
    public void Builtins_ForceDisable_MakesBuiltinInvisible()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        // Force-disable via shell sugar
        _runner.Run("unalias --force cd");

        // cd is now invisible
        Assert.False(_vm.AliasRegistry.Exists("cd"));
        Assert.DoesNotContain("cd", _vm.AliasRegistry.Names());

        // alias.get("cd") via Stash returns null
        ShellRunner.EvaluateSource(
            "let r = alias.get(\"cd\"); io.println(r == null ? \"null\" : \"found\");",
            _vm);
        Assert.Contains("null", _output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Builtins_ForceDisable_CdFallsThroughToPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        _runner.Run("unalias --force cd");

        // cd is not a standalone executable — spawn will fail
        var ex = Assert.Throws<RuntimeError>(() => _runner.Run("cd /tmp"));
        Assert.Equal(StashErrorTypes.CommandError, ex.ErrorType);
    }

    // ── 9. Re-registering builtins re-enables cd after force-disable ──────────

    [Fact]
    public void Builtins_ReRegister_ReEnablesForceDisabled()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        _runner.Run("unalias --force cd");
        Assert.False(_vm.AliasRegistry.Exists("cd"));

        // Simulate REPL restart: call RegisterBuiltins again
        BuiltinAliases.RegisterBuiltins(_vm);

        Assert.True(_vm.AliasRegistry.Exists("cd"));
        Assert.True(_vm.AliasRegistry.TryGet("cd", out var entry) && entry is not null);
        Assert.Equal(AliasRegistry.AliasSource.Builtin, entry!.Source);
    }

    // ── 10. Builtin descriptions are set ──────────────────────────────────────

    [Theory]
    [InlineData("cd",      "directory")]
    [InlineData("pwd",     "directory")]
    [InlineData("exit",    "exit")]
    [InlineData("quit",    "exit")]
    [InlineData("history", "history")]
    public void Builtins_HaveNonEmptyDescriptions(string name, string expectedSubstring)
    {
        Assert.True(_vm.AliasRegistry.TryGet(name, out var entry) && entry is not null);
        Assert.NotNull(entry!.Description);
        Assert.Contains(expectedSubstring, entry.Description!, StringComparison.OrdinalIgnoreCase);
    }

    // ── 11. alias.list() groups builtins under [builtin] header ──────────────

    [Fact]
    public void Builtins_ListPretty_ShowsBuiltinGroup()
    {
        ShellRunner.EvaluateSource("alias.__listPretty();", _vm);
        string output = _output.ToString();
        Assert.Contains("[builtin]", output, StringComparison.Ordinal);
        Assert.Contains("cd", output, StringComparison.Ordinal);
    }

    // ── 12. alias.get("cd") returns AliasInfo with source="builtin" ──────────

    [Fact]
    public void Builtins_AliasGet_ReturnCorrectSource()
    {
        ShellRunner.EvaluateSource(
            "let info = alias.get(\"cd\"); io.println(info.source);",
            _vm);
        Assert.Contains("builtin", _output.ToString(), StringComparison.Ordinal);
    }

    // ── 13. exit and quit still dispatch correctly ────────────────────────────

    [Fact]
    public void Builtins_Exit_PropagatesExitException()
    {
        var ex = Assert.Throws<ExitException>(() => _runner.Run("exit 7"));
        Assert.Equal(7, ex.ExitCode);
    }

    [Fact]
    public void Builtins_Quit_PropagatesExitException()
    {
        var ex = Assert.Throws<ExitException>(() => _runner.Run("quit 0"));
        Assert.Equal(0, ex.ExitCode);
    }

    // ── 14. Force-disabling a non-existent alias → AliasError ─────────────────

    [Fact]
    public void Builtins_ForceDisable_UnknownName_ThrowsAliasError()
    {
        var ex = Assert.Throws<RuntimeError>(() =>
            _runner.Run("unalias --force nonexistent_alias_xyz"));
        Assert.Equal(StashErrorTypes.AliasError, ex.ErrorType);
    }
}
