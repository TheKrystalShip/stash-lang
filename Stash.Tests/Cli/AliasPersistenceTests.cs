using System;
using System.Collections.Generic;
using System.IO;
using Stash.Bytecode;
using Stash.Cli.Shell;
using Stash.Runtime;
using Stash.Stdlib;
using Stash.Stdlib.BuiltIns;
using Xunit;

namespace Stash.Tests.Cli;

/// <summary>
/// Integration tests for Phase F alias persistence — <c>alias.save</c>, <c>alias.load</c>,
/// <c>alias.__removeSaved</c>, and the <c>aliases.stash</c> file round-trip.
/// </summary>
[Collection("AliasStaticState")]
public sealed class AliasPersistenceTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly string _aliasFile;
    private readonly string? _savedPathOverride;

    public AliasPersistenceTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), $"stash-persist-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tmpDir);
        _aliasFile = Path.Combine(_tmpDir, "aliases.stash");

        // Redirect AliasPersistence to our temp directory so tests never touch
        // the real ~/.config/stash/aliases.stash.
        _savedPathOverride = AliasPersistence.PathOverride;
        AliasPersistence.PathOverride = _aliasFile;
    }

    public void Dispose()
    {
        AliasPersistence.PathOverride = _savedPathOverride;
        try { Directory.Delete(_tmpDir, recursive: true); } catch { }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (ShellRunner Runner, VirtualMachine Vm) MakeEnv()
    {
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        vm.Output = new StringWriter();
        vm.ErrorOutput = Console.Error;
        vm.EmbeddedMode = true;

        var ctx = new ShellContext
        {
            Vm = vm,
            PathCache = new PathExecutableCache(_ => true),
            Keywords = ShellContext.BuildKeywordSet(),
            Namespaces = new HashSet<string>(StdlibRegistry.NamespaceNames, StringComparer.Ordinal),
            ShellBuiltinNames = ShellContext.BuildShellBuiltinSet(),
        };

        var runner = new ShellRunner(ctx);
        AliasDispatcher.Wire(runner, vm);
        return (runner, vm);
    }

    private static void DefineTemplate(VirtualMachine vm, string name, string body,
        AliasRegistry.AliasSource source = AliasRegistry.AliasSource.Repl)
    {
        vm.AliasRegistry.Define(new AliasRegistry.AliasEntry
        {
            Name = name,
            Kind = AliasRegistry.AliasKind.Template,
            TemplateBody = body,
            Source = source,
        });
    }

    // ── 1. Single alias save writes file ─────────────────────────────────────

    [Fact]
    public void Save_SingleName_WritesFileWithCorrectContent()
    {
        var (_, vm) = MakeEnv();
        DefineTemplate(vm, "g", "git ${args}");

        string path = AliasPersistence.Save(vm, "g");

        Assert.Equal(_aliasFile, path);
        Assert.True(File.Exists(_aliasFile));
        string content = File.ReadAllText(_aliasFile);
        Assert.Contains("alias.define(\"g\", \"git \\${args}\");", content);
    }

    // ── 2. alias.save() (all) writes all non-builtin aliases ─────────────────

    [Fact]
    public void Save_All_WritesAllNonBuiltinAliases()
    {
        var (_, vm) = MakeEnv();
        DefineTemplate(vm, "g",   "git ${args}");
        DefineTemplate(vm, "gst", "git status");

        AliasPersistence.Save(vm);

        string content = File.ReadAllText(_aliasFile);
        Assert.Contains("alias.define(\"g\",", content);
        Assert.Contains("alias.define(\"gst\",", content);
        // Builtin aliases (cd, pwd, exit, quit, history) must NOT be in the file.
        Assert.DoesNotContain("alias.define(\"cd\",", content);
    }

    // ── 3. After saving, entry.Source becomes Saved ──────────────────────────

    [Fact]
    public void Save_UpdatesSourceToSaved()
    {
        var (_, vm) = MakeEnv();
        DefineTemplate(vm, "g", "git status");
        Assert.Equal(AliasRegistry.AliasSource.Repl,
            GetEntry(vm, "g").Source);

        AliasPersistence.Save(vm, "g");

        Assert.Equal(AliasRegistry.AliasSource.Saved,
            GetEntry(vm, "g").Source);
    }

    // ── 4. alias.load() re-registers aliases from file ───────────────────────

    [Fact]
    public void Load_ReadsFileAndRegistersAliases()
    {
        var (runner, vm) = MakeEnv();
        File.WriteAllText(_aliasFile,
            "alias.define(\"g\", \"git \\${args}\");\n" +
            "alias.define(\"gst\", \"git status\");\n");

        int count = AliasPersistence.Load(vm, runner);

        Assert.Equal(2, count);
        Assert.True(vm.AliasRegistry.Exists("g"));
        Assert.True(vm.AliasRegistry.Exists("gst"));
    }

    // ── 5. Loaded aliases have Source = Saved ────────────────────────────────

    [Fact]
    public void Load_TagsNewAliasesAsSaved()
    {
        var (runner, vm) = MakeEnv();
        File.WriteAllText(_aliasFile, "alias.define(\"g\", \"git ${args}\");\n");

        AliasPersistence.Load(vm, runner);

        Assert.Equal(AliasRegistry.AliasSource.Saved, GetEntry(vm, "g").Source);
    }

    // ── 6. Round-trip: define → save → clear → load → entries match ──────────

    [Fact]
    public void RoundTrip_DefineAndReload_PreservesEntries()
    {
        var (runner1, vm1) = MakeEnv();
        DefineTemplate(vm1, "g",   "git ${args}");
        DefineTemplate(vm1, "gst", "git status");
        AliasPersistence.Save(vm1);

        // Simulate fresh session with a new VM.
        var (runner2, vm2) = MakeEnv();
        int loaded = AliasPersistence.Load(vm2, runner2);

        Assert.Equal(2, loaded);
        Assert.True(vm2.AliasRegistry.TryGet("g", out var gEntry) && gEntry is not null);
        Assert.Equal("git ${args}", gEntry!.TemplateBody);
        Assert.True(vm2.AliasRegistry.TryGet("gst", out var gstEntry) && gstEntry is not null);
        Assert.Equal("git status", gstEntry!.TemplateBody);
    }

    // ── 7. Template body with ${args} survives round-trip (escaping correct) ──

    [Fact]
    public void RoundTrip_TemplateWithArgPlaceholder_PreservesPlaceholder()
    {
        var (runner1, vm1) = MakeEnv();
        DefineTemplate(vm1, "g", "git ${args}");
        AliasPersistence.Save(vm1);

        var (runner2, vm2) = MakeEnv();
        AliasPersistence.Load(vm2, runner2);

        Assert.True(vm2.AliasRegistry.TryGet("g", out var e) && e is not null);
        Assert.Equal("git ${args}", e!.TemplateBody);
    }

    // ── 8. Function alias with top-level fn serializes by name ───────────────

    [Fact]
    public void Save_FunctionAliasWithTopLevelFn_SerializesAsName()
    {
        var (runner, vm) = MakeEnv();
        // Define a top-level fn, then create a function alias pointing to it.
        ShellRunner.EvaluateSource("fn deploy_impl() { return 0; }", vm);
        ShellRunner.EvaluateSource("alias.define(\"deploy\", deploy_impl);", vm);

        string path = AliasPersistence.Save(vm, "deploy");

        string content = File.ReadAllText(path);
        // Should contain the fn name as identifier (no quotes).
        Assert.Contains("alias.define(\"deploy\", deploy_impl);", content);
    }

    // ── 9. Function alias with top-level fn: load restores correctly ─────────

    [Fact]
    public void RoundTrip_FunctionAlias_RestoresCorrectly()
    {
        var (runner1, vm1) = MakeEnv();
        ShellRunner.EvaluateSource("fn deploy_impl() { return 0; }", vm1);
        ShellRunner.EvaluateSource("alias.define(\"deploy\", deploy_impl);", vm1);
        AliasPersistence.Save(vm1, "deploy");

        // Fresh session with same fn defined (simulating an rc file loading it).
        var (runner2, vm2) = MakeEnv();
        ShellRunner.EvaluateSource("fn deploy_impl() { return 0; }", vm2);
        int loaded = AliasPersistence.Load(vm2, runner2);

        Assert.Equal(1, loaded);
        Assert.True(vm2.AliasRegistry.TryGet("deploy", out var entry) && entry is not null);
        Assert.Equal(AliasRegistry.AliasKind.Function, entry!.Kind);
        Assert.NotNull(entry.FunctionBody);
    }

    // ── 10. Lambda closure → save throws AliasError ──────────────────────────

    [Fact]
    public void Save_FunctionAliasWithLambda_ThrowsAliasError()
    {
        var (runner, vm) = MakeEnv();
        // Lambda with a capture — has upvalues.
        ShellRunner.EvaluateSource(
            "let prefix = \"git\"; alias.define(\"g\", (x) => prefix + x);", vm);

        var ex = Assert.Throws<RuntimeError>(() => AliasPersistence.Save(vm, "g"));
        Assert.Equal(StashErrorTypes.AliasError, ex.ErrorType);
        Assert.Contains("top-level fn", ex.Message);
    }

    // ── 11. AliasOptions confirm: round-trips correctly ──────────────────────

    [Fact]
    public void Save_AliasWithConfirm_SerializesAndLoads()
    {
        var (runner1, vm1) = MakeEnv();
        // Define via Stash source so AliasOptions struct is parsed.
        ShellRunner.EvaluateSource(
            "alias.define(\"rm\", \"rm -i \\${args}\", AliasOptions { confirm: \"Delete files?\" });",
            vm1);
        AliasPersistence.Save(vm1, "rm");

        var (runner2, vm2) = MakeEnv();
        AliasPersistence.Load(vm2, runner2);

        Assert.True(vm2.AliasRegistry.TryGet("rm", out var entry) && entry is not null);
        Assert.Equal("Delete files?", entry!.Confirm);
    }

    // ── 12. AliasOptions closure-bodied before/after → save rejects ──────────

    [Fact]
    public void Save_AliasWithClosureHook_ThrowsAliasError()
    {
        var (runner, vm) = MakeEnv();
        // Define an alias with a closure as 'before' hook.
        ShellRunner.EvaluateSource(
            "alias.define(\"g\", \"git status\", AliasOptions { before: (n, a) => true });",
            vm);

        var ex = Assert.Throws<RuntimeError>(() => AliasPersistence.Save(vm, "g"));
        Assert.Equal(StashErrorTypes.AliasError, ex.ErrorType);
        Assert.Contains("before", ex.Message);
        Assert.Contains("top-level fn", ex.Message);
    }

    // ── 13. alias.__removeSaved removes entry from file ──────────────────────

    [Fact]
    public void RemoveSaved_RemovesEntryFromFile()
    {
        var (runner, vm) = MakeEnv();
        DefineTemplate(vm, "g",   "git ${args}");
        DefineTemplate(vm, "gst", "git status");
        AliasPersistence.Save(vm);

        int removed = AliasPersistence.RemoveSaved("g");

        Assert.Equal(1, removed);
        string content = File.ReadAllText(_aliasFile);
        Assert.DoesNotContain("alias.define(\"g\",", content);
        Assert.Contains("alias.define(\"gst\",", content);
    }

    // ── 14. RemoveSaved returns 0 when not found ──────────────────────────────

    [Fact]
    public void RemoveSaved_NotFound_ReturnsZero()
    {
        // No file yet.
        int result = AliasPersistence.RemoveSaved("nonexistent");
        Assert.Equal(0, result);
    }

    // ── 15. Corrupt aliases.stash: warns, continues, returns successful count ─

    [Fact]
    public void Load_CorruptFile_WarnsAndContinues()
    {
        var (runner, vm) = MakeEnv();
        // Mix of a valid line and an invalid one.
        File.WriteAllText(_aliasFile,
            "alias.define(\"g\", \"git status\");\n" +
            "this is not valid stash code;\n" +
            "alias.define(\"gst\", \"git status\");\n");

        var errBuf = new StringWriter();
        var prevErr = Console.Error;
        Console.SetError(errBuf);
        int count;
        try
        {
            count = AliasPersistence.Load(vm, runner);
        }
        finally
        {
            Console.SetError(prevErr);
        }

        // Two valid aliases should load; one warning for the bad line.
        Assert.Equal(2, count);
        Assert.Contains("warning", errBuf.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    // ── 16. Path resolution: $XDG_CONFIG_HOME honored ────────────────────────

    [Fact]
    public void GetPath_XdgSet_UsesXdgPath()
    {
        // Temporarily clear PathOverride to test real path resolution.
        string? saved = AliasPersistence.PathOverride;
        AliasPersistence.PathOverride = null;

        string? prevXdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        try
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _tmpDir);
            string path = AliasPersistence.GetPath();
            Assert.Equal(Path.Combine(_tmpDir, "stash", "aliases.stash"), path);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", prevXdg);
            AliasPersistence.PathOverride = saved;
        }
    }

    // ── 17. Path resolution: fallback to ~/.config/stash/aliases.stash ───────

    [Fact]
    public void GetPath_XdgNotSet_FallsBackToHomeConfig()
    {
        string? saved = AliasPersistence.PathOverride;
        AliasPersistence.PathOverride = null;

        string? prevXdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        try
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", null);
            string path = AliasPersistence.GetPath();

            // On Windows the path will use %APPDATA%; on POSIX it should be under ~/.config.
            if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.Windows))
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                Assert.Equal(Path.Combine(home, ".config", "stash", "aliases.stash"), path);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", prevXdg);
            AliasPersistence.PathOverride = saved;
        }
    }

    // ── 18. Save + Load missing file returns 0 ───────────────────────────────

    [Fact]
    public void Load_MissingFile_ReturnsZero()
    {
        var (runner, vm) = MakeEnv();
        // PathOverride points to a file that doesn't exist yet.
        int count = AliasPersistence.Load(vm, runner);
        Assert.Equal(0, count);
        Assert.False(File.Exists(_aliasFile));
    }

    // ── 19. Single-name save merges with existing file content ───────────────

    [Fact]
    public void Save_SingleName_PreservesOtherSavedEntries()
    {
        var (_, vm) = MakeEnv();
        DefineTemplate(vm, "g",   "git ${args}");
        DefineTemplate(vm, "gst", "git status");
        // Save both; then save only "gst" again to verify "g" is preserved.
        AliasPersistence.Save(vm);

        DefineTemplate(vm, "gst", "git status --short");
        AliasPersistence.Save(vm, "gst");

        string content = File.ReadAllText(_aliasFile);
        Assert.Contains("alias.define(\"g\",", content);
        Assert.Contains("alias.define(\"gst\",", content);
        Assert.Contains("git status --short", content);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AliasRegistry.AliasEntry GetEntry(VirtualMachine vm, string name)
    {
        if (!vm.AliasRegistry.TryGet(name, out AliasRegistry.AliasEntry? e) || e is null)
            throw new InvalidOperationException($"alias '{name}' not found in registry");
        return e;
    }
}
