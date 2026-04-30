using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Stash.Cli.Completion;
using Stash.Cli.Completion.Completers;
using Stash.Cli.Shell;

namespace Stash.Tests.Cli.Completion;

/// <summary>
/// Unit tests for <see cref="CommandCompleter"/> covering spec §15.4.
/// </summary>
public class CommandCompleterTests
{
    private static CompletionDeps MakeDeps()
    {
        var realVm = MakeVm();
        var cache = new PathExecutableCache();
        var registry = new CustomCompleterRegistry();
        return new CompletionDeps(realVm, cache, registry);
    }

    private static Stash.Bytecode.VirtualMachine MakeVm(string? source = null)
    {
        var vm = new Stash.Bytecode.VirtualMachine(Stash.Stdlib.StdlibDefinitions.CreateVMGlobals());
        vm.Output = TextWriter.Null;
        vm.ErrorOutput = TextWriter.Null;
        vm.EmbeddedMode = true;
        if (source != null)
            ShellRunner.EvaluateSource(source, vm);
        return vm;
    }

    private static CursorContext MakeCtx(string token) =>
        new(CompletionMode.Shell, 0, token.Length, token, false, '\0', false, Array.Empty<string>());

    private static CompletionDeps MakeDepsWithVm(Stash.Bytecode.VirtualMachine vm) =>
        new(vm, new PathExecutableCache(), new CustomCompleterRegistry());

    // ── Sugar names ──────────────────────────────────────────────────────────

    [Fact]
    public void Complete_TokenC_IncludesCd()
    {
        var deps = MakeDeps();
        var completer = new CommandCompleter();

        IReadOnlyList<Candidate> result = completer.Complete(MakeCtx("c"), deps);

        Assert.Contains(result, c => c.Insert == "cd" && c.Kind == CandidateKind.Sugar);
    }

    [Fact]
    public void Complete_EmptyToken_IncludesAllSugarNames()
    {
        var deps = MakeDeps();
        var completer = new CommandCompleter();

        IReadOnlyList<Candidate> result = completer.Complete(MakeCtx(""), deps);

        foreach (string sugar in new[] { "cd", "pwd", "exit", "quit" })
            Assert.Contains(result, c => c.Insert == sugar && c.Kind == CandidateKind.Sugar);
    }

    // ── PATH executables ─────────────────────────────────────────────────────

    [Fact]
    public void Complete_TokenGit_IncludesGitIfInstalled()
    {
        if (OperatingSystem.IsWindows()) return;

        var deps = MakeDeps();
        var completer = new CommandCompleter();
        IReadOnlyList<Candidate> result = completer.Complete(MakeCtx("git"), deps);

        // git is almost certainly installed in CI; if not, just verify no exception
        if (result.Any(c => c.Insert == "git"))
            Assert.Contains(result, c => c.Insert == "git" && c.Kind == CandidateKind.Executable);
    }

    // ── REPL globals ─────────────────────────────────────────────────────────

    [Fact]
    public void Complete_CallableGlobal_IsIncluded()
    {
        var vm = MakeVm("let foo = () => { return 42; };");
        var deps = MakeDepsWithVm(vm);
        var completer = new CommandCompleter();

        IReadOnlyList<Candidate> result = completer.Complete(MakeCtx("fo"), deps);

        Assert.Contains(result, c => c.Insert == "foo" && c.Kind == CandidateKind.StashGlobal);
    }

    [Fact]
    public void Complete_NonCallableGlobal_IsNotIncluded()
    {
        var vm = MakeVm("let bar = 5;");
        var deps = MakeDepsWithVm(vm);
        var completer = new CommandCompleter();

        IReadOnlyList<Candidate> result = completer.Complete(MakeCtx("ba"), deps);

        Assert.DoesNotContain(result, c => c.Insert == "bar");
    }

    // ── Backslash prefix ─────────────────────────────────────────────────────

    [Fact]
    public void Complete_BackslashPrefix_IsStrippedForMatchingAndReprependedInInsert()
    {
        if (OperatingSystem.IsWindows()) return;

        // Use a sugar name since "cd" is always there
        var deps = MakeDeps();
        var completer = new CommandCompleter();

        IReadOnlyList<Candidate> result = completer.Complete(MakeCtx("\\c"), deps);

        // Should contain \cd (sugar with backslash prepended)
        Assert.Contains(result, c => c.Insert == "\\cd" && c.Kind == CandidateKind.Sugar);
    }

    [Fact]
    public void Complete_BangPrefix_IsStrippedForMatchingAndReprependedInInsert()
    {
        var deps = MakeDeps();
        var completer = new CommandCompleter();

        IReadOnlyList<Candidate> result = completer.Complete(MakeCtx("!c"), deps);

        // Should contain !cd (sugar with ! prepended)
        Assert.Contains(result, c => c.Insert == "!cd" && c.Kind == CandidateKind.Sugar);
    }

    // ── Deduplication ────────────────────────────────────────────────────────

    [Fact]
    public void Complete_NoDuplicateInserts()
    {
        var deps = MakeDeps();
        var completer = new CommandCompleter();

        IReadOnlyList<Candidate> result = completer.Complete(MakeCtx(""), deps);

        var inserts = result.Select(c => c.Insert).ToList();
        var distinct = inserts.Distinct(StringComparer.Ordinal).ToList();
        Assert.Equal(distinct.Count, inserts.Count);
    }

    // ── Helper ───────────────────────────────────────────────────────────────
}
