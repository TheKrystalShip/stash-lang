using System.Linq;
using Stash.Analysis;

namespace Stash.Tests.Analysis;

/// <summary>
/// Tests for SA0830 — Deprecated built-in member.
/// </summary>
public class DeprecatedBuiltInMemberRuleTests : AnalysisTestBase
{
    [Fact]
    public void Process_Chdir_EmitsSA0830()
    {
        var diagnostics = Validate("process.chdir(\"/tmp\");");
        Assert.Contains(diagnostics, d => d.Code == "SA0830");
    }

    [Fact]
    public void Process_Exit_EmitsSA0830()
    {
        var diagnostics = Validate("process.exit(0);");
        Assert.Contains(diagnostics, d => d.Code == "SA0830");
    }

    [Fact]
    public void Process_LastExitCode_EmitsSA0830()
    {
        var diagnostics = Validate("let code = process.lastExitCode();");
        Assert.Contains(diagnostics, d => d.Code == "SA0830");
    }

    [Fact]
    public void Process_Sigterm_EmitsSA0830_WithSignalTermReplacement()
    {
        var diagnostics = Validate("let sig = process.SIGTERM;");
        var d = diagnostics.FirstOrDefault(d => d.Code == "SA0830");
        Assert.NotNull(d);
        Assert.Contains("Signal.Term", d!.Message);
    }

    [Fact]
    public void Env_Chdir_DoesNotEmitSA0830()
    {
        var diagnostics = Validate("env.chdir(\"/tmp\");");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0830");
    }

    [Fact]
    public void Process_Spawn_DoesNotEmitSA0830()
    {
        var diagnostics = Validate("process.spawn(\"ls\");");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0830");
    }

    [Fact]
    public void SA0830_MessageContainsBothOldAndNewNames()
    {
        var diagnostics = Validate("process.chdir(\"/tmp\");");
        var d = diagnostics.FirstOrDefault(d => d.Code == "SA0830");
        Assert.NotNull(d);
        Assert.Contains("process.chdir", d!.Message);
        Assert.Contains("env.chdir", d.Message);
    }

    [Fact]
    public void SA0830_IsDeprecatedFlag_IsSet()
    {
        var diagnostics = Validate("process.exit(1);");
        var d = diagnostics.FirstOrDefault(d => d.Code == "SA0830");
        Assert.NotNull(d);
        Assert.True(d!.IsDeprecated);
    }

    [Fact]
    public void Process_PopDir_EmitsSA0830()
    {
        var diagnostics = Validate("process.popDir();");
        Assert.Contains(diagnostics, d => d.Code == "SA0830");
    }

    [Fact]
    public void Process_DirStack_EmitsSA0830()
    {
        var diagnostics = Validate("let s = process.dirStack();");
        Assert.Contains(diagnostics, d => d.Code == "SA0830");
    }

    [Fact]
    public void Process_DirStackDepth_EmitsSA0830()
    {
        var diagnostics = Validate("let n = process.dirStackDepth();");
        Assert.Contains(diagnostics, d => d.Code == "SA0830");
    }

    [Fact]
    public void Process_WithDir_EmitsSA0830()
    {
        var diagnostics = Validate("process.withDir(\"/tmp\", () => null);");
        Assert.Contains(diagnostics, d => d.Code == "SA0830");
    }

    [Fact]
    public void Process_SIGHUP_EmitsSA0830_WithSignalHupReplacement()
    {
        var diagnostics = Validate("let sig = process.SIGHUP;");
        var d = diagnostics.FirstOrDefault(d => d.Code == "SA0830");
        Assert.NotNull(d);
        Assert.Contains("Signal.Hup", d!.Message);
    }

    [Fact]
    public void Process_SIGUSR1_EmitsSA0830_WithSignalUsr1Replacement()
    {
        var diagnostics = Validate("let sig = process.SIGUSR1;");
        var d = diagnostics.FirstOrDefault(d => d.Code == "SA0830");
        Assert.NotNull(d);
        Assert.Contains("Signal.Usr1", d!.Message);
    }

    [Fact]
    public void SA0830_SeverityIsWarning()
    {
        var diagnostics = Validate("process.exit(0);");
        var d = diagnostics.FirstOrDefault(d => d.Code == "SA0830");
        Assert.NotNull(d);
        Assert.Equal(DiagnosticLevel.Warning, d!.Level);
    }

    // =========================================================================
    // Stdlib Namespace Audit — Tier 1: str.* → re.* deprecations
    // =========================================================================

    [Fact]
    public void StrMatch_EmitsSA0830_MentioningReMatch()
    {
        var diagnostics = Validate("""str.match("hello", "\\d+");""");
        var d = diagnostics.FirstOrDefault(d => d.Code == "SA0830");
        Assert.NotNull(d);
        Assert.Contains("str.match", d!.Message);
        Assert.Contains("re.match", d.Message);
    }

    [Fact]
    public void StrReplaceRegex_EmitsSA0830_MentioningReReplace()
    {
        var diagnostics = Validate("""str.replaceRegex("hello", "\\d+", "_");""");
        var d = diagnostics.FirstOrDefault(d => d.Code == "SA0830");
        Assert.NotNull(d);
        Assert.Contains("str.replaceRegex", d!.Message);
        Assert.Contains("re.replace", d.Message);
    }

    [Fact]
    public void StrIsMatch_EmitsSA0830_MentioningReTest()
    {
        var diagnostics = Validate("""str.isMatch("hello", "\\d+");""");
        var d = diagnostics.FirstOrDefault(d => d.Code == "SA0830");
        Assert.NotNull(d);
        Assert.Contains("str.isMatch", d!.Message);
        Assert.Contains("re.test", d.Message);
    }

    // =========================================================================
    // Stdlib Namespace Audit — Tier 1: net.* → tcp/ws/dns deprecations
    // =========================================================================

    [Fact]
    public void NetTcpConnect_EmitsSA0830_MentioningTcpConnect()
    {
        var diagnostics = Validate("""net.tcpConnect("localhost", 80);""");
        var d = diagnostics.FirstOrDefault(d => d.Code == "SA0830");
        Assert.NotNull(d);
        Assert.Contains("net.tcpConnect", d!.Message);
        Assert.Contains("tcp.connect", d.Message);
    }

    [Fact]
    public void NetWsConnect_EmitsSA0830_MentioningWsConnect()
    {
        var diagnostics = Validate("""net.wsConnect("ws://localhost");""");
        var d = diagnostics.FirstOrDefault(d => d.Code == "SA0830");
        Assert.NotNull(d);
        Assert.Contains("net.wsConnect", d!.Message);
        Assert.Contains("ws.connect", d.Message);
    }

    [Fact]
    public void NetResolve_EmitsSA0830_MentioningDnsResolve()
    {
        var diagnostics = Validate("""net.resolve("example.com");""");
        var d = diagnostics.FirstOrDefault(d => d.Code == "SA0830");
        Assert.NotNull(d);
        Assert.Contains("net.resolve", d!.Message);
        Assert.Contains("dns.resolve", d.Message);
    }

    // =========================================================================
    // Stdlib Namespace Audit — Tier 1: sys.* → signal.* deprecations
    // =========================================================================

    [Fact]
    public void SysOnSignal_EmitsSA0830_MentioningSignalOn()
    {
        var diagnostics = Validate("sys.onSignal(Signal.Term, () => null);");
        var d = diagnostics.FirstOrDefault(d => d.Code == "SA0830");
        Assert.NotNull(d);
        Assert.Contains("sys.onSignal", d!.Message);
        Assert.Contains("signal.on", d.Message);
    }

    [Fact]
    public void SysOffSignal_EmitsSA0830_MentioningSignalOff()
    {
        var diagnostics = Validate("sys.offSignal(Signal.Term);");
        var d = diagnostics.FirstOrDefault(d => d.Code == "SA0830");
        Assert.NotNull(d);
        Assert.Contains("sys.offSignal", d!.Message);
        Assert.Contains("signal.off", d.Message);
    }

    // =========================================================================
    // Stdlib Namespace Audit — Tier 2: arr.new / conv.charCode renames
    // =========================================================================

    [Fact]
    public void ArrNew_EmitsSA0830_MentioningArrCreate()
    {
        var diagnostics = Validate("""arr.new("int", 5);""");
        var d = diagnostics.FirstOrDefault(d => d.Code == "SA0830");
        Assert.NotNull(d);
        Assert.Contains("arr.new", d!.Message);
        Assert.Contains("arr.create", d.Message);
    }

    [Fact]
    public void ConvCharCode_EmitsSA0830_MentioningStrCharCode()
    {
        var diagnostics = Validate("""conv.charCode("A");""");
        var d = diagnostics.FirstOrDefault(d => d.Code == "SA0830");
        Assert.NotNull(d);
        Assert.Contains("conv.charCode", d!.Message);
        Assert.Contains("str.charCode", d.Message);
    }

    // =========================================================================
    // Negative: new canonical names must NOT emit SA0830
    // =========================================================================

    [Fact]
    public void ReMatch_CanonicalName_DoesNotEmitSA0830()
    {
        var diagnostics = Validate("""re.match("hello", "\\d+");""");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0830");
    }
}
