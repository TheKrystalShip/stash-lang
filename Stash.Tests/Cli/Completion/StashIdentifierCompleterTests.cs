using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Stash.Cli.Completion;
using Stash.Cli.Completion.Completers;
using Stash.Cli.Shell;
using Stash.Stdlib;

namespace Stash.Tests.Cli.Completion;

/// <summary>
/// Unit tests for <see cref="StashIdentifierCompleter"/> covering spec §15.5.
/// </summary>
public class StashIdentifierCompleterTests
{
    private static Stash.Bytecode.VirtualMachine MakeVm(string? source = null)
    {
        var vm = new Stash.Bytecode.VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        vm.Output = TextWriter.Null;
        vm.ErrorOutput = TextWriter.Null;
        vm.EmbeddedMode = true;
        if (source != null)
            ShellRunner.EvaluateSource(source, vm);
        return vm;
    }

    private static CompletionDeps MakeDeps(Stash.Bytecode.VirtualMachine? vm = null) =>
        new(vm ?? MakeVm(), new PathExecutableCache(), new CustomCompleterRegistry());

    private static CursorContext MakeCtx(string token, CompletionMode mode = CompletionMode.Stash) =>
        new(mode, 0, token.Length, token, false, '\0', false, Array.Empty<string>());

    // ── Keywords ──────────────────────────────────────────────────────────────

    [Fact]
    public void Complete_TokenFor_IncludesForKeyword()
    {
        var completer = new StashIdentifierCompleter();
        IReadOnlyList<Candidate> result = completer.Complete(MakeCtx("fo"), MakeDeps());

        Assert.Contains(result, c => c.Insert == "for" && c.Kind == CandidateKind.StashKeyword);
    }

    [Fact]
    public void Complete_TokenFn_IncludesFnKeyword()
    {
        var completer = new StashIdentifierCompleter();
        IReadOnlyList<Candidate> result = completer.Complete(MakeCtx("fn"), MakeDeps());

        Assert.Contains(result, c => c.Insert == "fn" && c.Kind == CandidateKind.StashKeyword);
    }

    // ── Built-in functions ────────────────────────────────────────────────────

    [Fact]
    public void Complete_TokenPri_IncludesPrintAndPrintln()
    {
        var completer = new StashIdentifierCompleter();
        IReadOnlyList<Candidate> result = completer.Complete(MakeCtx("pri"), MakeDeps());

        Assert.Contains(result, c => c.Insert == "print" && c.Kind == CandidateKind.StashFunction);
        Assert.Contains(result, c => c.Insert == "println" && c.Kind == CandidateKind.StashFunction);
    }

    // ── Namespace names ───────────────────────────────────────────────────────

    [Fact]
    public void Complete_TokenFs_IncludesFsNamespace()
    {
        var completer = new StashIdentifierCompleter();
        IReadOnlyList<Candidate> result = completer.Complete(MakeCtx("f"), MakeDeps());

        Assert.Contains(result, c => c.Insert == "fs" && c.Kind == CandidateKind.StashNamespace);
    }

    [Fact]
    public void Complete_TokenF_IncludesForAndFsAndFalse()
    {
        var completer = new StashIdentifierCompleter();
        IReadOnlyList<Candidate> result = completer.Complete(MakeCtx("f"), MakeDeps());

        var inserts = result.Select(c => c.Insert).ToArray();
        Assert.Contains("for", inserts);
        Assert.Contains("fs", inserts);
        Assert.Contains("false", inserts);
        Assert.Contains("fn", inserts);
    }

    // ── REPL globals ──────────────────────────────────────────────────────────

    [Fact]
    public void Complete_UserGlobal_IsIncluded()
    {
        var vm = MakeVm("let myVar = 42;");
        var completer = new StashIdentifierCompleter();
        IReadOnlyList<Candidate> result = completer.Complete(MakeCtx("my"), MakeDeps(vm));

        Assert.Contains(result, c => c.Insert == "myVar" && c.Kind == CandidateKind.StashGlobal);
    }

    [Fact]
    public void Complete_NonCallableGlobal_IsIncluded()
    {
        // StashIdentifierCompleter includes ALL globals (callable or not)
        var vm = MakeVm("let bar = 5;");
        var completer = new StashIdentifierCompleter();
        IReadOnlyList<Candidate> result = completer.Complete(MakeCtx("ba"), MakeDeps(vm));

        Assert.Contains(result, c => c.Insert == "bar" && c.Kind == CandidateKind.StashGlobal);
    }

    // ── Empty token ───────────────────────────────────────────────────────────

    [Fact]
    public void Complete_EmptyToken_ReturnsAllSources()
    {
        var completer = new StashIdentifierCompleter();
        IReadOnlyList<Candidate> result = completer.Complete(MakeCtx(""), MakeDeps());

        Assert.NotEmpty(result);
        // Should contain at least one from each source
        Assert.Contains(result, c => c.Kind == CandidateKind.StashKeyword);
        Assert.Contains(result, c => c.Kind == CandidateKind.StashFunction);
        Assert.Contains(result, c => c.Kind == CandidateKind.StashNamespace);
    }

    // ── Deduplication ────────────────────────────────────────────────────────

    [Fact]
    public void Complete_NoDuplicateInserts()
    {
        var completer = new StashIdentifierCompleter();
        IReadOnlyList<Candidate> result = completer.Complete(MakeCtx(""), MakeDeps());

        var inserts = result.Select(c => c.Insert).ToList();
        var distinct = inserts.Distinct(StringComparer.Ordinal).ToList();
        Assert.Equal(distinct.Count, inserts.Count);
    }

    // ── Substitution mode ─────────────────────────────────────────────────────

    [Fact]
    public void Complete_SubstitutionMode_WorksSameAsStashMode()
    {
        var completer = new StashIdentifierCompleter();
        IReadOnlyList<Candidate> stash = completer.Complete(MakeCtx("pr", CompletionMode.Stash), MakeDeps());
        IReadOnlyList<Candidate> sub = completer.Complete(MakeCtx("pr", CompletionMode.Substitution), MakeDeps());

        // Both should produce the same candidates (engine picks mode; completer just produces)
        var stashInserts = stash.Select(c => c.Insert).OrderBy(s => s).ToArray();
        var subInserts = sub.Select(c => c.Insert).OrderBy(s => s).ToArray();
        Assert.Equal(stashInserts, subInserts);
    }
}
