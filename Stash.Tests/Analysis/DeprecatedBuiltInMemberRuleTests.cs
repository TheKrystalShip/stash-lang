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
}
