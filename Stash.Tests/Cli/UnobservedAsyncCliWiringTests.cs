namespace Stash.Tests.Cli;

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

/// <summary>
/// D1 — CLI driver wiring: verifies that the <c>ReportUnobservedFaults</c> call-sites
/// in <c>Program.RunFile</c> and <c>Program.RunSource</c> are correctly wired.
///
/// <para>
/// These tests exercise the <em>production path</em> — they spawn the real CLI binary as
/// a subprocess and capture stdout, stderr, and the exit code.  Only subprocess invocation
/// can exercise the <c>finally</c>/<c>catch</c> exit-hook sequencing in <c>Program.cs</c>;
/// calling <c>Program.Main</c> directly is unsafe because it calls <c>Environment.Exit</c>.
/// </para>
///
/// <para>
/// No <c>[Collection]</c> attribute is needed: these tests do not capture the parent
/// process's <c>Console</c> — each spawns a fresh child process, so they are safe to run
/// in parallel with other test classes.
/// </para>
/// </summary>
public sealed class UnobservedAsyncCliWiringTests : IDisposable
{
    private readonly string _tempDir;

    public UnobservedAsyncCliWiringTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"stash-cli-wiring-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the path to the <c>Stash</c> CLI apphost built alongside the test suite.
    /// Derives the path from <see cref="AppContext.BaseDirectory"/> (the test bin dir) by
    /// replacing the <c>Stash.Tests</c> segment with <c>Stash.Cli</c>, then appending the
    /// platform-appropriate apphost name.
    /// </summary>
    private static string GetCliBinaryPath()
    {
        string testBinDir = AppContext.BaseDirectory;
        // e.g. …/Stash.Tests/bin/Debug/net10.0/ → …/Stash.Cli/bin/Debug/net10.0/
        string cliBinDir = testBinDir.Replace(
            Path.DirectorySeparatorChar + "Stash.Tests" + Path.DirectorySeparatorChar,
            Path.DirectorySeparatorChar + "Stash.Cli" + Path.DirectorySeparatorChar,
            StringComparison.Ordinal);

        string apphost = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Stash.exe" : "Stash";
        return Path.Combine(cliBinDir, apphost);
    }

    /// <summary>
    /// Runs the CLI binary against <paramref name="scriptPath"/> and returns
    /// (stdout, stderr, exitCode). Reads both streams concurrently to prevent deadlock.
    /// </summary>
    private static async Task<(string Stdout, string Stderr, int ExitCode)> RunCliAsync(string scriptPath)
    {
        string binary = GetCliBinaryPath();
        var psi = new ProcessStartInfo
        {
            FileName = binary,
            ArgumentList = { scriptPath },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start CLI process: {binary}");

        // Read both streams concurrently — sequential reads deadlock if the other buffer fills.
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();

        bool exited = process.WaitForExit(30_000);
        if (!exited)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"CLI process did not exit within 30 s for script: {scriptPath}");
        }

        string stdout = await stdoutTask;
        string stderr = await stderrTask;
        return (stdout, stderr, process.ExitCode);
    }

    private string WriteTempScript(string name, string source)
    {
        string path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, source);
        return path;
    }

    // ── done_when #1: unobserved faulted task → exit 0, warning on stderr ────

    /// <summary>
    /// A script that spawns a faulting task (and waits long enough for it to fault before
    /// the process exits) produces exit code 0, empty stdout, and exactly one D1 warning
    /// block on stderr. Exercises the <c>finally { if (!primaryErrored) Report(…) }</c>
    /// path in <c>Program.RunFile</c>.
    /// </summary>
    [Fact]
    public async Task UnobservedFaultedTask_ExitZero_WarningOnStderr()
    {
        string script = WriteTempScript("unobserved_fault.stash", @"
task.run(() => { throw ValueError { message: ""oops"" }; });
time.sleep(0.3);
");
        var (stdout, stderr, exitCode) = await RunCliAsync(script);

        Assert.Equal(0, exitCode);
        Assert.Equal("", stdout);
        Assert.Contains("warning: 1 unobserved async error(s):", stderr);
        Assert.Contains("ValueError: oops", stderr);

        // Exactly one warning header — not duplicated
        int warningCount = stderr.Split('\n').CountWhere(line => line.StartsWith("warning:"));
        Assert.Equal(1, warningCount);
    }

    // ── done_when #7: top-level error does NOT excuse the unobserved report ──

    /// <summary>
    /// A script that first spawns a faulting task (then sleeps to let it fault), then
    /// throws a top-level error produces: the primary error on stderr, the D1 warning block
    /// on stderr (in that order), and exit code 70. Exercises the
    /// <c>catch (RuntimeError) { PrintRuntimeError(ex); Report(…); Exit(70); }</c> path.
    /// </summary>
    [Fact]
    public async Task TopLevelError_AndUnobservedFault_BothReported_ExitSeventy()
    {
        string script = WriteTempScript("toplevel_and_unobserved.stash", @"
task.run(() => { throw ValueError { message: ""async-oops"" }; });
time.sleep(0.3);
throw ValueError { message: ""top-level-error"" };
");
        var (stdout, stderr, exitCode) = await RunCliAsync(script);

        Assert.Equal(70, exitCode);
        Assert.Equal("", stdout);

        // Primary error appears
        Assert.Contains("ValueError: top-level-error", stderr);

        // Unobserved warning also appears (done_when #7: primary error does not excuse it)
        Assert.Contains("warning: 1 unobserved async error(s):", stderr);
        Assert.Contains("ValueError: async-oops", stderr);

        // Primary error comes BEFORE the unobserved warning
        int primaryIdx = stderr.IndexOf("ValueError: top-level-error", StringComparison.Ordinal);
        int warningIdx = stderr.IndexOf("warning: 1 unobserved async error(s):", StringComparison.Ordinal);
        Assert.True(primaryIdx < warningIdx,
            $"Expected primary error (pos {primaryIdx}) before unobserved warning (pos {warningIdx})");
    }
}

// ── Local extension helper (avoids pulling in a LINQ package for one call) ──

file static class StringArrayExtensions
{
    internal static int CountWhere(this string[] arr, Func<string, bool> predicate)
    {
        int n = 0;
        foreach (string s in arr)
            if (predicate(s)) n++;
        return n;
    }
}
