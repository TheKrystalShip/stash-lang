using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Stash.Bytecode;
using Stash.Cli.Shell;
using Stash.Runtime;
using Stash.Stdlib;

namespace Stash.Tests.Cli;

/// <summary>
/// Integration tests for shell-mode redirect execution (Phase 5, §5.6).
/// All tests spawn real POSIX processes and are skipped on Windows.
/// </summary>
public class ShellRedirectTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ShellRunner MakeRunner()
    {
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        var ctx = new ShellContext
        {
            Vm = vm,
            PathCache = new PathExecutableCache(_ => true),
            Keywords = ShellContext.BuildKeywordSet(),
            Namespaces = new HashSet<string>(StdlibRegistry.NamespaceNames, StringComparer.Ordinal),
            ShellBuiltinNames = ShellContext.BuildShellBuiltinSet(),
        };
        return new ShellRunner(ctx);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Redirect_StdoutTruncate_WritesFile()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        string tmp = Path.GetTempFileName();
        try
        {
            MakeRunner().Run($"echo hi > {tmp}");
            Assert.Equal("hi\n", File.ReadAllText(tmp));
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Redirect_StdoutAppend_AppendsToFile()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        string tmp = Path.GetTempFileName();
        try
        {
            var runner = MakeRunner();
            runner.Run($"echo line1 >> {tmp}");
            runner.Run($"echo line2 >> {tmp}");
            string content = File.ReadAllText(tmp);
            Assert.Contains("line1", content, StringComparison.Ordinal);
            Assert.Contains("line2", content, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Redirect_StderrSeparately()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        string outFile = Path.GetTempFileName();
        string errFile = Path.GetTempFileName();
        try
        {
            // sh -c 'echo out; echo err >&2'
            MakeRunner().Run($"sh -c 'echo out; echo err >&2' > {outFile} 2> {errFile}");
            Assert.Contains("out", File.ReadAllText(outFile), StringComparison.Ordinal);
            Assert.Contains("err", File.ReadAllText(errFile), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(outFile);
            File.Delete(errFile);
        }
    }

    [Fact]
    public void Redirect_BothStreams()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        string tmp = Path.GetTempFileName();
        try
        {
            // &> redirects both stdout and stderr into the same file.
            MakeRunner().Run($"sh -c 'echo out; echo err >&2' &> {tmp}");
            string content = File.ReadAllText(tmp);
            Assert.Contains("out", content, StringComparison.Ordinal);
            Assert.Contains("err", content, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Redirect_BothStreamsAppend()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        string tmp = Path.GetTempFileName();
        try
        {
            var runner = MakeRunner();
            runner.Run($"echo first &>> {tmp}");
            runner.Run($"echo second &>> {tmp}");
            string content = File.ReadAllText(tmp);
            Assert.Contains("first", content, StringComparison.Ordinal);
            Assert.Contains("second", content, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Redirect_FileTilde_ExpandsHome()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string target = Path.Combine(home, "test_redirect_phase5.txt");
        try
        {
            MakeRunner().Run("echo hello > ~/test_redirect_phase5.txt");
            Assert.True(File.Exists(target));
            Assert.Contains("hello", File.ReadAllText(target), StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(target)) File.Delete(target);
        }
    }

    [Fact]
    public void Redirect_PipelineLastStage_OnlyApplies()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        string tmp = Path.GetTempFileName();
        try
        {
            // echo a | wc -l > tmp: the word count of "a\n" is 1.
            MakeRunner().Run($"echo a | wc -l > {tmp}");
            string content = File.ReadAllText(tmp).Trim();
            Assert.Equal("1", content);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Redirect_NonExistentDir_Errors()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        var runner = MakeRunner();
        var ex = Assert.Throws<RuntimeError>(() =>
            runner.Run("echo x > /no/such/dir/file.txt"));
        Assert.Equal(StashErrorTypes.CommandError, ex.ErrorType);
    }
}
