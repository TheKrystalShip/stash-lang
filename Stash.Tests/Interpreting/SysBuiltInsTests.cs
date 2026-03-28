using Stash.Lexing;
using Stash.Parsing;
using Stash.Interpreting;
using Stash.Runtime;
using Stash.Runtime.Types;

namespace Stash.Tests.Interpreting;

public class SysBuiltInsTests
{
    private static object? Run(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        interpreter.Interpret(statements);

        var resultLexer = new Lexer("result");
        var resultTokens = resultLexer.ScanTokens();
        var resultParser = new Parser(resultTokens);
        var resultExpr = resultParser.Parse();
        return interpreter.Interpret(resultExpr);
    }

    private static void RunExpectingError(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        Assert.Throws<RuntimeError>(() => interpreter.Interpret(statements));
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
}
