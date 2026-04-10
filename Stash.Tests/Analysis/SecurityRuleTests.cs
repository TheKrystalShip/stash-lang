using System.Linq;
using Stash.Analysis;

namespace Stash.Tests.Analysis;

/// <summary>
/// Tests for SA1301 (hardcoded credentials) and SA1302 (unsafe command interpolation).
/// </summary>
public class SecurityRuleTests : AnalysisTestBase
{
    // ── SA1301 — Hardcoded credentials ───────────────────────────────────────

    [Fact]
    public void SA1301_LetPassword_HardcodedString_EmitsDiagnostic()
    {
        var diagnostics = Validate("let password = \"hunter2\";");
        Assert.Contains(diagnostics, d => d.Code == "SA1301");
    }

    [Fact]
    public void SA1301_ConstApiKey_HardcodedString_EmitsDiagnostic()
    {
        var diagnostics = Validate("const api_key = \"sk-abc123\";");
        Assert.Contains(diagnostics, d => d.Code == "SA1301");
    }

    [Fact]
    public void SA1301_LetSecret_HardcodedString_EmitsDiagnostic()
    {
        var diagnostics = Validate("let secret = \"my-secret-value\";");
        Assert.Contains(diagnostics, d => d.Code == "SA1301");
    }

    [Fact]
    public void SA1301_LetToken_HardcodedString_EmitsDiagnostic()
    {
        var diagnostics = Validate("let token = \"eyJhbGci\";");
        Assert.Contains(diagnostics, d => d.Code == "SA1301");
    }

    [Fact]
    public void SA1301_LetPassword_EmptyString_NoDiagnostic()
    {
        var diagnostics = Validate("let password = \"\";");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA1301");
    }

    [Fact]
    public void SA1301_LetPassword_EnvLookup_NoDiagnostic()
    {
        var diagnostics = Validate("let password = env.get(\"PASSWORD\");");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA1301");
    }

    [Fact]
    public void SA1301_LetUsername_HardcodedString_NoDiagnostic()
    {
        var diagnostics = Validate("let username = \"admin\";");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA1301");
    }

    [Fact]
    public void SA1301_LetPassword_IntegerLiteral_NoDiagnostic()
    {
        var diagnostics = Validate("let password = 1234;");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA1301");
    }

    [Fact]
    public void SA1301_LetPassword_NullLiteral_NoDiagnostic()
    {
        var diagnostics = Validate("let password = null;");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA1301");
    }

    [Fact]
    public void SA1301_MessageContainsVariableName()
    {
        var diagnostics = Validate("let password = \"hunter2\";");
        var diag = Assert.Single(diagnostics, d => d.Code == "SA1301");
        Assert.Contains("password", diag.Message);
    }

    [Fact]
    public void SA1301_ConstPrivateKey_HardcodedString_EmitsDiagnostic()
    {
        var diagnostics = Validate("const private_key = \"-----BEGIN RSA PRIVATE KEY-----\";");
        Assert.Contains(diagnostics, d => d.Code == "SA1301");
    }

    [Fact]
    public void SA1301_LetAuthToken_HardcodedString_EmitsDiagnostic()
    {
        var diagnostics = Validate("let auth_token = \"Bearer abc123\";");
        Assert.Contains(diagnostics, d => d.Code == "SA1301");
    }

    [Fact]
    public void SA1301_CaseInsensitive_LetPASSWORD_EmitsDiagnostic()
    {
        var diagnostics = Validate("let PASSWORD = \"hunter2\";");
        Assert.Contains(diagnostics, d => d.Code == "SA1301");
    }

    // ── SA1302 — Unsafe command interpolation ────────────────────────────────

    [Fact]
    public void SA1302_CommandWithInterpolatedString_EmitsDiagnostic()
    {
        var diagnostics = Validate("let name = \"alice\"; $(echo ${name});");
        Assert.Contains(diagnostics, d => d.Code == "SA1302");
    }

    [Fact]
    public void SA1302_CommandWithEmbeddedInterpolation_EmitsDiagnostic()
    {
        var diagnostics = Validate("let dir = \"/tmp\"; $(rm -rf ${dir});");
        Assert.Contains(diagnostics, d => d.Code == "SA1302");
    }

    [Fact]
    public void SA1302_CommandLiteralOnly_NoDiagnostic()
    {
        var diagnostics = Validate("$(echo \"hello\");");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA1302");
    }

    [Fact]
    public void SA1302_CommandNoInterpolation_NoDiagnostic()
    {
        var diagnostics = Validate("$(ls -la);");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA1302");
    }

    [Fact]
    public void SA1302_StrictCommandWithInterpolation_NoDiagnostic()
    {
        var diagnostics = Validate("let dir = \"/tmp\"; $!(rm -rf ${dir});");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA1302");
    }

    [Fact]
    public void SA1302_CommandInterpolatedStringNoVariables_NoDiagnostic()
    {
        // Command with only literal parts — no injection risk
        var diagnostics = Validate("$(echo hello);");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA1302");
    }

    [Fact]
    public void SA1302_MessageContainsVariableName()
    {
        var diagnostics = Validate("let name = \"alice\"; $(echo ${name});");
        var diag = diagnostics.Single(d => d.Code == "SA1302");
        Assert.Contains("name", diag.Message);
    }
}
