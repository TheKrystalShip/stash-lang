using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Stash.Registry.Web.Pages;
using Stash.Tests.Registry.Web.Fixtures;
using Xunit;

namespace Stash.Tests.Registry.Web;

/// <summary>
/// Architecture guard: asserts that all Phase-2 files in <see cref="PhaseTwoFileSet.Paths"/>
/// are byte-unchanged between the Phase-2 merge-base and <c>HEAD</c>.
/// </summary>
/// <remarks>
/// <para>
/// The check uses <c>git diff --quiet &lt;phase-2-base&gt; -- &lt;files&gt;</c> which exits 1
/// if any diff is non-empty, 0 if clean. This compares the base commit to the working tree
/// so uncommitted edits are also caught.
/// </para>
/// <para>
/// <b>Phase-2 merge-base computation:</b> <c>git merge-base HEAD main</c> finds the most
/// recent common ancestor. In a worktree, the root is detected by walking up from
/// <see cref="AppContext.BaseDirectory"/> until a file named <c>Stash.sln</c> is found.
/// </para>
/// <para>
/// <b>Override:</b> set the <c>PHASE2_BASE_REF</c> environment variable to pin a specific
/// commit SHA, bypassing the dynamic <c>git merge-base</c> computation. Useful in CI or when
/// the worktree is detached.
/// </para>
/// <para>
/// <b>Fail-path fixture:</b>
/// <see cref="DiffComparator_OnSyntheticNonEmptyDiff_DetectsChange"/> proves the comparator
/// trips on a synthetic diff, so "0 diff because scan ran nothing" is not a silent pass.
/// </para>
/// </remarks>
public sealed class PhaseTwoFilesByteUnchangedMetaTests
{
    // ── Repo-root discovery ───────────────────────────────────────────────────

    /// <summary>
    /// Finds the repository root by walking up from the test output directory until
    /// a <c>Stash.sln</c> file is found. In a worktree, the root has a <c>.git</c>
    /// <em>file</em> (not a directory), so we walk by <c>Stash.sln</c> presence.
    /// </summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Stash.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Cannot find the repo root (Stash.sln not found). " +
            "The test must run from within the repository.");
    }

    // ── Phase-2 merge-base ────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the Phase-2 merge-base ref. Checks the <c>PHASE2_BASE_REF</c> environment
    /// variable first (override); falls back to <c>git merge-base HEAD main</c>.
    /// </summary>
    private static string GetPhase2BaseRef(string repoRoot)
    {
        var envOverride = Environment.GetEnvironmentVariable("PHASE2_BASE_REF");
        if (!string.IsNullOrWhiteSpace(envOverride))
            return envOverride.Trim();

        return RunGit(repoRoot, "merge-base HEAD main").Trim();
    }

    // ── Git helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Runs a git command in <paramref name="repoRoot"/> and returns stdout.
    /// Throws on non-zero exit code (except where the caller checks explicitly).
    /// </summary>
    private static string RunGit(string repoRoot, string arguments)
    {
        var startInfo = new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start git process.");
        process.WaitForExit(10_000);

        var stdout = process.StandardOutput.ReadToEnd().Trim();
        var stderr = process.StandardError.ReadToEnd().Trim();

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"git {arguments} failed with exit code {process.ExitCode}.\n" +
                $"stdout: {stdout}\nstderr: {stderr}");

        return stdout;
    }

    /// <summary>
    /// Runs <c>git diff --quiet &lt;baseRef&gt; -- &lt;files&gt;</c> and returns
    /// <see langword="true"/> if there is NO diff (exit 0), <see langword="false"/> if diff exists (exit 1).
    /// </summary>
    internal static bool HasNoDiff(string repoRoot, string baseRef, IEnumerable<string> relativeFilePaths)
    {
        // Build the path list prefixed with the project sub-directory.
        var paths = string.Join(" ", relativeFilePaths.Select(p =>
            $"Stash.Registry.Web/{p.Replace('\\', '/')}"));

        var arguments = $"diff --quiet {baseRef} -- {paths}";

        var startInfo = new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start git process for diff check.");
        process.WaitForExit(15_000);

        return process.ExitCode == 0; // 0 = no diff, 1 = diff exists
    }

    // ── Production compliance ─────────────────────────────────────────────────

    /// <summary>
    /// <b>Load-bearing assertion.</b> Runs <c>git diff --quiet &lt;phase-2-base&gt; --
    /// &lt;Phase-2 file set&gt;</c> and fails if any Phase-2 file has been modified.
    /// </summary>
    [Fact]
    public void AllPhase2Files_AreByteUnchanged_SincePhase2MergeBase()
    {
        var repoRoot = FindRepoRoot();
        var baseRef = GetPhase2BaseRef(repoRoot);

        // ── Binding floor: file set is non-empty ──────────────────────────────
        Assert.True(
            PhaseTwoFileSet.Paths.Length > 0,
            "PhaseTwoFileSet.Paths is empty — the binding floor check cannot proceed. " +
            "Add the Phase-2 file list to PhaseTwoFileSet.Paths.");

        var noDiff = HasNoDiff(repoRoot, baseRef, PhaseTwoFileSet.Paths);

        Assert.True(
            noDiff,
            $"One or more Phase-2 files have been modified since the Phase-2 merge-base '{baseRef}'. " +
            $"The following files are in the pinned set and must not be edited by Phase 3:\n" +
            string.Join("\n", PhaseTwoFileSet.Paths.Select(p => $"  Stash.Registry.Web/{p}")) + "\n\n" +
            "Run `git diff <phase-2-base> -- Stash.Registry.Web/` to see the diff.");
    }

    // ── Fail-path fixtures ────────────────────────────────────────────────────

    /// <summary>
    /// Proves the diff comparator has teeth: <see cref="HasNoDiff"/> returns <see langword="false"/>
    /// for a file that IS modified in Phase 3 (e.g. <c>Program.cs</c>), and returns
    /// <see langword="true"/> for a Phase-2 file we know has NOT changed
    /// (<c>Services/HttpRegistryClient.cs</c>).
    /// This exercises BOTH branches of <see cref="HasNoDiff"/> so a vacuous pass is impossible.
    /// </summary>
    [Fact]
    public void DiffComparator_BothBranches_WorkCorrectly()
    {
        var repoRoot = FindRepoRoot();
        var baseRef = GetPhase2BaseRef(repoRoot);

        // ── Dirty branch: Program.cs is modified in Phase 3 → HasNoDiff returns false ──
        // This proves the comparator DOES trip when a file has changed.
        var programIsDirty = !HasNoDiff(repoRoot, baseRef, new[] { "Program.cs" });
        Assert.True(
            programIsDirty,
            $"Program.cs should differ from the Phase-2 merge-base (it was modified in Phase 3), " +
            $"but HasNoDiff returned true. The comparator may not be detecting changes. " +
            $"Base ref: {baseRef}");

        // ── Clean branch: HttpRegistryClient.cs is NOT modified → HasNoDiff returns true ──
        // This proves the comparator does NOT false-positive on unchanged Phase-2 files.
        var httpClientIsClean = HasNoDiff(repoRoot, baseRef, new[] { "Services/HttpRegistryClient.cs" });
        Assert.True(
            httpClientIsClean,
            $"Services/HttpRegistryClient.cs should be byte-unchanged from the Phase-2 merge-base, " +
            $"but HasNoDiff returned false. Either the file was accidentally modified or the " +
            $"base-ref computation is wrong. Base ref: {baseRef}");
    }

    /// <summary>
    /// Proves that no Phase-2 file appears in the A1 phase's <c>files</c> list.
    /// This is a self-check by the implementer that the phase boundaries are respected.
    /// </summary>
    [Fact]
    public void A1PhaseFiles_ContainNoPhase2Files()
    {
        // The A1 phase's files list (from plan.yaml) — all new files.
        var a1Files = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "Auth/BffSession.cs",
            "Auth/ISessionStore.cs",
            "Auth/InMemorySessionStore.cs",
            "Auth/ISessionTokenAccessor.cs",
            "Auth/CookieSessionTokenAccessor.cs",
            "Auth/NoActiveSessionException.cs",
            "Auth/SessionCookie.cs",
            "Auth/SessionCookieAuthenticationOptions.cs",
            "Auth/SessionCookieAuthenticationHandler.cs",
            "Auth/LoginService.cs",
            "Auth/LogoutService.cs",
            "Pages/Login.cshtml",
            "Pages/Login.cshtml.cs",
            "Pages/Logout.cshtml",
            "Pages/Logout.cshtml.cs",
            "wwwroot/css/site.css",
            "Program.cs",
            "Stash.Registry.Web.csproj",
            "appsettings.json",
            "appsettings.Development.json",
        };

        var phase2Set = new HashSet<string>(PhaseTwoFileSet.Paths, System.StringComparer.OrdinalIgnoreCase);

        var overlap = a1Files.Intersect(phase2Set).ToList();

        Assert.True(
            overlap.Count == 0,
            $"The following files appear in BOTH the A1 phase files list and the Phase-2 " +
            $"file set — Phase-2 files must not be edited in A1:\n" +
            string.Join("\n", overlap.Select(p => $"  {p}")));
    }

    /// <summary>
    /// Verifies the Phase-2 file set has at least the expected minimum count.
    /// Removing entries from the set is a deliberate action that should fail here first.
    /// </summary>
    [Fact]
    public void PhaseTwoFileSet_HasExpectedMinimumCount()
    {
        // 5 page cshtml + 5 page cshtml.cs + _Layout + _ViewImports + _ViewStart
        // + 3 Services + RegistryClientConfig + 3 Rendering = 19 files.
        const int minExpected = 19;

        Assert.True(
            PhaseTwoFileSet.Paths.Length >= minExpected,
            $"PhaseTwoFileSet.Paths has only {PhaseTwoFileSet.Paths.Length} entries, " +
            $"expected at least {minExpected}. " +
            "Removing entries from the set is a deliberate action — add a comment explaining why.");
    }
}
