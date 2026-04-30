using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Stash.Bytecode;
using Stash.Cli.Completion;
using Stash.Cli.Shell;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib;
using Stash.Stdlib.BuiltIns;

namespace Stash.Tests.Cli.Completion;

/// <summary>
/// End-to-end integration tests for the completion pipeline (spec §15.10).
/// Builds a real <see cref="CompletionEngine"/> and verifies round-trip behaviour.
/// </summary>
public class CompletionIntegrationTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (CompletionEngine engine, VirtualMachine vm, CustomCompleterRegistry registry)
        MakeFullSetup(Func<string, bool>? isExec = null, TextWriter? errorOutput = null)
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
        var engine = new CompletionEngine(vm, cache, registry, classifier, errorOutput ?? TextWriter.Null);
        return (engine, vm, registry);
    }

    // ── Core engine routing ───────────────────────────────────────────────────

    [Fact]
    public void Complete_FsDot_StashMode_AllFsMembersReturned()
    {
        var (engine, _, _) = MakeFullSetup();
        var result = engine.Complete("fs.", 3);

        Assert.True(result.Candidates.Count > 0);
        Assert.Equal(3, result.ReplaceStart);  // only suffix replaced
        Assert.All(result.Candidates, c => Assert.DoesNotContain(".", c.Insert));
    }

    [Fact]
    public void Complete_MathDot_IncludesPIConstant()
    {
        var (engine, _, _) = MakeFullSetup();
        var result = engine.Complete("math.", 5);

        Assert.Contains(result.Candidates, c => c.Insert == "PI");
    }

    [Fact]
    public void Complete_ArrDotPu_FilteredToArrPush()
    {
        var (engine, _, _) = MakeFullSetup();
        var result = engine.Complete("arr.pu", 6);

        Assert.Contains(result.Candidates, c => c.Insert == "push");
        Assert.All(result.Candidates, c =>
            Assert.StartsWith("pu", c.Insert, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Complete_Print_StashMode_PrefixFilter()
    {
        var (engine, _, _) = MakeFullSetup();
        var result = engine.Complete("print", 5);

        Assert.Contains(result.Candidates, c => c.Insert == "print");
        Assert.Contains(result.Candidates, c => c.Insert == "println");
        Assert.All(result.Candidates, c =>
            Assert.StartsWith("print", c.Insert, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Complete_GlobToken_ReturnsEmpty()
    {
        var (engine, _, _) = MakeFullSetup();
        var result = engine.Complete("*.stash", 7);

        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void Complete_BraceToken_ReturnsEmpty()
    {
        var (engine, _, _) = MakeFullSetup();
        var result = engine.Complete("file.{txt", 9);

        Assert.Empty(result.Candidates);
    }

    // ── Throwing custom completer — logs once and falls back ─────────────────

    [Fact]
    public void ThrowingCompleter_LogsOnceAndFallsBackToPathCompleter()
    {
        var stderrSw = new StringWriter();
        var (engine, vm, registry) = MakeFullSetup(isExec: name => name == "foo", errorOutput: stderrSw);

        // Register a throwing completer
        ShellRunner.EvaluateSource(
            """let _bad = (ctx) => { throw "kaboom"; };""", vm);
        var callable = vm.Globals["_bad"].ToObject() as IStashCallable;
        Assert.NotNull(callable);
        registry.Register("foo", callable!);

        // First Tab on "foo bar" — should log once and fall back to PathCompleter
        var result1 = engine.Complete("foo bar", 7);
        string stderr1 = stderrSw.ToString();
        Assert.Contains("kaboom", stderr1);

        stderrSw.GetStringBuilder().Clear();

        // Second Tab — same completer, should NOT re-log (idempotent)
        var result2 = engine.Complete("foo bar", 7);
        string stderr2 = stderrSw.ToString();
        Assert.Equal(string.Empty, stderr2);
    }

    // ── CompletionWiring round-trip via complete.suggest ─────────────────────

    [Fact]
    public void Wire_SuggestHandler_ReturnsFsMembers()
    {
        CompleteBuiltIns.ResetAllForTesting();

        var (engine, vm, registry) = MakeFullSetup();
        CompletionWiring.Wire(engine, registry, vm);

        try
        {
            Assert.NotNull(CompleteBuiltIns.SuggestHandler);
            var inst = CompleteBuiltIns.SuggestHandler!("fs.", "fs.".Length);
            Assert.Equal("CompletionResult", inst.TypeName);

            var candidates = inst.GetField("candidates", null).ToObject() as List<StashValue>;
            Assert.NotNull(candidates);
            Assert.True(candidates!.Count > 0);
        }
        finally
        {
            CompleteBuiltIns.ResetAllForTesting();
        }
    }

    [Fact]
    public void Wire_BuildCompletionResultStruct_FieldsMatchSpec()
    {
        var candidates = new List<Candidate>
        {
            new("foo", "foo", CandidateKind.StashFunction),
            new("foobar", "foobar", CandidateKind.StashFunction),
        };
        var result = new CompletionResult(2, 5, candidates, "foo");
        var inst = CompletionWiring.BuildCompletionResultStruct(result);

        Assert.Equal("CompletionResult", inst.TypeName);
        Assert.Equal(2L, inst.GetField("replace_start", null).AsInt);
        Assert.Equal(5L, inst.GetField("replace_end", null).AsInt);
        Assert.Equal("foo", inst.GetField("common_prefix", null).ToObject());

        var cands = inst.GetField("candidates", null).ToObject() as List<StashValue>;
        Assert.NotNull(cands);
        Assert.Equal(2, cands!.Count);
        Assert.Equal("foo", cands[0].ToObject());
        Assert.Equal("foobar", cands[1].ToObject());
    }
}
