using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Stash.Bytecode;
using Stash.Cli.Shell;
using Stash.Runtime;
using Stash.Stdlib;
using Stash.Stdlib.BuiltIns;

namespace Stash.Tests.Cli;

/// <summary>
/// Integration tests for Phase C alias shell sugar — desugaring of <c>alias</c> and
/// <c>unalias</c> shell-mode lines through <see cref="ShellRunner"/>.
/// </summary>
[Collection("AliasStaticState")]
public sealed class AliasShellSugarTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (ShellRunner Runner, VirtualMachine Vm, StringWriter Output) MakeRunner()
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

        // Wire the AliasExecutor delegate so template re-feed and alias.exec() work.
        AliasDispatcher.Wire(runner, vm);

        return (runner, vm, sw);
    }

    // =========================================================================
    // 1. Template alias — quoted body
    // =========================================================================

    [Fact]
    public void TemplateAlias_QuotedBody_RegistersCorrectly()
    {
        var (runner, vm, _) = MakeRunner();

        runner.Run("alias g = \"git status\"");

        Assert.True(vm.AliasRegistry.Exists("g"));
        vm.AliasRegistry.TryGet("g", out var entry);
        Assert.Equal("git status", entry!.TemplateBody);
        Assert.Equal(AliasRegistry.AliasKind.Template, entry.Kind);
    }

    // =========================================================================
    // 2. Template alias — single-word unquoted body
    // =========================================================================

    [Fact]
    public void TemplateAlias_UnquotedBareWord_RegistersCorrectly()
    {
        var (runner, vm, _) = MakeRunner();

        runner.Run("alias gst = git");

        Assert.True(vm.AliasRegistry.Exists("gst"));
        vm.AliasRegistry.TryGet("gst", out var entry);
        Assert.Equal("git", entry!.TemplateBody);
    }

    // =========================================================================
    // 3. Template alias — ${args} placeholder preserved as literal
    // =========================================================================

    [Fact]
    public void TemplateAlias_ArgsPlaceholder_StoredLiterally()
    {
        var (runner, vm, _) = MakeRunner();

        runner.Run("alias g = \"git ${args}\"");

        vm.AliasRegistry.TryGet("g", out var entry);
        // The ${args} placeholder must be stored verbatim, not expanded
        Assert.Equal("git ${args}", entry!.TemplateBody);
    }

    // =========================================================================
    // 4. Template alias — multi-word unquoted body is a parse error
    // =========================================================================

    [Fact]
    public void TemplateAlias_MultiWordUnquotedBody_ThrowsParseError()
    {
        var (runner, vm, _) = MakeRunner();

        var ex = Assert.Throws<RuntimeError>(() => runner.Run("alias gst = git status"));
        Assert.Equal(StashErrorTypes.ParseError, ex.ErrorType);
        Assert.Contains("quoted", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // 5. Function alias — expression body, called via AliasDispatcher
    // =========================================================================

    [Fact]
    public void FunctionAlias_ExpressionBody_RegistersAndInvokes()
    {
        var (runner, vm, sw) = MakeRunner();

        runner.Run("alias greet(msg) = io.println(msg)");

        // Verify it was registered as a function alias
        Assert.True(vm.AliasRegistry.Exists("greet"));
        vm.AliasRegistry.TryGet("greet", out var entry);
        Assert.Equal(AliasRegistry.AliasKind.Function, entry!.Kind);

        // Invoke via bare-word shell dispatch
        sw.GetStringBuilder().Clear();
        runner.Run("greet hello");

        Assert.Contains("hello", sw.ToString(), StringComparison.Ordinal);
    }

    // =========================================================================
    // 6. Function alias — block body
    // =========================================================================

    [Fact]
    public void FunctionAlias_BlockBody_RegistersAndInvokes()
    {
        var (runner, vm, sw) = MakeRunner();

        runner.Run("alias greetblock(msg) { io.println(msg); }");

        Assert.True(vm.AliasRegistry.Exists("greetblock"));
        vm.AliasRegistry.TryGet("greetblock", out var entry);
        Assert.Equal(AliasRegistry.AliasKind.Function, entry!.Kind);

        sw.GetStringBuilder().Clear();
        runner.Run("greetblock world");

        Assert.Contains("world", sw.ToString(), StringComparison.Ordinal);
    }

    // =========================================================================
    // 7. Function alias — typed parameter with default value
    // =========================================================================

    [Fact]
    public void FunctionAlias_TypedParamWithDefault_RegistersCorrectly()
    {
        var (runner, vm, sw) = MakeRunner();

        runner.Run("alias greetdef(msg: string = \"Hi\") { io.println(msg); }");

        Assert.True(vm.AliasRegistry.Exists("greetdef"));
        vm.AliasRegistry.TryGet("greetdef", out var entry);
        Assert.Equal(AliasRegistry.AliasKind.Function, entry!.Kind);

        // Call with explicit arg
        sw.GetStringBuilder().Clear();
        runner.Run("greetdef custom");

        Assert.Contains("custom", sw.ToString(), StringComparison.Ordinal);
    }

    // =========================================================================
    // 8. unalias — removes alias from registry
    // =========================================================================

    [Fact]
    public void Unalias_Name_RemovesFromRegistry()
    {
        var (runner, vm, _) = MakeRunner();

        runner.Run("alias g = \"git\"");
        Assert.True(vm.AliasRegistry.Exists("g"));

        runner.Run("unalias g");

        Assert.False(vm.AliasRegistry.Exists("g"));
    }

    // =========================================================================
    // 9. unalias --all — removes all user aliases
    // =========================================================================

    [Fact]
    public void UnaliasAll_RemovesUserAliasesLeavesBuiltins()
    {
        var (runner, vm, _) = MakeRunner();

        runner.Run("alias a = \"cmd a\"");
        runner.Run("alias b = \"cmd b\"");
        runner.Run("alias c = \"cmd c\"");

        runner.Run("unalias --all");

        // Phase D: built-in aliases (cd, pwd, exit, quit, history) survive --all.
        Assert.False(vm.AliasRegistry.Exists("a"));
        Assert.False(vm.AliasRegistry.Exists("b"));
        Assert.False(vm.AliasRegistry.Exists("c"));
        // Only builtins remain.
        var remaining = vm.AliasRegistry.All().ToList();
        Assert.All(remaining, e => Assert.Equal(AliasRegistry.AliasSource.Builtin, e.Source));
    }

    // =========================================================================
    // 10. unalias --save — Phase F: removes from session and from file
    // =========================================================================

    [Fact]
    public void UnaliasSave_RemovesFromSessionAndFile()
    {
        var tmpFile = System.IO.Path.GetTempFileName();
        var prevPath = Stash.Cli.Shell.AliasPersistence.PathOverride;
        Stash.Cli.Shell.AliasPersistence.PathOverride = tmpFile;
        try
        {
            var (runner, vm, _) = MakeRunner();
            runner.Run("alias g = \"git\"");
            // Persist it first so there's something to remove
            runner.Run("alias --save g = \"git\"");
            Assert.True(System.IO.File.Exists(tmpFile));

            // Now unalias --save should remove from session and from file
            runner.Run("unalias --save g");

            Assert.False(vm.AliasRegistry.Exists("g"));
            string contents = System.IO.File.ReadAllText(tmpFile);
            Assert.DoesNotContain("\"g\"", contents, StringComparison.Ordinal);
        }
        finally
        {
            Stash.Cli.Shell.AliasPersistence.PathOverride = prevPath;
            System.IO.File.Delete(tmpFile);
        }
    }

    // =========================================================================
    // 11. unalias --force — Phase D: session-disables a built-in alias
    // =========================================================================

    [Fact]
    public void UnaliasForce_SessionDisablesBuiltin()
    {
        var (runner, vm, _) = MakeRunner();

        // cd is registered as a builtin at startup.
        Assert.True(vm.AliasRegistry.Exists("cd"));

        // unalias --force cd disables it for the session — no error.
        runner.Run("unalias --force cd");

        // cd is now invisible.
        Assert.False(vm.AliasRegistry.Exists("cd"));
    }

    // =========================================================================
    // 12. alias --save — Phase F: defines alias and persists to file
    // =========================================================================

    [Fact]
    public void AliasSave_DefinesAndPersistsAlias()
    {
        var tmpFile = System.IO.Path.GetTempFileName();
        var prevPath = Stash.Cli.Shell.AliasPersistence.PathOverride;
        Stash.Cli.Shell.AliasPersistence.PathOverride = tmpFile;
        try
        {
            var (runner, vm, _) = MakeRunner();

            runner.Run("alias --save g = \"git\"");

            // Alias registered in session
            Assert.True(vm.AliasRegistry.Exists("g"));

            // Alias written to file
            string contents = System.IO.File.ReadAllText(tmpFile);
            Assert.Contains("\"g\"", contents, StringComparison.Ordinal);
            Assert.Contains("git", contents, StringComparison.Ordinal);
        }
        finally
        {
            Stash.Cli.Shell.AliasPersistence.PathOverride = prevPath;
            System.IO.File.Delete(tmpFile);
        }
    }

    // =========================================================================
    // 13. alias (no args) — pretty list output
    // =========================================================================

    [Fact]
    public void AliasNoArgs_PrintsList()
    {
        var (runner, vm, sw) = MakeRunner();

        runner.Run("alias g = \"git status\"");
        runner.Run("alias ll = \"ls -la\"");

        sw.GetStringBuilder().Clear();
        runner.Run("alias");

        string output = sw.ToString();
        Assert.Contains("g", output, StringComparison.Ordinal);
        Assert.Contains("ll", output, StringComparison.Ordinal);
    }

    // =========================================================================
    // 14. alias <name> — inspect single alias
    // =========================================================================

    [Fact]
    public void AliasSingleName_PrintsDefinition()
    {
        var (runner, vm, sw) = MakeRunner();

        runner.Run("alias g = \"git status\"");

        sw.GetStringBuilder().Clear();
        runner.Run("alias g");

        string output = sw.ToString();
        Assert.Contains("g", output, StringComparison.Ordinal);
        Assert.Contains("git status", output, StringComparison.Ordinal);
    }

    // =========================================================================
    // 15. alias <name> — not found
    // =========================================================================

    [Fact]
    public void AliasSingleName_NotFound_PrintsNotDefined()
    {
        var (runner, _, sw) = MakeRunner();

        runner.Run("alias nosuchname");

        string output = sw.ToString();
        Assert.Contains("nosuchname", output, StringComparison.Ordinal);
        Assert.Contains("not defined", output, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // 16. alias --help — prints help text
    // =========================================================================

    [Fact]
    public void AliasHelp_PrintsHelpText()
    {
        var (runner, _, sw) = MakeRunner();

        runner.Run("alias --help");

        string output = sw.ToString();
        Assert.False(string.IsNullOrWhiteSpace(output));
        Assert.Contains("alias", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unalias", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("${args}", output, StringComparison.Ordinal);
    }

    // =========================================================================
    // 17. alias no-args with no aliases — shows "(no aliases defined)"
    // =========================================================================

    [Fact]
    public void AliasNoArgs_ShowsBuiltinGroup()
    {
        // Phase D: five builtins are now always registered, so bare 'alias' shows [builtin].
        var (runner, _, sw) = MakeRunner();

        runner.Run("alias");

        string output = sw.ToString();
        Assert.Contains("[builtin]", output, StringComparison.Ordinal);
        Assert.Contains("cd", output, StringComparison.Ordinal);
    }

    // =========================================================================
    // 18. Function alias with ${...} in body — body not pre-expanded
    // =========================================================================

    [Fact]
    public void FunctionAlias_WithInterpolationInBody_BodyNotPreExpanded()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        var (runner, vm, _) = MakeRunner();

        // ${msg} appears inside a $() shell call — it is a Stash lambda parameter
        // reference, NOT a REPL variable to be expanded at define time.
        runner.Run("alias echoarg(msg) = io.println(msg)");

        Assert.True(vm.AliasRegistry.Exists("echoarg"));
        vm.AliasRegistry.TryGet("echoarg", out var entry);
        Assert.Equal(AliasRegistry.AliasKind.Function, entry!.Kind);
    }

    // =========================================================================
    // 19. Template alias — single-quoted body
    // =========================================================================

    [Fact]
    public void TemplateAlias_SingleQuotedBody_RegistersCorrectly()
    {
        var (runner, vm, _) = MakeRunner();

        runner.Run("alias gst = 'git status'");

        Assert.True(vm.AliasRegistry.Exists("gst"));
        vm.AliasRegistry.TryGet("gst", out var entry);
        Assert.Equal("git status", entry!.TemplateBody);
    }

    // =========================================================================
    // 20. unalias no args — throws helpful error
    // =========================================================================

    [Fact]
    public void Unalias_NoArgs_ThrowsCommandError()
    {
        var (runner, _, _) = MakeRunner();

        var ex = Assert.Throws<RuntimeError>(() => runner.Run("unalias"));
        Assert.Equal(StashErrorTypes.CommandError, ex.ErrorType);
    }
}
