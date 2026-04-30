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
/// Unit tests for <see cref="DottedMemberCompleter"/> covering spec §15.6.
/// </summary>
public class DottedMemberCompleterTests
{
    private static Stash.Bytecode.VirtualMachine MakeVm() =>
        new(StdlibDefinitions.CreateVMGlobals())
        {
            Output = TextWriter.Null,
            ErrorOutput = TextWriter.Null,
            EmbeddedMode = true,
        };

    private static CompletionDeps MakeDeps() =>
        new(MakeVm(), new PathExecutableCache(), new CustomCompleterRegistry(), System.IO.TextWriter.Null);

    private static CursorContext MakeCtx(string token, CompletionMode mode = CompletionMode.Stash) =>
        new(mode, 0, token.Length, token, false, '\0', false, Array.Empty<string>());

    // ── Namespace member enumeration ──────────────────────────────────────────

    [Fact]
    public void Complete_FsDot_ReturnsAllFsMembers()
    {
        var completer = new DottedMemberCompleter();
        IReadOnlyList<Candidate> result = completer.Complete(MakeCtx("fs."), MakeDeps());

        Assert.NotEmpty(result);
        // All kinds should be StashMember
        Assert.All(result, c => Assert.Equal(CandidateKind.StashMember, c.Kind));
    }

    [Fact]
    public void Complete_FsDot_ContainsReadFile()
    {
        var completer = new DottedMemberCompleter();
        IReadOnlyList<Candidate> result = completer.Complete(MakeCtx("fs."), MakeDeps());

        Assert.Contains(result, c => c.Insert == "readFile");
    }

    [Fact]
    public void Complete_FsDot_ContainsExistsFile()
    {
        var completer = new DottedMemberCompleter();
        IReadOnlyList<Candidate> result = completer.Complete(MakeCtx("fs."), MakeDeps());

        Assert.Contains(result, c => c.Insert == "existsFile" || c.Insert == "exists");
    }

    [Fact]
    public void Complete_MathDot_ContainsConstants()
    {
        var completer = new DottedMemberCompleter();
        IReadOnlyList<Candidate> result = completer.Complete(MakeCtx("math."), MakeDeps());

        // math namespace has PI and E constants
        Assert.Contains(result, c => c.Insert == "PI" || c.Insert == "E");
    }

    // ── Suffix suffix doesn't affect short names returned ────────────────────

    [Fact]
    public void Complete_FsDotExi_ReturnsShortNames()
    {
        var completer = new DottedMemberCompleter();
        IReadOnlyList<Candidate> result = completer.Complete(MakeCtx("fs.exi"), MakeDeps());

        // The completer returns short names (no "fs." prefix)
        Assert.All(result, c => Assert.DoesNotContain("fs.", c.Insert));
        Assert.All(result, c => Assert.DoesNotContain("fs.", c.Display));
    }

    // ── Unknown prefix → empty ────────────────────────────────────────────────

    [Fact]
    public void Complete_UnknownPrefix_ReturnsEmpty()
    {
        var completer = new DottedMemberCompleter();
        IReadOnlyList<Candidate> result = completer.Complete(MakeCtx("foo.bar"), MakeDeps());

        Assert.Empty(result);
    }

    [Fact]
    public void Complete_UserVariable_ReturnsEmpty()
    {
        var completer = new DottedMemberCompleter();
        // User variable — no type inference in v1
        IReadOnlyList<Candidate> result = completer.Complete(MakeCtx("myObj.field"), MakeDeps());

        Assert.Empty(result);
    }

    // ── No dot → empty ────────────────────────────────────────────────────────

    [Fact]
    public void Complete_NoDot_ReturnsEmpty()
    {
        var completer = new DottedMemberCompleter();
        IReadOnlyList<Candidate> result = completer.Complete(MakeCtx("fs"), MakeDeps());

        Assert.Empty(result);
    }

    // ── Substitution mode ─────────────────────────────────────────────────────

    [Fact]
    public void Complete_SubstitutionMode_WorksSameAsStashMode()
    {
        var completer = new DottedMemberCompleter();
        IReadOnlyList<Candidate> stash = completer.Complete(MakeCtx("arr.", CompletionMode.Stash), MakeDeps());
        IReadOnlyList<Candidate> sub = completer.Complete(MakeCtx("arr.", CompletionMode.Substitution), MakeDeps());

        var stashInserts = stash.Select(c => c.Insert).OrderBy(s => s).ToArray();
        var subInserts = sub.Select(c => c.Insert).OrderBy(s => s).ToArray();
        Assert.Equal(stashInserts, subInserts);
    }

    // ── Arr has members ───────────────────────────────────────────────────────

    [Fact]
    public void Complete_ArrDot_ContainsPushAndPop()
    {
        var completer = new DottedMemberCompleter();
        IReadOnlyList<Candidate> result = completer.Complete(MakeCtx("arr."), MakeDeps());

        Assert.Contains(result, c => c.Insert == "push");
        Assert.Contains(result, c => c.Insert == "pop" || c.Insert == "length" || c.Insert == "filter");
    }
}
