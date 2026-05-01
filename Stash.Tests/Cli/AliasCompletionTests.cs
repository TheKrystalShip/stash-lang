using System;
using System.Collections.Generic;
using System.IO;
using Stash.Bytecode;
using Stash.Cli.Completion;
using Stash.Cli.Shell;
using Stash.Runtime;
using Stash.Stdlib;

namespace Stash.Tests.Cli;

/// <summary>
/// Integration tests for Phase G alias tab completion (spec §11.1–§11.3).
/// Covers first-word completion, argument delegation for template aliases,
/// and alias/unalias sugar position completions.
/// </summary>
[Collection("AliasStaticState")]
public sealed class AliasCompletionTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (CompletionEngine Engine, VirtualMachine Vm, CustomCompleterRegistry Registry)
        MakeSetup(Func<string, bool>? isExec = null)
    {
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        vm.Output = TextWriter.Null;
        vm.ErrorOutput = TextWriter.Null;
        vm.EmbeddedMode = true;

        var cache = new PathExecutableCache(isExec ?? (_ => false));
        var registry = new CustomCompleterRegistry();

        var shellCtx = new ShellContext
        {
            Vm = vm,
            PathCache = cache,
            Keywords = ShellContext.BuildKeywordSet(),
            Namespaces = new HashSet<string>(StdlibRegistry.NamespaceNames, StringComparer.Ordinal),
            ShellBuiltinNames = ShellContext.BuildShellBuiltinSet(),
        };
        var classifier = new ShellLineClassifier(shellCtx);
        var engine = new CompletionEngine(vm, cache, registry, classifier, TextWriter.Null);
        return (engine, vm, registry);
    }

    /// <summary>
    /// Registers a template alias directly in the VM registry without going through the shell runner.
    /// </summary>
    private static void DefineTemplateAlias(VirtualMachine vm, string name, string body)
    {
        vm.AliasRegistry.Define(new AliasRegistry.AliasEntry
        {
            Name = name,
            Kind = AliasRegistry.AliasKind.Template,
            TemplateBody = body,
            Source = AliasRegistry.AliasSource.Repl,
        });
    }

    /// <summary>
    /// Registers a function alias directly in the VM registry.
    /// </summary>
    private static void DefineFunctionAlias(VirtualMachine vm, string name)
    {
        var body = new BuiltInFunction(name, arity: 0,
            (_, _) => StashValue.Null);
        vm.AliasRegistry.Define(new AliasRegistry.AliasEntry
        {
            Name = name,
            Kind = AliasRegistry.AliasKind.Function,
            FunctionBody = body,
            Source = AliasRegistry.AliasSource.Repl,
        });
    }

    /// <summary>
    /// Registers a built-in alias (cd/pwd style) directly.
    /// </summary>
    private static void DefineBuiltinAlias(VirtualMachine vm, string name, string description)
    {
        var body = new BuiltInFunction(name, arity: 0,
            (_, _) => StashValue.Null);
        vm.AliasRegistry.Define(new AliasRegistry.AliasEntry
        {
            Name = name,
            Kind = AliasRegistry.AliasKind.Function,
            FunctionBody = body,
            Description = description,
            Source = AliasRegistry.AliasSource.Builtin,
        });
    }

    // =========================================================================
    // §11.1 — alias names appear in first-word completion
    // =========================================================================

    [Fact]
    public void FirstWord_AliasNamesIncluded_InCommandPosition()
    {
        var (engine, vm, _) = MakeSetup(isExec: _ => false);
        DefineTemplateAlias(vm, "g", "git ${args}");
        DefineTemplateAlias(vm, "gst", "git status");
        DefineTemplateAlias(vm, "gco", "git checkout ${args[0]}");

        // Complete from the start of the line — command position
        var result = engine.Complete("g", 1);

        Assert.Contains(result.Candidates, c => c.Insert == "g");
        Assert.Contains(result.Candidates, c => c.Insert == "gst");
        Assert.Contains(result.Candidates, c => c.Insert == "gco");
    }

    [Fact]
    public void FirstWord_AllDefinedAliases_AppearsInEmptyCompletion()
    {
        var (engine, vm, _) = MakeSetup(isExec: _ => false);
        DefineTemplateAlias(vm, "ll", "ls -la");
        DefineTemplateAlias(vm, "la", "ls -la");

        // Completing "ll" — classifier sees alias "ll" → Shell → CommandCompleter
        var result = engine.Complete("ll", 2);
        Assert.Contains(result.Candidates, c => c.Insert == "ll");

        // Completing "la" — classifier sees alias "la" → Shell → CommandCompleter
        var result2 = engine.Complete("la", 2);
        Assert.Contains(result2.Candidates, c => c.Insert == "la");
    }

    // =========================================================================
    // §11.1 — built-in aliases appear in first-word completion
    // =========================================================================

    [Fact]
    public void FirstWord_BuiltinAliasesIncluded()
    {
        var (engine, vm, _) = MakeSetup(isExec: _ => false);
        // Register built-in-style aliases (as BuiltinAliases.RegisterBuiltins would do)
        DefineBuiltinAlias(vm, "cd",      "change directory");
        DefineBuiltinAlias(vm, "pwd",     "print working directory");
        DefineBuiltinAlias(vm, "exit",    "exit the shell");

        // Completing "cd" — classifier sees alias "cd" → Shell → CommandCompleter
        var result = engine.Complete("cd", 2);
        Assert.Contains(result.Candidates, c => c.Insert == "cd");

        // Completing "pwd" — classifier sees alias "pwd" → Shell → CommandCompleter
        var result2 = engine.Complete("pwd", 3);
        Assert.Contains(result2.Candidates, c => c.Insert == "pwd");
    }

    [Fact]
    public void FirstWord_AfterForceDisable_AliasExcluded()
    {
        var (engine, vm, _) = MakeSetup(isExec: _ => false);
        DefineTemplateAlias(vm, "myalias", "echo hello");

        // Before disable — should appear
        var before = engine.Complete("myalias", 7);
        Assert.Contains(before.Candidates, c => c.Insert == "myalias");

        // Force-disable the alias
        vm.AliasRegistry.ForceDisable("myalias");

        // After disable — should NOT appear
        var after = engine.Complete("myalias", 7);
        Assert.DoesNotContain(after.Candidates, c => c.Insert == "myalias");
    }

    // =========================================================================
    // §11.3 — `alias <TAB>` and `unalias <TAB>` complete alias names
    // =========================================================================

    [Fact]
    public void AliasSugar_AliasSpaceTab_ReturnsAliasNames()
    {
        var (engine, vm, _) = MakeSetup();
        DefineTemplateAlias(vm, "g",   "git ${args}");
        DefineTemplateAlias(vm, "gst", "git status");

        // "alias " with cursor at position 6 (after the space, second-word position)
        var result = engine.Complete("alias ", 6);

        Assert.Contains(result.Candidates, c => c.Insert == "g");
        Assert.Contains(result.Candidates, c => c.Insert == "gst");
    }

    [Fact]
    public void AliasSugar_AliasGPrefix_FiltersToGAliases()
    {
        var (engine, vm, _) = MakeSetup();
        DefineTemplateAlias(vm, "g",   "git ${args}");
        DefineTemplateAlias(vm, "gst", "git status");
        DefineTemplateAlias(vm, "ll",  "ls -la");

        // "alias g" with cursor at 7 — prefix "g" should filter
        var result = engine.Complete("alias g", 7);

        Assert.Contains(result.Candidates, c => c.Insert == "g");
        Assert.Contains(result.Candidates, c => c.Insert == "gst");
        Assert.DoesNotContain(result.Candidates, c => c.Insert == "ll");
    }

    [Fact]
    public void AliasSugar_UnaliasSpaceTab_ReturnsAliasNames()
    {
        var (engine, vm, _) = MakeSetup();
        DefineTemplateAlias(vm, "g",   "git ${args}");
        DefineTemplateAlias(vm, "gst", "git status");

        // "unalias " with cursor at position 8
        var result = engine.Complete("unalias ", 8);

        Assert.Contains(result.Candidates, c => c.Insert == "g");
        Assert.Contains(result.Candidates, c => c.Insert == "gst");
    }

    [Fact]
    public void AliasSugar_AliasNameEqualsTab_DoesNotReturnAliasNames()
    {
        var (engine, vm, _) = MakeSetup(isExec: name => name == "git");
        DefineTemplateAlias(vm, "g", "git ${args}");

        // "alias g = " — cursor is after the "= " (body position)
        // Only PATH executables should be returned; alias-kind items must NOT appear
        var result = engine.Complete("alias g = ", 10);

        // "git" is a PATH executable — should appear
        Assert.Contains(result.Candidates, c => c.Insert == "git");
        // No candidates with Kind == Alias (alias names excluded from body completion)
        Assert.DoesNotContain(result.Candidates, c => c.Kind == CandidateKind.Alias);
    }

    // =========================================================================
    // §11.2 — template alias argument completion delegates to underlying command
    // =========================================================================

    [Fact]
    public void TemplateAlias_ArgumentCompletion_DelegatesToUnderlying()
    {
        var (engine, vm, registry) = MakeSetup(isExec: name => name == "git");
        DefineTemplateAlias(vm, "g", "git ${args}");

        // Register a custom completer for "git" that returns known candidates
        var gitCompleter = new BuiltInFunction("_git_completer", arity: 0,
            (_, _) =>
            {
                var list = new System.Collections.Generic.List<StashValue>
                {
                    StashValue.FromObj("push"),
                    StashValue.FromObj("pull"),
                    StashValue.FromObj("commit"),
                };
                return StashValue.FromObj(list);
            });
        registry.Register("git", gitCompleter);

        // "g " with cursor at position 2 — argument position for alias "g"
        var result = engine.Complete("g ", 2);

        // Custom completer for "git" should have been invoked
        Assert.Contains(result.Candidates, c => c.Insert == "push");
        Assert.Contains(result.Candidates, c => c.Insert == "pull");
        Assert.Contains(result.Candidates, c => c.Insert == "commit");
    }

    [Fact]
    public void TemplateAlias_ArgumentCompletion_PlaceholderOnlyBody_FallsBackToPath()
    {
        var (engine, vm, registry) = MakeSetup(isExec: _ => false);
        // Body starts with a placeholder — no underlying command to extract
        DefineTemplateAlias(vm, "myrun", "${args}");

        // "myrun " — no underlying command, fall back to path completer (no crash)
        var result = engine.Complete("myrun ", 6);

        // Just assert no exception; path completer returns file candidates (may be empty)
        Assert.NotNull(result);
    }

    [Fact]
    public void FunctionAlias_NoCustomCompleter_FallsBackToPathCompletion()
    {
        var (engine, vm, _) = MakeSetup(isExec: _ => false);
        DefineFunctionAlias(vm, "deploy");

        // "deploy " — function alias, no custom completer registered → path fallback
        var result = engine.Complete("deploy ", 7);

        // Should not throw; path completer runs
        Assert.NotNull(result);
    }

    [Fact]
    public void TemplateAlias_ArgumentCompletion_PrefixFiltered()
    {
        var (engine, vm, registry) = MakeSetup(isExec: name => name == "git");
        DefineTemplateAlias(vm, "g", "git ${args}");

        var gitCompleter = new BuiltInFunction("_git_completer", arity: 0,
            (_, _) =>
            {
                var list = new System.Collections.Generic.List<StashValue>
                {
                    StashValue.FromObj("push"),
                    StashValue.FromObj("pull"),
                    StashValue.FromObj("commit"),
                };
                return StashValue.FromObj(list);
            });
        registry.Register("git", gitCompleter);

        // "g pu" — prefix "pu" should filter to "push" and "pull"
        var result = engine.Complete("g pu", 4);

        Assert.Contains(result.Candidates, c => c.Insert == "push");
        Assert.Contains(result.Candidates, c => c.Insert == "pull");
        Assert.DoesNotContain(result.Candidates, c => c.Insert == "commit");
    }
}
