using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Stash.Bytecode;
using Stash.Cli.Shell;
using Stash.Runtime;
using Stash.Stdlib;
using Stash.Stdlib.BuiltIns;

namespace Stash.Tests.Cli;

/// <summary>
/// Integration tests for Phase B alias dispatch — bare-word alias invocation via
/// <see cref="ShellRunner"/>, cycle detection, classifier routing, and pipeline expansion.
///
/// Subprocess output is not connected to <c>vm.Output</c>, so tests that need to
/// inspect what a template alias printed use file redirects (the same technique as
/// <see cref="ShellRedirectTests"/>).  Function aliases that use <c>io.println</c> write
/// to <c>vm.Output</c> and can be captured via <see cref="StringWriter"/>.
/// </summary>
public sealed class AliasDispatchTests : IDisposable
{
    // ── Temp-file bookkeeping ─────────────────────────────────────────────────

    private readonly string _tmpRoot;
    private int _tmpIdx;

    public AliasDispatchTests()
    {
        _tmpRoot = Path.Combine(Path.GetTempPath(), $"stash-alias-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tmpRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpRoot, recursive: true); } catch { }
    }

    private string TmpFile() =>
        Path.Combine(_tmpRoot, $"out{System.Threading.Interlocked.Increment(ref _tmpIdx)}.txt");

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (ShellRunner Runner, VirtualMachine Vm, ShellLineClassifier Classifier, StringWriter Output)
        MakeRunner()
    {
        var sw = new StringWriter();
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        vm.Output = sw;
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
        var classifier = new ShellLineClassifier(ctx);

        // Wire the AliasExecutor delegate so alias.exec() and template re-feed work.
        AliasDispatcher.Wire(runner, vm);

        return (runner, vm, classifier, sw);
    }

    private static void DefineTemplateAlias(VirtualMachine vm, string name, string body)
    {
        vm.AliasRegistry.Define(new AliasRegistry.AliasEntry
        {
            Name = name,
            Kind = AliasRegistry.AliasKind.Template,
            TemplateBody = body,
        });
    }

    // =========================================================================
    // 1. Bare-word template alias — no args
    // =========================================================================

    [Fact]
    public void TemplateAlias_BareWord_NoArgs_RunsExpandedCommand()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        var (runner, vm, _, _) = MakeRunner();
        string tmp = TmpFile();
        DefineTemplateAlias(vm, "gst", $"echo hi > {tmp}");

        runner.Run("gst");

        Assert.Equal("hi\n", File.ReadAllText(tmp));
        Assert.Equal(0, vm.LastExitCode);
    }

    // =========================================================================
    // 2. Template alias with ${args}
    // =========================================================================

    [Fact]
    public void TemplateAlias_WithArgsPlaceholder_ForwardsAllArgs()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        var (runner, vm, _, _) = MakeRunner();
        string tmp = TmpFile();
        DefineTemplateAlias(vm, "gst", "echo ${args} > " + tmp);

        runner.Run("gst foo bar");

        string output = File.ReadAllText(tmp).Trim();
        Assert.Contains("foo", output, StringComparison.Ordinal);
        Assert.Contains("bar", output, StringComparison.Ordinal);
    }

    // =========================================================================
    // 3. Template alias with ${args[N]}
    // =========================================================================

    [Fact]
    public void TemplateAlias_IndexedArg_ForwardsSingleArg()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        var (runner, vm, _, _) = MakeRunner();
        string tmp = TmpFile();
        DefineTemplateAlias(vm, "gst", "echo ${args[1]} > " + tmp);

        runner.Run("gst foo bar");

        Assert.Contains("bar", File.ReadAllText(tmp), StringComparison.Ordinal);
    }

    // =========================================================================
    // 4. \name bypasses alias — attempts PATH lookup
    // =========================================================================

    [Fact]
    public void ForcedPrefix_BypassesAlias_AttemptsPathLookup()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        var (runner, vm, _, _) = MakeRunner();
        string tmp = TmpFile();
        DefineTemplateAlias(vm, "gst", "echo hi > " + tmp);

        // \gst — forced shell mode, bypasses alias registry; gst is not on PATH.
        var ex = Assert.Throws<RuntimeError>(() => runner.Run(@"\gst foo"));
        Assert.Equal(StashErrorTypes.CommandError, ex.ErrorType);
        // Output file should not exist (alias was not executed).
        Assert.False(File.Exists(tmp), "alias should not have run");
    }

    // =========================================================================
    // 5. !name bypasses alias — strict mode PATH lookup
    // =========================================================================

    [Fact]
    public void StrictPrefix_BypassesAlias_AttemptsPathLookup()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        var (runner, vm, _, _) = MakeRunner();
        string tmp = TmpFile();
        DefineTemplateAlias(vm, "gst", "echo hi > " + tmp);

        // !gst — strict mode, bypasses alias; gst is not on PATH → CommandError.
        var ex = Assert.Throws<RuntimeError>(() => runner.Run("!gst foo"));
        Assert.Equal(StashErrorTypes.CommandError, ex.ErrorType);
        Assert.False(File.Exists(tmp), "alias should not have run");
    }

    // =========================================================================
    // 6. Cycle detection — a → b → a
    // =========================================================================

    [Fact]
    public void CycleDetection_MutualRecursion_ThrowsAliasError()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        var (runner, vm, _, _) = MakeRunner();
        DefineTemplateAlias(vm, "a", "b");
        DefineTemplateAlias(vm, "b", "a");

        var ex = Assert.Throws<RuntimeError>(() => runner.Run("a"));
        Assert.Equal(StashErrorTypes.AliasError, ex.ErrorType);
        Assert.Contains("recursive alias expansion", ex.Message, StringComparison.Ordinal);
        Assert.Contains("a", ex.Message, StringComparison.Ordinal);
        Assert.Contains("b", ex.Message, StringComparison.Ordinal);
    }

    // =========================================================================
    // 7. Chain depth — 33-deep chain → AliasError
    // =========================================================================

    [Fact]
    public void ChainDepth_TooDeep_ThrowsAliasError()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        var (runner, vm, _, _) = MakeRunner();

        // Build a chain of 33 aliases: alias0 → alias1 → … → alias32.
        // The depth guard fires when the stack already holds 32 entries and we
        // try to push a 33rd (alias32).
        for (int i = 0; i < 32; i++)
            DefineTemplateAlias(vm, $"alias{i}", $"alias{i + 1}");
        DefineTemplateAlias(vm, "alias32", "echo done");

        var ex = Assert.Throws<RuntimeError>(() => runner.Run("alias0"));
        Assert.Equal(StashErrorTypes.AliasError, ex.ErrorType);
        Assert.Contains("too deep", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // 8. Chain: a → b where b redirects to a file
    // =========================================================================

    [Fact]
    public void Chain_TwoAliases_ExecutesTerminalCommand()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        var (runner, vm, _, _) = MakeRunner();
        string tmp = TmpFile();
        DefineTemplateAlias(vm, "a", "b");
        DefineTemplateAlias(vm, "b", "echo hi > " + tmp);

        runner.Run("a");

        Assert.Equal("hi\n", File.ReadAllText(tmp));
    }

    // =========================================================================
    // 9. Function alias — single-stage (output via io.println → vm.Output)
    // =========================================================================

    [Fact]
    public void FunctionAlias_SingleStage_InvokesCallable()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        var (runner, vm, _, sw) = MakeRunner();

        ShellRunner.EvaluateSource(
            """alias.define("g", () => { io.println("from g"); });""",
            vm);

        runner.Run("g");

        Assert.Contains("from g", sw.ToString(), StringComparison.Ordinal);
    }

    // =========================================================================
    // 10. Classifier routes alias name to Shell even when Stash global exists
    // =========================================================================

    [Fact]
    public void Classifier_AliasOverridesStashGlobal_ReturnsShellMode()
    {
        var (_, vm, classifier, _) = MakeRunner();

        // Declare a Stash global with the same name as the alias.
        ShellRunner.EvaluateSource("let gco = 5;", vm);

        // Register an alias for the same name.
        DefineTemplateAlias(vm, "gco", "echo gco-expanded ${args}");

        // Alias should win: bare-word "gco main" → Shell, not Stash.
        LineMode mode = classifier.Classify("gco main");
        Assert.Equal(LineMode.Shell, mode);
    }

    // =========================================================================
    // 11. Pipeline template alias expansion (Linux only)
    // =========================================================================

    [Fact]
    public void PipelineAlias_TemplateInPipelineStage_IsExpanded()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return;

        var (runner, vm, _, _) = MakeRunner();
        string tmp = TmpFile();

        // alias g = "wc -l" — counts lines
        DefineTemplateAlias(vm, "g", "wc -l");

        // "cat /etc/hostname | g > file" → "cat /etc/hostname | wc -l > file" → "1"
        runner.Run($"cat /etc/hostname | g > {tmp}");

        string output = File.ReadAllText(tmp).Trim();
        // wc -l output is a small integer (hostname has 1 line)
        Assert.Matches(@"^\s*\d+\s*$", output);
    }

    // =========================================================================
    // 12. Strict args — alias body has no placeholders but args are passed
    // =========================================================================

    [Fact]
    public void StrictArgs_NoPlaceholderWithArgs_ThrowsAliasError()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        var (runner, vm, _, _) = MakeRunner();
        DefineTemplateAlias(vm, "gst", "echo hi");  // no ${args} placeholder

        var ex = Assert.Throws<RuntimeError>(() => runner.Run("gst foo bar"));
        Assert.Equal(StashErrorTypes.AliasError, ex.ErrorType);
        Assert.Contains("no arguments", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // 13. AliasExecutor delegate — invoked from alias.exec for template aliases
    // =========================================================================

    [Fact]
    public void AliasExec_TemplateAlias_ExecutesViaDelegate()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        var (_, vm, _, _) = MakeRunner();
        string tmp = TmpFile();
        DefineTemplateAlias(vm, "gst", "echo aliasexec > " + tmp);

        // alias.exec() should trigger the AliasExecutor delegate for template aliases.
        ShellRunner.EvaluateSource(
            """let code = alias.exec("gst", []);""",
            vm);

        Assert.Equal("aliasexec\n", File.ReadAllText(tmp));
    }
}
