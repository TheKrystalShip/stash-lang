using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Stash.Cli.Completion;
using Stash.Cli.Completion.Completers;
using Stash.Cli.Shell;

namespace Stash.Tests.Cli.Completion;

/// <summary>
/// Unit tests for <see cref="PathCompleter"/> covering spec §15.3.
/// Uses a real temporary directory for isolation, cleaned up via IDisposable.
/// </summary>
public sealed class PathCompleterTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly PathCompleter _completer = new();
    private readonly CompletionDeps _deps;

    public PathCompleterTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tmpDir);

        // Create test entries
        File.WriteAllText(Path.Combine(_tmpDir, "Alpha.txt"), string.Empty);
        File.WriteAllText(Path.Combine(_tmpDir, "beta.md"), string.Empty);
        File.WriteAllText(Path.Combine(_tmpDir, ".hidden"), string.Empty);
        Directory.CreateDirectory(Path.Combine(_tmpDir, "subdir"));

        var vm = new Stash.Bytecode.VirtualMachine(Stash.Stdlib.StdlibDefinitions.CreateVMGlobals());
        _deps = new CompletionDeps(vm, new PathExecutableCache(), new CustomCompleterRegistry());
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); }
        catch { /* best effort */ }
    }

    private CursorContext MakeCtx(string token) =>
        new(CompletionMode.Shell, 0, token.Length, token, false, '\0', false, Array.Empty<string>());

    // ── Empty token: all visible entries (no dotfiles) ────────────────────────

    [Fact]
    public void Complete_EmptyToken_ReturnsVisibleEntries()
    {
        var ctx = MakeCtx(PathInTmp(""));
        IReadOnlyList<Candidate> result = _completer.Complete(ctx, _deps);

        var displays = result.Select(c => c.Display).ToArray();
        Assert.Contains("Alpha.txt", displays);
        Assert.Contains("beta.md", displays);
        Assert.Contains("subdir/", displays);
        Assert.DoesNotContain(".hidden", displays);
    }

    // ── Dot prefix: dotfiles included ────────────────────────────────────────

    [Fact]
    public void Complete_DotPrefix_IncludesDotfiles()
    {
        var ctx = MakeCtx(PathInTmp("."));
        IReadOnlyList<Candidate> result = _completer.Complete(ctx, _deps);

        Assert.Contains(result, c => c.Display == ".hidden");
    }

    // ── Smart-case lower: case-insensitive match ──────────────────────────────

    [Fact]
    public void Complete_LowerPrefix_MatchesCaseInsensitive()
    {
        var ctx = MakeCtx(PathInTmp("al"));
        IReadOnlyList<Candidate> result = _completer.Complete(ctx, _deps);

        Assert.Contains(result, c => c.Display == "Alpha.txt");
    }

    // ── Smart-case upper: case-sensitive, no match ────────────────────────────

    [Fact]
    public void Complete_UpperPrefix_NoCaseInsensitiveMatch()
    {
        var ctx = MakeCtx(PathInTmp("AL"));
        IReadOnlyList<Candidate> result = _completer.Complete(ctx, _deps);

        // "AL" is uppercase → case-sensitive → no match for "Alpha.txt" (starts with 'A' not 'AL')
        Assert.DoesNotContain(result, c => c.Display == "Alpha.txt");
    }

    // ── Directory trailing slash ──────────────────────────────────────────────

    [Fact]
    public void Complete_DirectoryEntry_HasTrailingSlash()
    {
        var ctx = MakeCtx(PathInTmp("sub"));
        IReadOnlyList<Candidate> result = _completer.Complete(ctx, _deps);

        Assert.Contains(result, c => c.Display == "subdir/" && c.Kind == CandidateKind.Directory);
    }

    // ── Bare tilde → ~/  ─────────────────────────────────────────────────────

    [Fact]
    public void Complete_BareTilde_ReturnsHomeDirCandidate()
    {
        var ctx = MakeCtx("~");
        IReadOnlyList<Candidate> result = _completer.Complete(ctx, _deps);

        Assert.Single(result);
        Assert.Equal("~/", result[0].Insert);
        Assert.Equal(CandidateKind.Directory, result[0].Kind);
    }

    // ── Tilde home prefix ─────────────────────────────────────────────────────

    [Fact]
    public void Complete_TildeSlash_EnumeratesHomeDir()
    {
        if (OperatingSystem.IsWindows()) return; // HOME not guaranteed on Windows CI

        string? home = Environment.GetEnvironmentVariable("HOME");
        if (string.IsNullOrEmpty(home) || !Directory.Exists(home)) return;

        var ctx = MakeCtx("~/");
        IReadOnlyList<Candidate> result = _completer.Complete(ctx, _deps);

        // Insert values should all start with ~/
        foreach (var c in result)
            Assert.StartsWith("~/", c.Insert);
    }

    // ── File kind ────────────────────────────────────────────────────────────

    [Fact]
    public void Complete_FileEntry_HasFileKind()
    {
        var ctx = MakeCtx(PathInTmp("Al"));
        IReadOnlyList<Candidate> result = _completer.Complete(ctx, _deps);

        Assert.Contains(result, c => c.Display == "Alpha.txt" && c.Kind == CandidateKind.File);
    }

    // ── Permission-denied directory ───────────────────────────────────────────

    [Fact]
    public void Complete_NonExistentDir_ReturnsEmpty()
    {
        string nonExistent = Path.Combine(_tmpDir, "does_not_exist", "");
        var ctx = MakeCtx(nonExistent);
        IReadOnlyList<Candidate> result = _completer.Complete(ctx, _deps);

        Assert.Empty(result);
    }

    // ── Insert preserves directory prefix ────────────────────────────────────

    [Fact]
    public void Complete_DirPrefix_InsertPreservesDirPart()
    {
        string prefix = _tmpDir.TrimEnd('/') + "/Al";
        var ctx = MakeCtx(prefix);
        IReadOnlyList<Candidate> result = _completer.Complete(ctx, _deps);

        // Insert should start with the directory prefix
        foreach (var c in result)
            Assert.StartsWith(_tmpDir.TrimEnd('/') + "/", c.Insert);
    }

    // ── Helper ───────────────────────────────────────────────────────────────

    /// <summary>Builds a path token using the tmp dir as directory prefix.</summary>
    private string PathInTmp(string namePart) =>
        _tmpDir.TrimEnd('/') + '/' + namePart;
}
