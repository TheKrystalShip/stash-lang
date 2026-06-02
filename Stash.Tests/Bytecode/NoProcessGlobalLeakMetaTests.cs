using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Stash.Tests.Bytecode;

/// <summary>
/// Roslyn-based meta-test that guards against direct process-global reads and writes
/// in <c>Stash.Stdlib/</c> and <c>Stash.Bytecode/</c> that bypass the per-VM virtualized
/// state on <see cref="Stash.Bytecode.Runtime.VMContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The problem:</b> two <c>VirtualMachine</c> instances in the same .NET process must
/// share nothing observable through the environment.  Today, every direct call to
/// <c>System.Environment.CurrentDirectory</c>, <c>Environment.GetEnvironmentVariable</c>,
/// <c>Environment.SetEnvironmentVariable</c>, <c>Environment.GetEnvironmentVariables</c>,
/// and <c>Directory.GetCurrentDirectory()</c> in stdlib or bytecode code reaches past the
/// VM and mutates or reads the real process-global state — a host-isolation hazard.
/// </para>
/// <para>
/// <b>The allowed pattern:</b> code should read/write <c>ctx.WorkingDirectory</c>,
/// <c>ctx.GetEnv(name)</c>, <c>ctx.SetEnv(name, value)</c>, <c>ctx.UnsetEnv(name)</c>,
/// <c>ctx.AllEnv()</c>, or <c>ctx.ResolveAgainstCwd(path)</c>.
/// </para>
/// <para>
/// <b>NOT scanned — process-identity reads.</b>  <c>System.Environment.GetFolderPath</c>,
/// <c>Environment.MachineName</c>, and <c>Environment.UserName</c> are NOT in the scan set.
/// These three are constants for the process lifetime (user home directory, hostname, login
/// name) rather than mutable env/cwd state.  They intentionally bypass the VM overlay and
/// will never appear on the exemption list.  <c>env.home</c>, <c>env.hostname</c>, and
/// <c>env.user</c> surface these via the identity-read path deliberately.
/// </para>
/// <para>
/// <b>NOT scanned — CLI-only assemblies.</b>  <c>Stash.Cli/</c> is NOT in scope for this
/// scan.  CLI host code legitimately consults real process state (e.g., to seed a
/// <c>StashEngine</c>), and that is expected behavior at the host boundary.  Only
/// <c>Stash.Stdlib/</c> and <c>Stash.Bytecode/</c> are scanned — they form the engine
/// interior that must never bypass the VM view.
/// </para>
/// <para>
/// <b>The approach:</b> scan all <c>.cs</c> files under <c>Stash.Stdlib/</c> and
/// <c>Stash.Bytecode/</c> for <c>MemberAccessExpressionSyntax</c> nodes whose member
/// name is one of the five target APIs:
/// <list type="bullet">
///   <item><c>CurrentDirectory</c> (from <c>System.Environment</c>)</item>
///   <item><c>GetEnvironmentVariable</c></item>
///   <item><c>GetEnvironmentVariables</c></item>
///   <item><c>SetEnvironmentVariable</c></item>
///   <item><c>GetCurrentDirectory</c> (from <c>Directory</c>)</item>
/// </list>
/// Each sink is classified as:
/// <list type="bullet">
///   <item><b>Pinned</b> — the file appears in <see cref="PinnedExemptions"/> with a
///     recorded reason and an expected count that matches the actual occurrences.</item>
///   <item><b>Violation</b> — not pinned.  The test fails on any violation.</item>
/// </list>
/// </para>
/// <para>
/// <b>Three assertions prove correctness:</b>
/// <list type="number">
///   <item><b>Production compliance</b> — every sink in the two source trees is
///     covered by the pinned exemption map.</item>
///   <item><b>Fail-path (teeth)</b> — a synthetic fixture snippet containing a raw
///     <c>System.Environment.CurrentDirectory</c> access is detected as a violation,
///     proving the scan catches the exact omission we care about.</item>
///   <item><b>Exemption pin</b> — the exact per-file sink count in
///     <see cref="PinnedExemptions"/> matches the actual scanned counts.  Adding or
///     removing a sink without updating the pin forces a test edit — forcing deliberate
///     reviewer attention on every change to the exemption list.</item>
/// </list>
/// </para>
/// <para>
/// <b>Phase lifecycle:</b> this guard goes up GREEN on its first commit (phase 2B-2) with
/// the full set of today's sinks as exemptions.  Subsequent migration phases (2B-3 cwd,
/// 2B-4 env) each route a batch of sinks through the <c>VMContext</c> API and shrink the
/// exemption counts accordingly.  The guard stays GREEN throughout; the exemption map
/// approaches zero for to-be-migrated files, while <c>VMContext.cs</c> retains three
/// permanent "chokepoint" entries (the fallback-to-real-env reads that ARE the per-VM
/// overlay implementation).
/// </para>
/// <para>
/// <b>Pinned sinks (as of 2026-06-02, phase 2B-4 final state — ZERO active stdlib exemptions):</b>
/// See <see cref="PinnedExemptions"/> for the per-file counts and recorded rationales.
/// Only <c>VMContext.cs</c>'s three permanent chokepoint sinks remain.
/// </para>
/// </remarks>
public sealed class NoProcessGlobalLeakMetaTests
{
    // ── Scan targets ──────────────────────────────────────────────────────────

    /// <summary>
    /// The set of <c>MemberAccessExpressionSyntax</c> member names that constitute a
    /// process-global leak when used in <c>Stash.Stdlib/</c> or <c>Stash.Bytecode/</c>
    /// outside the pinned exemption list.
    ///
    /// <para>
    /// NOT included: <c>GetFolderPath</c>, <c>MachineName</c>, <c>UserName</c> — these
    /// are process-identity reads (constants for the process lifetime), not mutable
    /// env/cwd state.  They are intentionally exempt from the scan; see class remarks.
    /// </para>
    /// </summary>
    private static readonly IReadOnlySet<string> ScannedMemberNames =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "CurrentDirectory",        // System.Environment.CurrentDirectory (get/set)
            "GetEnvironmentVariable",  // System.Environment.GetEnvironmentVariable(name)
            "GetEnvironmentVariables", // System.Environment.GetEnvironmentVariables()
            "SetEnvironmentVariable",  // System.Environment.SetEnvironmentVariable(name, value)
            "GetCurrentDirectory",     // Directory.GetCurrentDirectory()
        };

    // ── Pinned exemptions (file → expected sink count) ────────────────────────

    /// <summary>
    /// Per-file count of process-global leak sinks that are currently permitted.
    /// Maps a forward-slash relative path (relative to the owning source root, either
    /// <c>Stash.Stdlib/</c> or <c>Stash.Bytecode/</c>) to the exact number of pinned
    /// occurrences in that file.
    ///
    /// <para>
    /// Each file's entry carries its rationale below.  To shrink a count: migrate the
    /// corresponding sinks to use the <c>IInterpreterContext</c> API
    /// (<c>ctx.WorkingDirectory</c>, <c>ctx.GetEnv</c>, etc.), then decrement the count
    /// here.  The pin assertion will fail if actual ≠ recorded — no silent drifts.
    /// </para>
    ///
    /// <para>
    /// <b>Bytecode/Runtime/VMContext.cs (3) — PERMANENT (chokepoint fallback reads).</b>
    /// VMContext is the per-VM overlay implementation itself.  Its three sinks are:
    /// <list type="bullet">
    ///   <item>Line 31 — <c>WorkingDirectory = System.Environment.CurrentDirectory</c>:
    ///     the one-time seed at VM construction; the only allowed read of the real process
    ///     cwd.  Never re-read after construction.</item>
    ///   <item>Line 96 — <c>System.Environment.GetEnvironmentVariable(name)</c>:
    ///     the overlay fallback in <c>GetEnv</c> — when the per-VM overlay has no entry
    ///     for <paramref name="name"/>, falls back to the real process env.</item>
    ///   <item>Line 110 — <c>System.Environment.GetEnvironmentVariables()</c>:
    ///     the overlay fallback in <c>AllEnv</c> — enumerates the real process env as
    ///     the base layer under the per-VM overlay.</item>
    /// </list>
    /// These three sites ARE the destination of the migration; they will never be removed.
    /// </para>
    ///
    /// <para>
    /// <b>All other files — MIGRATED in phases 2B-3 and 2B-4.</b>
    /// <list type="bullet">
    ///   <item><c>EnvBuiltIns.cs</c> (11), <c>CurrentProcessImpl.cs</c> (5),
    ///     <c>ProcessBuiltIns.cs</c> (6) — migrated in phase 2B-3.</item>
    ///   <item><c>GlobExpander.cs</c> (1), <c>SysBuiltIns.cs</c> (2),
    ///     <c>TermBuiltIns.cs</c> (2), <c>PromptBuiltIns.cs</c> (4),
    ///     <c>CliBuiltIns.Parse.cs</c> (1), <c>PkgBuiltIns.cs</c> (2) — migrated in
    ///     phase 2B-4.</item>
    /// </list>
    /// All rows removed from exemption list; ZERO active stdlib/bytecode exemptions remain.
    /// </para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int> PinnedExemptions =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            // Bytecode/ — permanent chokepoint (3 sinks, never migrated away)
            // These are VMContext's own fallback reads to real process env/cwd;
            // they ARE the per-VM overlay implementation, not bypasses of it.
            ["Runtime/VMContext.cs"] = 3,
        };

    // ── Source-directory discovery ────────────────────────────────────────────

    /// <summary>Minimum number of <c>.cs</c> files the scan must parse per source root.
    /// Guards against a vacuous pass when path discovery regresses and the scanner
    /// processes zero files.  Decoupled from sink count (which shrinks toward zero
    /// as 2B-3/2B-4 migrate sinks).</summary>
    private const int MinFilesPerSourceDir = 5;

    /// <summary>Locates a project source directory by walking up from the test assembly
    /// until the named <c>.csproj</c> file is found.</summary>
    private static string FindSourceDir(string projectName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, projectName, $"{projectName}.csproj");
            if (File.Exists(candidate))
                return Path.Combine(dir.FullName, projectName);
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            $"Cannot find {projectName}/{projectName}.csproj — test must run from within the repo.");
    }

    // ── Sink representation ───────────────────────────────────────────────────

    /// <summary>Represents a discovered process-global leak site in a source file.</summary>
    private sealed record LeakSite(
        /// <summary>Forward-slash path relative to the owning source root.</summary>
        string RelativePath,
        int Line,
        string MemberName);

    // ── Scan implementation ───────────────────────────────────────────────────

    /// <summary>
    /// Scans all <c>.cs</c> files under <paramref name="sourceDir"/> for
    /// <see cref="ScannedMemberNames"/> accesses and returns every site found.
    /// Files under <c>bin/</c> and <c>obj/</c> subdirectories are excluded.
    /// </summary>
    private static List<LeakSite> ScanDirectory(string sourceDir)
    {
        var sites = new List<LeakSite>();

        var csFiles = Directory.EnumerateFiles(sourceDir, "*.cs", SearchOption.AllDirectories)
            .Where(f =>
            {
                string rel = f.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar, '/');
                return !rel.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && !rel.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            });

        foreach (string filePath in csFiles)
        {
            string source = File.ReadAllText(filePath);
            string relPath = filePath
                .Substring(sourceDir.Length)
                .TrimStart(Path.DirectorySeparatorChar, '/')
                .Replace(Path.DirectorySeparatorChar, '/');

            ScanSource(source, relPath, sites);
        }

        return sites;
    }

    /// <summary>
    /// Parses <paramref name="source"/> with Roslyn and appends every
    /// process-global leak site to <paramref name="sites"/>.
    ///
    /// <para>
    /// The scan walks <c>MemberAccessExpressionSyntax</c> nodes in the parsed
    /// syntax tree — XML doc comments and other trivia are NOT walked, so references
    /// to the banned APIs in comments do not produce false positives.
    /// </para>
    /// </summary>
    private static void ScanSource(string source, string label, List<LeakSite> sites)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetCompilationUnitRoot();

        foreach (var memberAccess in root.DescendantNodes()
                     .OfType<MemberAccessExpressionSyntax>())
        {
            string memberName = memberAccess.Name.Identifier.Text;
            if (!ScannedMemberNames.Contains(memberName)) continue;

            var lineSpan = memberAccess.GetLocation().GetLineSpan();
            int line = lineSpan.StartLinePosition.Line + 1;
            sites.Add(new LeakSite(label, line, memberName));
        }
    }

    // ── Production compliance ─────────────────────────────────────────────────

    /// <summary>
    /// Scans <c>Stash.Stdlib/</c> and <c>Stash.Bytecode/</c> for process-global leak
    /// sinks and asserts that every site is covered by the <see cref="PinnedExemptions"/>
    /// map (file path present AND per-file count matches exactly).
    /// </summary>
    [Fact]
    public void AllProcessGlobalLeakSinks_ArePinnedWithCorrectCount()
    {
        var sites = CollectAllSites(out int stdlibFiles, out int bytecodeFiles);

        AssertMinFilesScanned(stdlibFiles, "Stash.Stdlib");
        AssertMinFilesScanned(bytecodeFiles, "Stash.Bytecode");

        // Group actual sinks by relative path
        var actualCounts = sites
            .GroupBy(s => s.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        // Find violations: sinks in files NOT on the exemption list
        var violations = actualCounts
            .Where(kv => !PinnedExemptions.ContainsKey(kv.Key))
            .OrderBy(kv => kv.Key)
            .ToList();

        Assert.True(
            violations.Count == 0,
            $"{violations.Count} file(s) with unexempted process-global leak sink(s):\n" +
            string.Join("\n", violations.Select(kv =>
                $"  {kv.Key}: {kv.Value} sink(s) — add to PinnedExemptions or route through ctx")) +
            "\n\nAll System.Environment.CurrentDirectory / GetEnvironmentVariable / " +
            "SetEnvironmentVariable / GetEnvironmentVariables / Directory.GetCurrentDirectory " +
            "accesses in Stash.Stdlib/** and Stash.Bytecode/** must appear on the exemption list.");
    }

    /// <summary>
    /// Asserts that the per-file sink count in <see cref="PinnedExemptions"/> exactly
    /// matches the actual scanned count for every pinned file.
    ///
    /// <para>
    /// This is the <em>exact-count pin</em>: adding a sink to an exempted file without
    /// incrementing the count fails here; removing / migrating a sink without decrementing
    /// the count also fails here.  Together with
    /// <see cref="AllProcessGlobalLeakSinks_ArePinnedWithCorrectCount"/>, every change to
    /// the exemption list requires a deliberate edit of this file.
    /// </para>
    /// </summary>
    [Fact]
    public void PinnedExemptions_MatchActualSinkCounts()
    {
        var sites = CollectAllSites(out _, out _);

        var actualCounts = sites
            .GroupBy(s => s.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var mismatches = new List<string>();

        foreach (var (pin, expected) in PinnedExemptions)
        {
            int actual = actualCounts.GetValueOrDefault(pin, 0);
            if (actual != expected)
            {
                mismatches.Add(
                    $"  {pin}: expected {expected} sink(s), found {actual} — " +
                    (actual < expected
                        ? "sink(s) may have been migrated; decrement the pin"
                        : "new sink(s) added without updating the pin; increment or route through ctx"));
            }
        }

        Assert.True(
            mismatches.Count == 0,
            "PinnedExemptions count diverged from actual scanned counts:\n" +
            string.Join("\n", mismatches) +
            "\nUpdate PinnedExemptions in NoProcessGlobalLeakMetaTests to match reality " +
            "and document the rationale for each change.");
    }

    // ── Self-tests (scanner has teeth) ───────────────────────────────────────

    /// <summary>
    /// Verifies the scanner flags a raw <c>System.Environment.CurrentDirectory</c> read
    /// that is NOT on the exemption list — the exact omission the guard exists to catch.
    /// </summary>
    /// <remarks>
    /// The snippet is the content of the embedded fixture file
    /// <c>Bytecode/Fixtures/NoProcessGlobalLeakMetaTests.Fixtures/BadEnvAccess.txt</c>,
    /// which uses a <c>.txt</c> extension to prevent the SDK from compiling it into the
    /// test assembly.
    /// </remarks>
    [Fact]
    public void Scanner_BadEnvAccess_IsDetectedAsLeak()
    {
        string fixtureSource = LoadFixtureText("BadEnvAccess.txt");

        var sites = new List<LeakSite>();
        ScanSource(fixtureSource, "BadEnvAccess.txt", sites);

        Assert.True(
            sites.Count > 0,
            "Scanner found no process-global leak sinks in the bad fixture. " +
            "Check that BadEnvAccess.txt still contains 'System.Environment.CurrentDirectory'.");

        // Every site found must be a leak (nothing on the exemption list, so all are violations)
        Assert.True(
            sites.All(s => s.MemberName == "CurrentDirectory"),
            $"Expected all fixtures sites to be 'CurrentDirectory' accesses, " +
            $"but found: {string.Join(", ", sites.Select(s => s.MemberName))}");
    }

    /// <summary>
    /// Verifies the scanner does NOT flag process-identity reads that are intentionally
    /// outside the scan set: <c>Environment.GetFolderPath</c>, <c>Environment.MachineName</c>,
    /// <c>Environment.UserName</c>.  These are constants for the process lifetime and are
    /// allowed to bypass the per-VM overlay.
    /// </summary>
    [Fact]
    public void Scanner_ProcessIdentityReads_AreNotFlagged()
    {
        const string identitySource = @"
using System;
namespace Stash.Stdlib.Fixtures;
// Process-identity reads: allowed, never overlaid.
// env.home → Environment.GetFolderPath(...)
// env.hostname → Environment.MachineName
// env.user → Environment.UserName
internal static class ProcessIdentityFixture
{
    internal static string Home()     => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    internal static string Hostname() => Environment.MachineName;
    internal static string User()     => Environment.UserName;
}";

        var sites = new List<LeakSite>();
        ScanSource(identitySource, "identity-snippet", sites);

        Assert.True(
            sites.Count == 0,
            $"Scanner incorrectly flagged {sites.Count} process-identity read(s) as leak(s): " +
            string.Join(", ", sites.Select(s => $"{s.MemberName} at line {s.Line}")) +
            ". GetFolderPath/MachineName/UserName are intentionally outside the scan set.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Collects all sink sites from both <c>Stash.Stdlib/</c> and <c>Stash.Bytecode/</c>,
    /// prefixing relative paths with the source-root-relative directory to keep them
    /// distinct (e.g. <c>BuiltIns/EnvBuiltIns.cs</c> vs <c>Runtime/VMContext.cs</c>).
    /// </summary>
    private static List<LeakSite> CollectAllSites(out int stdlibFiles, out int bytecodeFiles)
    {
        string stdlibDir = FindSourceDir("Stash.Stdlib");
        string bytecodeDir = FindSourceDir("Stash.Bytecode");

        var stdlibSites = ScanDirectory(stdlibDir);
        var bytecodeSites = ScanDirectory(bytecodeDir);

        stdlibFiles = Directory.EnumerateFiles(stdlibDir, "*.cs", SearchOption.AllDirectories)
            .Count(f =>
            {
                string rel = f.Substring(stdlibDir.Length).TrimStart(Path.DirectorySeparatorChar, '/');
                return !rel.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && !rel.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            });

        bytecodeFiles = Directory.EnumerateFiles(bytecodeDir, "*.cs", SearchOption.AllDirectories)
            .Count(f =>
            {
                string rel = f.Substring(bytecodeDir.Length).TrimStart(Path.DirectorySeparatorChar, '/');
                return !rel.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && !rel.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            });

        return stdlibSites.Concat(bytecodeSites).ToList();
    }

    /// <summary>Asserts that at least <see cref="MinFilesPerSourceDir"/> files were scanned.</summary>
    private static void AssertMinFilesScanned(int fileCount, string sourceName)
    {
        Assert.True(
            fileCount >= MinFilesPerSourceDir,
            $"Only {fileCount} .cs file(s) found under '{sourceName}/' (expected >= {MinFilesPerSourceDir}). " +
            "Path discovery may have regressed — the scan is not reaching the source tree.");
    }

    // ── Fixture loader ────────────────────────────────────────────────────────

    /// <summary>Reads a fixture embedded resource by file name from the
    /// <c>Stash.Tests.Bytecode.Fixtures.NoProcessGlobalLeakMetaTests.Fixtures</c> namespace.</summary>
    private static string LoadFixtureText(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        string resourceName =
            $"Stash.Tests.Bytecode.Fixtures.NoProcessGlobalLeakMetaTests.Fixtures.{fileName}";

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' not found. " +
                $"Available resources: {string.Join(", ", assembly.GetManifestResourceNames())}");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
