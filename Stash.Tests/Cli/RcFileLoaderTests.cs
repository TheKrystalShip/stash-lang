using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Stash.Bytecode;
using Stash.Cli.Shell;
using Stash.Runtime;
using Stash.Stdlib;
using Xunit;

namespace Stash.Tests.Cli;

/// <summary>
/// Tests for §12 RC file: path resolution (<see cref="RcFileLoader.FindRcFile"/>) and
/// content loading (<see cref="RcFileLoader.Load"/>).
/// </summary>
public sealed class RcFileLoaderTests : IDisposable
{
    // ── Environment isolation ─────────────────────────────────────────────────

    private readonly string _tempHome;
    private readonly string? _origHome;
    private readonly string? _origUserProfile;
    private readonly string? _origXdgConfigHome;

    public RcFileLoaderTests()
    {
        _tempHome = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempHome);

        _origHome = Environment.GetEnvironmentVariable("HOME");
        _origUserProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        _origXdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");

        // Point HOME and USERPROFILE to the temp dir so SpecialFolder.UserProfile resolves there.
        Environment.SetEnvironmentVariable("HOME", _tempHome);
        Environment.SetEnvironmentVariable("USERPROFILE", _tempHome);
        // Unset XDG_CONFIG_HOME by default; individual tests set it as needed.
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("HOME", _origHome);
        Environment.SetEnvironmentVariable("USERPROFILE", _origUserProfile);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _origXdgConfigHome);

        try { Directory.Delete(_tempHome, recursive: true); } catch { }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Creates a temp file relative to the temp home directory.</summary>
    private string WriteFile(string relativePath, string content)
    {
        string fullPath = Path.Combine(_tempHome, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    private static VirtualMachine MakeVm(StringWriter? stdout = null)
    {
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        vm.Output = stdout ?? TextWriter.Null;
        vm.ErrorOutput = TextWriter.Null;
        vm.EmbeddedMode = true;
        return vm;
    }

    /// <summary>
    /// Captures Console.Error for the duration of <paramref name="action"/>.
    /// Returns the captured text.
    /// </summary>
    private static string CaptureStderr(Action action)
    {
        var originalError = Console.Error;
        var capture = new StringWriter();
        Console.SetError(capture);
        try
        {
            action();
        }
        finally
        {
            Console.SetError(originalError);
        }
        return capture.ToString();
    }

    // ════════════════════════════════════════════════════════════════════════
    // §12.1 Path resolution tests
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void FindRcFile_NoCandidatesExist_ReturnsNull()
    {
        // Temp home is empty; XDG unset.
        string? result = RcFileLoader.FindRcFile();
        Assert.Null(result);
    }

    [Fact]
    public void FindRcFile_OnlyStashrcExists_ReturnsStashrc()
    {
        string expected = WriteFile(".stashrc", "");

        string? result = RcFileLoader.FindRcFile();

        Assert.Equal(expected, result);
    }

    [Fact]
    public void FindRcFile_OnlyConfigInitExists_ReturnsConfigPath()
    {
        string expected = WriteFile(Path.Combine(".config", "stash", "init.stash"), "");

        string? result = RcFileLoader.FindRcFile();

        Assert.Equal(expected, result);
    }

    [Fact]
    public void FindRcFile_OnlyXdgInitExists_ReturnsXdgPath()
    {
        string xdgDir = Path.Combine(_tempHome, "xdg");
        Directory.CreateDirectory(xdgDir);
        string expected = Path.Combine(xdgDir, "stash", "init.stash");
        Directory.CreateDirectory(Path.GetDirectoryName(expected)!);
        File.WriteAllText(expected, "");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", xdgDir);

        string? result = RcFileLoader.FindRcFile();

        Assert.Equal(expected, result);
    }

    [Fact]
    public void FindRcFile_AllExist_PrefersXdg()
    {
        // Create all three candidates.
        WriteFile(".stashrc", "");
        WriteFile(Path.Combine(".config", "stash", "init.stash"), "");

        string xdgDir = Path.Combine(_tempHome, "xdg");
        Directory.CreateDirectory(xdgDir);
        string xdgInit = Path.Combine(xdgDir, "stash", "init.stash");
        Directory.CreateDirectory(Path.GetDirectoryName(xdgInit)!);
        File.WriteAllText(xdgInit, "");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", xdgDir);

        string? result = RcFileLoader.FindRcFile();

        Assert.Equal(xdgInit, result);
    }

    [Fact]
    public void FindRcFile_XdgAndStashrc_PrefersXdg()
    {
        WriteFile(".stashrc", "");

        string xdgDir = Path.Combine(_tempHome, "xdg");
        Directory.CreateDirectory(xdgDir);
        string xdgInit = Path.Combine(xdgDir, "stash", "init.stash");
        Directory.CreateDirectory(Path.GetDirectoryName(xdgInit)!);
        File.WriteAllText(xdgInit, "");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", xdgDir);

        string? result = RcFileLoader.FindRcFile();

        Assert.Equal(xdgInit, result);
    }

    [Fact]
    public void FindRcFile_ConfigAndStashrc_PrefersConfig()
    {
        WriteFile(".stashrc", "");
        string expected = WriteFile(Path.Combine(".config", "stash", "init.stash"), "");

        string? result = RcFileLoader.FindRcFile();

        Assert.Equal(expected, result);
    }

    [Fact]
    public void FindRcFile_XdgEnvSetButFileMissing_FallsThrough()
    {
        // XDG is set but the file doesn't exist there; fallback to ~/.stashrc.
        string xdgDir = Path.Combine(_tempHome, "xdg");
        Directory.CreateDirectory(xdgDir);
        // Do NOT create xdgDir/stash/init.stash
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", xdgDir);

        string expected = WriteFile(".stashrc", "");

        string? result = RcFileLoader.FindRcFile();

        Assert.Equal(expected, result);
    }

    [Fact]
    public void FindRcFile_XdgEnvEmpty_IgnoresXdg()
    {
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", "");
        string expected = WriteFile(".stashrc", "");

        string? result = RcFileLoader.FindRcFile();

        Assert.Equal(expected, result);
    }

    // ════════════════════════════════════════════════════════════════════════
    // §12.2 Load tests
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Load_EmptyFile_NoErrors()
    {
        string rcPath = WriteFile(".stashrc", "");
        var vm = MakeVm();

        string stderr = CaptureStderr(() => RcFileLoader.Load(rcPath, vm, shellEnabled: false));

        Assert.Empty(stderr);
    }

    [Fact]
    public void Load_OnlyComments_NoErrors()
    {
        // Stash uses // for line comments (no RC-specific comment syntax)
        string rcPath = WriteFile(".stashrc", "// this is a comment\n// another comment\n");
        var vm = MakeVm();

        string stderr = CaptureStderr(() => RcFileLoader.Load(rcPath, vm, shellEnabled: false));

        Assert.Empty(stderr);
    }

    [Fact]
    public void Load_StashDeclarationPersists()
    {
        string rcPath = WriteFile(".stashrc", "let x = 42;\n");
        var sw = new StringWriter();
        var vm = MakeVm(sw);

        RcFileLoader.Load(rcPath, vm, shellEnabled: false);
        ShellRunner.EvaluateSource("io.println(x);", vm);

        Assert.Equal("42", sw.ToString().Trim());
    }

    [Fact]
    public void Load_ErrorOnLine_PrintsWarningContinuesLoading()
    {
        // Line 1: valid, Line 2: parse error, Line 3: valid.
        string rcPath = WriteFile(".stashrc", "let a = 1;\nlet b = ;\nlet c = 3;\n");
        var vm = MakeVm();

        string stderr = CaptureStderr(() => RcFileLoader.Load(rcPath, vm, shellEnabled: false));

        // a and c should be defined.
        var swA = new StringWriter();
        vm.Output = swA;
        ShellRunner.EvaluateSource("io.println(a);", vm);
        Assert.Equal("1", swA.ToString().Trim());

        var swC = new StringWriter();
        vm.Output = swC;
        ShellRunner.EvaluateSource("io.println(c);", vm);
        Assert.Equal("3", swC.ToString().Trim());

        // Warning should mention line 2.
        Assert.Contains(":2:", stderr);
    }

    [Fact]
    public void Load_ShellDisabled_TreatsLinesAsStash()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
            !RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return;

        string original = Environment.CurrentDirectory;
        string rcPath = WriteFile(".stashrc", "cd /tmp\n");
        var vm = MakeVm();

        // With shellEnabled=false, "cd /tmp" should fail as Stash (prints warning).
        string stderr = CaptureStderr(() => RcFileLoader.Load(rcPath, vm, shellEnabled: false));

        // The directory should NOT have changed.
        Assert.Equal(original, Environment.CurrentDirectory);
        // A warning should have been printed (cd is not valid Stash).
        Assert.NotEmpty(stderr);
    }

    [Fact]
    public void Load_ShellLineCdAffectsCwd()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
            !RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return;

        string original = Environment.CurrentDirectory;
        string rcPath = WriteFile(".stashrc", "cd /tmp\n");
        var vm = MakeVm();

        try
        {
            RcFileLoader.Load(rcPath, vm, shellEnabled: true);
            Assert.Equal("/tmp", Environment.CurrentDirectory,
                StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.CurrentDirectory = original;
        }
    }

    [Fact]
    public void Load_BackslashContinuation_CoalescesLines()
    {
        // "let x = 1 +\" then "2" should become "let x = 1 + 2"
        string rcPath = WriteFile(".stashrc", "let x = 1 +\\\n2;\n");
        var sw = new StringWriter();
        var vm = MakeVm(sw);

        RcFileLoader.Load(rcPath, vm, shellEnabled: false);
        ShellRunner.EvaluateSource("io.println(x);", vm);

        Assert.Equal("3", sw.ToString().Trim());
    }

    [Fact]
    public void Load_BracketContinuation_CoalescesLines()
    {
        // Physical lines: "let myLen = [" / "1, 2, 3" / "].length;"
        // The unclosed '[' on line 1 causes lines 1-3 to be coalesced into one logical line:
        //   let myLen = [\n1, 2, 3\n].length;
        // If lines were NOT coalesced, "let myLen = [" would be a syntax error → warning.
        // The result is a scalar int (3) which persists across EvaluateSource calls.
        string rcPath = WriteFile(".stashrc", "let myLen = [\n1, 2, 3\n].length;\n");
        var sw = new StringWriter();
        var vm = MakeVm(sw);

        string stderr = CaptureStderr(() => RcFileLoader.Load(rcPath, vm, shellEnabled: false));
        Assert.True(string.IsNullOrEmpty(stderr), $"Load produced unexpected warning: {stderr}");

        ShellRunner.EvaluateSource("io.println(myLen);", vm);

        Assert.Equal("3", sw.ToString().Trim());
    }

    [Fact]
    public void Load_MultipleDeclarations_AllPersist()
    {
        string rcPath = WriteFile(".stashrc", "let a = 10;\nlet b = 20;\nlet c = a + b;\n");
        var sw = new StringWriter();
        var vm = MakeVm(sw);

        RcFileLoader.Load(rcPath, vm, shellEnabled: false);
        ShellRunner.EvaluateSource("io.println(c);", vm);

        Assert.Equal("30", sw.ToString().Trim());
    }

    [Fact]
    public void Load_FileReadError_PrintsWarningAndReturns()
    {
        string nonExistentPath = Path.Combine(_tempHome, "nonexistent_rc.stash");
        var vm = MakeVm();

        // Should not throw, just print a warning.
        string stderr = CaptureStderr(() => RcFileLoader.Load(nonExistentPath, vm, shellEnabled: false));

        Assert.Contains("stash: warning:", stderr);
        Assert.Contains(nonExistentPath, stderr);
    }

    [Fact]
    public void Load_WarningFormat_IncludesPathAndLineNumber()
    {
        string rcPath = WriteFile(".stashrc", "let a = 1;\nbadparse!!!\nlet c = 3;\n");
        var vm = MakeVm();

        string stderr = CaptureStderr(() => RcFileLoader.Load(rcPath, vm, shellEnabled: false));

        // Warning must contain the path and "stash: warning: <path>:<line>:"
        Assert.Contains("stash: warning:", stderr);
        Assert.Contains(rcPath, stderr);
        Assert.Contains(":2:", stderr);
    }
}
