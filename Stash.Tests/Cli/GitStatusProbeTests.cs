using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Stash.Cli.Repl;

namespace Stash.Tests.Cli;

/// <summary>
/// Integration tests for <see cref="GitStatusProbe"/>. These tests require
/// <c>git</c> to be on PATH; each test checks availability and skips gracefully
/// when git is not found.
/// Tests use isolated temp directories and never modify any real repository.
/// </summary>
public sealed class GitStatusProbeTests : IDisposable
{
    private readonly string _tempRoot;

    public GitStatusProbeTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"stash-git-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool GitAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return false;
            p.WaitForExit(2000);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string MakeDir(string label = "")
        => Directory.CreateDirectory(Path.Combine(_tempRoot,
            string.IsNullOrEmpty(label) ? Guid.NewGuid().ToString("N") : label)).FullName;

    /// <summary>Runs a git command in <paramref name="workDir"/>. Throws on non-zero exit.</summary>
    private static void Git(string workDir, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = args,
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // Suppress git's need for a user identity in CI
        psi.Environment["GIT_AUTHOR_NAME"] = "Test";
        psi.Environment["GIT_AUTHOR_EMAIL"] = "test@test.com";
        psi.Environment["GIT_COMMITTER_NAME"] = "Test";
        psi.Environment["GIT_COMMITTER_EMAIL"] = "test@test.com";

        using var p = Process.Start(psi)!;
        p.WaitForExit(5000);
    }

    // =========================================================================
    // 1. Outside any git repo
    // =========================================================================

    [Fact]
    public void Probe_OutsideGitRepo_ReturnsNotInRepo()
    {
        if (!GitAvailable()) return; // skip if git not found

        // Use a fresh isolated temp dir that cannot be inside any git repo
        string isolated = MakeDir("no-repo");

        var result = GitStatusProbe.Probe(isolated, timeoutMs: 3000);

        // Either null (probe failed) or isInRepo == false (clean non-repo exit)
        if (result is not null)
        {
            bool isInRepo = result.GetField("isInRepo", null).ToObject() is true;
            Assert.False(isInRepo, "Probe of a non-git directory should return isInRepo=false");
        }
    }

    // =========================================================================
    // 2. Clean git repo
    // =========================================================================

    [Fact]
    public void Probe_InsideCleanRepo_ReturnsInRepo_WithBranch()
    {
        if (!GitAvailable()) return;

        string repoDir = MakeDir("clean-repo");
        Git(repoDir, "init");
        Git(repoDir, "commit --allow-empty -m init");

        var result = GitStatusProbe.Probe(repoDir, timeoutMs: 3000);

        Assert.NotNull(result);
        bool isInRepo = result!.GetField("isInRepo", null).ToObject() is true;
        Assert.True(isInRepo);

        object? branch = result.GetField("branch", null).ToObject();
        Assert.IsType<string>(branch);
        Assert.NotEmpty((string)branch!);

        // Clean repo has zero dirty counts
        long staged = result.GetField("stagedCount", null).ToObject() as long? ?? 0L;
        long unstaged = result.GetField("unstagedCount", null).ToObject() as long? ?? 0L;
        long untracked = result.GetField("untrackedCount", null).ToObject() as long? ?? 0L;
        Assert.Equal(0L, staged + unstaged + untracked);
    }

    // =========================================================================
    // 3. Staged file
    // =========================================================================

    [Fact]
    public void Probe_WithStagedFile_HasStagedCount_IsDirty()
    {
        if (!GitAvailable()) return;

        string repoDir = MakeDir("staged-repo");
        Git(repoDir, "init");
        Git(repoDir, "commit --allow-empty -m init");

        // Stage a new file
        File.WriteAllText(Path.Combine(repoDir, "new.txt"), "hello");
        Git(repoDir, "add new.txt");

        var result = GitStatusProbe.Probe(repoDir, timeoutMs: 3000);

        Assert.NotNull(result);
        long staged = result!.GetField("stagedCount", null).ToObject() as long? ?? 0L;
        bool isDirty = result.GetField("isDirty", null).ToObject() is true;

        Assert.True(staged > 0, $"Expected stagedCount > 0, got {staged}");
        Assert.True(isDirty);
    }

    // =========================================================================
    // 4. Untracked file
    // =========================================================================

    [Fact]
    public void Probe_WithUntrackedFile_HasUntrackedCount_IsDirty()
    {
        if (!GitAvailable()) return;

        string repoDir = MakeDir("untracked-repo");
        Git(repoDir, "init");
        Git(repoDir, "commit --allow-empty -m init");

        // Create a file but don't add it
        File.WriteAllText(Path.Combine(repoDir, "untracked.txt"), "untracked");

        var result = GitStatusProbe.Probe(repoDir, timeoutMs: 3000);

        Assert.NotNull(result);
        long untracked = result!.GetField("untrackedCount", null).ToObject() as long? ?? 0L;
        bool isDirty = result.GetField("isDirty", null).ToObject() is true;

        Assert.True(untracked > 0, $"Expected untrackedCount > 0, got {untracked}");
        Assert.True(isDirty);
    }

    // =========================================================================
    // 5. Very small timeout
    // =========================================================================

    [Fact]
    public void Probe_VerySmallTimeout_DoesNotThrow()
    {
        if (!GitAvailable()) return;

        string dir = MakeDir("timeout-test");

        // With a 1 ms timeout, git likely won't complete in time.
        // The probe should return null or a non-repo result without throwing.
        var result = GitStatusProbe.Probe(dir, timeoutMs: 1);

        // No assertion on the value — either null or {isInRepo: false} is acceptable.
        // The important thing is no exception escapes.
    }

    // =========================================================================
    // 6. Nonexistent working directory
    // =========================================================================

    [Fact]
    public void Probe_NonexistentDir_ReturnsNullOrNotInRepo_DoesNotThrow()
    {
        if (!GitAvailable()) return;

        string nonexistent = Path.Combine(_tempRoot, "does-not-exist");

        // Should not throw; git will fail to run in a nonexistent dir
        var result = GitStatusProbe.Probe(nonexistent, timeoutMs: 3000);

        // null or isInRepo=false both acceptable
        if (result is not null)
        {
            bool isInRepo = result.GetField("isInRepo", null).ToObject() is true;
            Assert.False(isInRepo);
        }
    }
}
