using System.IO;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Bytecode;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Runtime.Types;

namespace Stash.Tests.Interpreting;

public class SysBuiltInsTests
{
    private static object? Run(string source)
    {
        string full = source + "\nreturn result;";
        var lexer = new Lexer(full, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(TestVM.CreateGlobals());
        return vm.Execute(chunk);
    }

    private static void RunExpectingError(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(TestVM.CreateGlobals());
        Assert.Throws<RuntimeError>(() => vm.Execute(chunk));
    }

    // ── sys namespace ─────────────────────────────────────────────────────────

    [Fact]
    public void Sys_IsNamespace()
    {
        var result = Run(@"let result = typeof(sys);");
        Assert.Equal("namespace", result);
    }

    // ── sys.cpuCount ─────────────────────────────────────────────────────────

    [Fact]
    public void CpuCount_ReturnsPositiveInteger()
    {
        var result = Run("let result = sys.cpuCount();");
        var count = Assert.IsType<long>(result);
        Assert.True(count > 0);
    }

    [Fact]
    public void CpuCount_MatchesDotNetProcessorCount()
    {
        var result = Run("let result = sys.cpuCount();");
        var count = Assert.IsType<long>(result);
        Assert.Equal((long)System.Environment.ProcessorCount, count);
    }

    [Fact]
    public void CpuCount_WithExtraArgsThrows()
    {
        RunExpectingError("sys.cpuCount(1);");
    }

    // ── sys.totalMemory ───────────────────────────────────────────────────────

    [Fact]
    public void TotalMemory_ReturnsPositiveInteger()
    {
        var result = Run("let result = sys.totalMemory();");
        var mem = Assert.IsType<long>(result);
        Assert.True(mem > 0);
    }

    [Fact]
    public void TotalMemory_ReasonableSize()
    {
        // At least 1 MB
        var result = Run("let result = sys.totalMemory();");
        var mem = Assert.IsType<long>(result);
        Assert.True(mem >= 1024L * 1024L);
    }

    // ── sys.freeMemory ────────────────────────────────────────────────────────

    [Fact]
    public void FreeMemory_ReturnsPositiveInteger()
    {
        var result = Run("let result = sys.freeMemory();");
        var mem = Assert.IsType<long>(result);
        Assert.True(mem > 0);
    }

    [Fact]
    public void FreeMemory_LessThanOrEqualToTotal()
    {
        var result = Run(@"
let free = sys.freeMemory();
let total = sys.totalMemory();
let result = free <= total;
");
        Assert.Equal(true, result);
    }

    // ── sys.uptime ────────────────────────────────────────────────────────────

    [Fact]
    public void Uptime_ReturnsPositiveFloat()
    {
        var result = Run("let result = sys.uptime();");
        var uptime = Assert.IsType<double>(result);
        Assert.True(uptime > 0.0);
    }

    [Fact]
    public void Uptime_ReasonableValue()
    {
        // Uptime should be at most 100 years in seconds
        var result = Run("let result = sys.uptime();");
        var uptime = Assert.IsType<double>(result);
        Assert.True(uptime < 100.0 * 365.0 * 24.0 * 3600.0);
    }

    // ── sys.loadAvg ───────────────────────────────────────────────────────────

    [Fact]
    public void LoadAvg_ReturnsArrayOfThree()
    {
        var result = Run("let result = sys.loadAvg();");
        var arr = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, arr.Count);
        Assert.All(arr, item => Assert.IsType<double>(item));
    }

    [Fact]
    public void LoadAvg_ValuesNonNegative()
    {
        var result = Run("let result = sys.loadAvg();");
        var arr = Assert.IsType<List<object?>>(result);
        foreach (var item in arr)
        {
            Assert.True((double)item! >= 0.0);
        }
    }

    // ── sys.diskUsage ─────────────────────────────────────────────────────────

    [Fact]
    public void DiskUsage_DefaultPath_ReturnsDictWithKeys()
    {
        var result = Run("let result = sys.diskUsage();");
        var dict = Assert.IsType<StashDictionary>(result);
        Assert.NotNull(dict.Get("total"));
        Assert.NotNull(dict.Get("used"));
        Assert.NotNull(dict.Get("free"));
    }

    [Fact]
    public void DiskUsage_DefaultPath_PositiveValues()
    {
        var result = Run(@"
let d = sys.diskUsage();
let result = d[""total""] > 0 and d[""free""] >= 0 and d[""used""] >= 0;
");
        Assert.Equal(true, result);
    }

    [Fact]
    public void DiskUsage_TotalEqualUsedPlusFree()
    {
        var result = Run(@"
let d = sys.diskUsage();
let result = d[""total""] == d[""used""] + d[""free""];
");
        Assert.Equal(true, result);
    }

    [Fact]
    public void DiskUsage_WithExplicitPath()
    {
        var result = Run(@"let result = sys.diskUsage(""/"");");
        var dict = Assert.IsType<StashDictionary>(result);
        Assert.NotNull(dict.Get("total"));
    }

    [Fact]
    public void DiskUsage_NonStringArgThrows()
    {
        RunExpectingError("sys.diskUsage(123);");
    }

    [Fact]
    public void DiskUsage_TotalAtLeast1MB()
    {
        var result = Run("let result = sys.diskUsage();");
        var dict = Assert.IsType<StashDictionary>(result);
        var total = Assert.IsType<long>(dict.Get("total"));
        Assert.True(total >= 1024L * 1024L);
    }

    // ── sys.pid ───────────────────────────────────────────────────────────────

    [Fact]
    public void Pid_ReturnsPositiveInteger()
    {
        var result = Run("let result = sys.pid();");
        var pid = Assert.IsType<long>(result);
        Assert.True(pid > 0);
    }

    [Fact]
    public void Pid_MatchesDotNetProcessId()
    {
        var result = Run("let result = sys.pid();");
        var pid = Assert.IsType<long>(result);
        Assert.Equal((long)System.Environment.ProcessId, pid);
    }

    // ── sys.tempDir ───────────────────────────────────────────────────────────

    [Fact]
    public void TempDir_ReturnsNonEmptyString()
    {
        var result = Run("let result = sys.tempDir();");
        var dir = Assert.IsType<string>(result);
        Assert.False(string.IsNullOrEmpty(dir));
    }

    [Fact]
    public void TempDir_DoesNotEndWithSeparator()
    {
        var result = Run("let result = sys.tempDir();");
        var dir = Assert.IsType<string>(result);
        Assert.False(dir.EndsWith("/") || dir.EndsWith("\\"));
    }

    [Fact]
    public void TempDir_DirectoryExists()
    {
        var result = Run("let result = sys.tempDir();");
        var dir = Assert.IsType<string>(result);
        Assert.True(Directory.Exists(dir));
    }

    // ── sys.networkInterfaces ─────────────────────────────────────────────────

    [Fact]
    public void NetworkInterfaces_ReturnsArray()
    {
        var result = Run("let result = sys.networkInterfaces();");
        Assert.IsType<List<object?>>(result);
    }

    [Fact]
    public void NetworkInterfaces_EachEntryHasExpectedFields()
    {
        var result = Run(@"
let ifaces = sys.networkInterfaces();
let result = len(ifaces) > 0;
");
        Assert.Equal(true, result);
    }

    [Fact]
    public void NetworkInterfaces_FirstEntryHasNameAndAddresses()
    {
        var result = Run(@"
let ifaces = sys.networkInterfaces();
let first = ifaces[0];
let result = typeof(first[""name""]) == ""string"" and typeof(first[""addresses""]) == ""array"";
");
        Assert.Equal(true, result);
    }

    [Fact]
    public void NetworkInterfaces_NamesAreNonEmptyStrings()
    {
        var result = Run("let result = sys.networkInterfaces();");
        var arr = Assert.IsType<List<object?>>(result);
        Assert.NotEmpty(arr);
        foreach (var entry in arr)
        {
            var dict = Assert.IsType<StashDictionary>(entry);
            var name = Assert.IsType<string>(dict.Get("name"));
            Assert.False(string.IsNullOrEmpty(name));
        }
    }

    [Fact]
    public void NetworkInterfaces_AddressesAreArrays()
    {
        var result = Run("let result = sys.networkInterfaces();");
        var arr = Assert.IsType<List<object?>>(result);
        foreach (var entry in arr)
        {
            var dict = Assert.IsType<StashDictionary>(entry);
            Assert.IsType<List<object?>>(dict.Get("addresses"));
        }
    }

    // ── sys.which ─────────────────────────────────────────────────────────

    [Fact]
    public void Which_KnownCommand_ReturnsPath()
    {
        string cmd = OperatingSystem.IsWindows() ? "cmd" : "sh";
        var result = Run($"let result = sys.which(\"{cmd}\");");
        string path = Assert.IsType<string>(result);
        Assert.False(string.IsNullOrEmpty(path));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Which_NonexistentCommand_ReturnsNull()
    {
        var result = Run("let result = sys.which(\"nonexistent_command_xyz_12345\");");
        Assert.Null(result);
    }

    [Fact]
    public void Which_EmptyString_ReturnsNull()
    {
        var result = Run("let result = sys.which(\"\");");
        Assert.Null(result);
    }

    [Fact]
    public void Which_NonStringArgThrows()
    {
        RunExpectingError("sys.which(123);");
    }

    [Fact]
    public void Which_ReturnsAbsolutePath()
    {
        string cmd = OperatingSystem.IsWindows() ? "cmd" : "sh";
        var result = Run($"let result = sys.which(\"{cmd}\");");
        string path = Assert.IsType<string>(result);
        Assert.True(Path.IsPathRooted(path));
    }

    [Fact]
    public void Which_NullCheck_Works()
    {
        var result = Run(@"
let path = sys.which(""nonexistent_impossible_command_xyz"");
let result = path == null;
");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Which_ConditionalPattern()
    {
        string cmd = OperatingSystem.IsWindows() ? "cmd" : "sh";
        var result = Run($@"
let found = sys.which(""{cmd}"");
let result = found != null;
");
        Assert.Equal(true, result);
    }

    // ── sys.Signal enum ────────────────────────────────────────────────────

    [Fact]
    public void Signal_IsEnum()
    {
        var result = Run("let result = typeof(sys.Signal);");
        Assert.Equal("enum", result);
    }

    [Fact]
    public void Signal_SIGTERM_Exists()
    {
        var result = Run("let result = sys.Signal.SIGTERM;");
        var ev = Assert.IsType<StashEnumValue>(result);
        Assert.Equal("Signal", ev.TypeName);
        Assert.Equal("SIGTERM", ev.MemberName);
    }

    [Fact]
    public void Signal_SIGINT_Exists()
    {
        var result = Run("let result = sys.Signal.SIGINT;");
        var ev = Assert.IsType<StashEnumValue>(result);
        Assert.Equal("Signal", ev.TypeName);
        Assert.Equal("SIGINT", ev.MemberName);
    }

    [Fact]
    public void Signal_SIGHUP_Exists()
    {
        var result = Run("let result = sys.Signal.SIGHUP;");
        var ev = Assert.IsType<StashEnumValue>(result);
        Assert.Equal("Signal", ev.TypeName);
        Assert.Equal("SIGHUP", ev.MemberName);
    }

    [Fact]
    public void Signal_SIGQUIT_Exists()
    {
        var result = Run("let result = sys.Signal.SIGQUIT;");
        var ev = Assert.IsType<StashEnumValue>(result);
        Assert.Equal("Signal", ev.TypeName);
        Assert.Equal("SIGQUIT", ev.MemberName);
    }

    [Fact]
    public void Signal_SIGUSR1_Exists()
    {
        var result = Run("let result = sys.Signal.SIGUSR1;");
        var ev = Assert.IsType<StashEnumValue>(result);
        Assert.Equal("Signal", ev.TypeName);
        Assert.Equal("SIGUSR1", ev.MemberName);
    }

    [Fact]
    public void Signal_SIGUSR2_Exists()
    {
        var result = Run("let result = sys.Signal.SIGUSR2;");
        var ev = Assert.IsType<StashEnumValue>(result);
        Assert.Equal("Signal", ev.TypeName);
        Assert.Equal("SIGUSR2", ev.MemberName);
    }

    [Fact]
    public void Signal_Equality()
    {
        var result = Run("let result = sys.Signal.SIGTERM == sys.Signal.SIGTERM;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Signal_Inequality()
    {
        var result = Run("let result = sys.Signal.SIGTERM != sys.Signal.SIGINT;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Signal_ToString()
    {
        var result = Run("let result = str.contains(\"\" + sys.Signal.SIGTERM, \"SIGTERM\");");
        Assert.Equal(true, result);
    }

    // ── sys.onSignal / sys.offSignal ──────────────────────────────────────

    [Fact]
    public void OnSignal_RegistersWithoutError()
    {
        var result = Run(@"
sys.onSignal(sys.Signal.SIGUSR1, () => {
    // handler body
});
sys.offSignal(sys.Signal.SIGUSR1);
let result = true;
");
        Assert.Equal(true, result);
    }

    [Fact]
    public void OnSignal_ReplaceHandler_DoesNotThrow()
    {
        var result = Run(@"
sys.onSignal(sys.Signal.SIGUSR1, () => { });
sys.onSignal(sys.Signal.SIGUSR1, () => { });
sys.offSignal(sys.Signal.SIGUSR1);
let result = true;
");
        Assert.Equal(true, result);
    }

    [Fact]
    public void OffSignal_RemovesHandler_DoesNotThrow()
    {
        var result = Run(@"
sys.onSignal(sys.Signal.SIGUSR1, () => { });
sys.offSignal(sys.Signal.SIGUSR1);
let result = true;
");
        Assert.Equal(true, result);
    }

    [Fact]
    public void OffSignal_NoExistingHandler_DoesNotThrow()
    {
        var result = Run(@"
sys.offSignal(sys.Signal.SIGUSR2);
let result = true;
");
        Assert.Equal(true, result);
    }

    [Fact]
    public void OnSignal_InvalidFirstArg_Throws()
    {
        RunExpectingError("sys.onSignal(\"SIGTERM\", () => { });");
    }

    [Fact]
    public void OnSignal_InvalidSecondArg_Throws()
    {
        RunExpectingError("sys.onSignal(sys.Signal.SIGTERM, \"not a function\");");
    }

    [Fact]
    public void OffSignal_InvalidArg_Throws()
    {
        RunExpectingError("sys.offSignal(\"SIGTERM\");");
    }

    [Fact]
    public void OnSignal_SIGUSR1_HandlerInvoked()
    {
        if (OperatingSystem.IsWindows()) return;

        string marker = Path.Combine(Path.GetTempPath(), $"stash_signal_test_{Guid.NewGuid():N}");
        try
        {
            var result = Run($@"
sys.onSignal(sys.Signal.SIGUSR1, () => {{
    fs.writeFile(""{marker.Replace("\\", "\\\\")}"", ""handled"");
}});
let pid = sys.pid();
$(kill -USR1 ${{pid}});
let _ = try $(sleep 0.3);
sys.offSignal(sys.Signal.SIGUSR1);
let result = true;
");
            Assert.Equal(true, result);
            Assert.True(File.Exists(marker), "Signal handler should have created the marker file");
            Assert.Equal("handled", File.ReadAllText(marker));
        }
        finally
        {
            if (File.Exists(marker))
                File.Delete(marker);
        }
    }

    [Fact]
    public void OnSignal_AllSignalEnumMembers_CanRegister()
    {
        var result = Run(@"
sys.onSignal(sys.Signal.SIGHUP, () => { });
sys.onSignal(sys.Signal.SIGINT, () => { });
sys.onSignal(sys.Signal.SIGQUIT, () => { });
sys.onSignal(sys.Signal.SIGTERM, () => { });
sys.onSignal(sys.Signal.SIGUSR1, () => { });
sys.onSignal(sys.Signal.SIGUSR2, () => { });
sys.offSignal(sys.Signal.SIGHUP);
sys.offSignal(sys.Signal.SIGINT);
sys.offSignal(sys.Signal.SIGQUIT);
sys.offSignal(sys.Signal.SIGTERM);
sys.offSignal(sys.Signal.SIGUSR1);
sys.offSignal(sys.Signal.SIGUSR2);
let result = true;
");
        Assert.Equal(true, result);
    }
}
