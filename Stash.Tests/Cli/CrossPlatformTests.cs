using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Stash.Bytecode;
using Stash.Cli;
using Stash.Cli.Shell;
using Stash.Runtime;
using Stash.Stdlib;

namespace Stash.Tests.Cli;

/// <summary>
/// §14 cross-platform polish tests.
/// Platform-specific tests are individually gated via <c>if (OperatingSystem.IsWindows()) return;</c>
/// (or the inverse) so they run only on the appropriate platform.
/// </summary>
public class CrossPlatformTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ShellLineClassifier MakeClassifier(Func<string, bool>? isExec = null)
    {
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        var ctx = new ShellContext
        {
            Vm = vm,
            PathCache = new PathExecutableCache(isExec ?? (_ => false)),
            Keywords = ShellContext.BuildKeywordSet(),
            Namespaces = new HashSet<string>(StdlibRegistry.NamespaceNames, StringComparer.Ordinal),
            ShellBuiltinNames = ShellContext.BuildShellBuiltinSet(),
        };
        return new ShellLineClassifier(ctx);
    }

    // ── §14.1 Windows gate message ────────────────────────────────────────────

    [Fact]
    public void WindowsGate_MessageConstant_MatchesSpec()
    {
        // The exact wording is mandated by §14.1; verify the constant so any future
        // edits are caught immediately on all platforms.
        Assert.Equal("shell mode not yet supported on Windows", Program.WindowsNoShellMessage);
    }

    // ── §14.2 Tilde expansion ─────────────────────────────────────────────────

    [Fact]
    public void Tilde_OnPosix_UsesHomeEnv()
    {
        if (OperatingSystem.IsWindows()) return;

        var ctx = new VMContext(CancellationToken.None);
        string expected = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/foo";
        Assert.Equal(expected, ctx.ExpandTilde("~/foo"));
    }

    [Fact]
    public void Tilde_OnPosix_BareTilde_ReturnsHome()
    {
        if (OperatingSystem.IsWindows()) return;

        var ctx = new VMContext(CancellationToken.None);
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(home, ctx.ExpandTilde("~"));
    }

    [Fact]
    public void Tilde_OnWindows_UsesUserProfile()
    {
        if (!OperatingSystem.IsWindows()) return;

        var ctx = new VMContext(CancellationToken.None);
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        // ExpandTilde replaces leading '~' with the UserProfile path verbatim.
        string result = ctx.ExpandTilde("~/foo");
        Assert.StartsWith(userProfile, result, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("foo", result, StringComparison.Ordinal);
    }

    // ── §14.2 Glob case-sensitivity ───────────────────────────────────────────

    [Fact]
    public void Glob_OnPosix_CaseSensitive()
    {
        if (OperatingSystem.IsWindows()) return;

        string tmpDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tmpDir);
        string prev = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(tmpDir);
            File.WriteAllText(Path.Combine(tmpDir, "Foo.txt"), "");
            var matches = GlobExpander.Expand("f*.txt");
            Assert.Empty(matches); // 'f' does NOT match 'F' on a case-sensitive FS
        }
        finally
        {
            Directory.SetCurrentDirectory(prev);
            try { Directory.Delete(tmpDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Glob_OnWindows_CaseInsensitive()
    {
        if (!OperatingSystem.IsWindows()) return;

        string tmpDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tmpDir);
        string prev = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(tmpDir);
            File.WriteAllText(Path.Combine(tmpDir, "Foo.txt"), "");
            var matches = GlobExpander.Expand("f*.txt");
            Assert.Contains("Foo.txt", matches); // 'f' matches 'F' on Windows
        }
        finally
        {
            Directory.SetCurrentDirectory(prev);
            try { Directory.Delete(tmpDir, recursive: true); } catch { }
        }
    }

    // ── §14.2 Windows path-like classifier ───────────────────────────────────

    [Fact]
    public void PathLike_BackslashPath_TriggersShellMode()
    {
        if (!OperatingSystem.IsWindows()) return;

        var classifier = MakeClassifier();
        // Windows absolute drive path: C:\foo.exe
        Assert.Equal(LineMode.Shell, classifier.Classify(@"C:\foo.exe"));
        // Relative dot-backslash path: .\foo.exe (also works on POSIX via .\ handling)
        Assert.Equal(LineMode.Shell, classifier.Classify(@".\foo.exe"));
    }

    [Fact]
    public void PathLike_DotBackslash_TriggersShellModeOnAllPlatforms()
    {
        // .\foo and ..\foo are path-like on both POSIX and Windows.
        var classifier = MakeClassifier();
        Assert.Equal(LineMode.Shell, classifier.Classify(@".\foo.exe"));
        Assert.Equal(LineMode.Shell, classifier.Classify(@"..\foo.exe"));
    }

    [Fact]
    public void PathLike_WindowsDrivePath_IsPathLike()
    {
        // PeekTokenizer should classify C:\... as PathLike (not Identifier).
        // Only meaningful on Windows — on POSIX no file will be found but classification is correct.
        var result = PeekTokenizer.Peek(@"C:\Windows\system32\cmd.exe");
        Assert.Equal(PeekKind.PathLike, result.Kind);
    }

    // ── §14.2 PathExecutableCache — PATHEXT + no-extension lookup ────────────

    [Fact]
    public void PathExecutable_NoExtension_ResolvesViaPathExt()
    {
        if (!OperatingSystem.IsWindows()) return;

        string tmpDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tmpDir);
        string? origPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "stashtest_tool.bat"), "@echo off");
            Environment.SetEnvironmentVariable("PATH", tmpDir + ";" + origPath);

            var cache = new PathExecutableCache();
            Assert.True(cache.IsExecutable("stashtest_tool"),     "no-extension lookup should succeed on Windows");
            Assert.True(cache.IsExecutable("stashtest_tool.bat"), "full-name lookup should also succeed");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", origPath);
            try { Directory.Delete(tmpDir, recursive: true); } catch { }
        }
    }
}
