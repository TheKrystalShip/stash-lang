namespace Stash.Tests.Analysis;

using System.Linq;
using Stash.Analysis;

/// <summary>
/// Tests for SA0846 — Call of namespace data member.
/// Verifies that <c>ns.member()</c> where <c>member</c> is a DataMember entry emits SA0846,
/// while plain property reads (<c>ns.member</c>), function calls, and dynamic receivers
/// do not produce false positives.
/// </summary>
public class NamespaceMemberCallDiagnosticTests : AnalysisTestBase
{
    // =========================================================================
    // Positive cases — SA0846 must fire
    // =========================================================================

    [Fact]
    public void DataMember_CalledWithParens_EmitsSA0846()
    {
        // log.level is a DataMember; calling it like a function is an error.
        var diagnostics = Validate("let x = log.level();");
        Assert.Contains(diagnostics, d => d.Code == "SA0846");
    }

    [Fact]
    public void DataMember_CalledWithParens_MessageNamesTheMember()
    {
        var diagnostics = Validate("log.level();");
        var d = diagnostics.FirstOrDefault(d => d.Code == "SA0846");
        Assert.NotNull(d);
        Assert.Contains("log.level", d!.Message);
    }

    [Fact]
    public void DataMember_CalledWithParens_MessageSaysValueMember()
    {
        var diagnostics = Validate("log.level();");
        var d = diagnostics.FirstOrDefault(d => d.Code == "SA0846");
        Assert.NotNull(d);
        Assert.Contains("value member", d!.Message);
    }

    [Fact]
    public void DataMember_CalledWithExtraArgs_EmitsSA0846()
    {
        // Call-of-DataMember is rejected regardless of argument count.
        var diagnostics = Validate("""log.level("extra");""");
        Assert.Contains(diagnostics, d => d.Code == "SA0846");
    }

    [Fact]
    public void SA0846_Severity_IsError()
    {
        var diagnostics = Validate("log.level();");
        var d = diagnostics.FirstOrDefault(d => d.Code == "SA0846");
        Assert.NotNull(d);
        Assert.Equal(DiagnosticLevel.Error, d!.Level);
    }

    // =========================================================================
    // Negative cases — SA0846 must NOT fire
    // =========================================================================

    [Fact]
    public void DataMember_BareRead_DoesNotEmitSA0846()
    {
        // Reading without parentheses is the correct usage.
        var diagnostics = Validate("let x = log.level;");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0846");
    }

    [Fact]
    public void FunctionMember_Called_DoesNotEmitSA0846()
    {
        // log.debug is a function; calling it should not emit SA0846.
        var diagnostics = Validate("""log.debug("hello");""");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0846");
    }

    [Fact]
    public void DynamicReceiver_DataMemberRead_DoesNotEmitSA0846()
    {
        // Dynamic receiver: the static rule cannot fire — only the runtime path is used.
        var diagnostics = Validate("let ns = log; let x = ns.level;");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0846");
    }

    [Fact]
    public void UnknownNamespace_CalledWithParens_DoesNotEmitSA0846()
    {
        // Unknown namespace → rule bails out early; no false positive.
        var diagnostics = Validate("let x = foo.bar();");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0846");
    }

    [Fact]
    public void FunctionReference_BareRead_DoesNotEmitSA0846()
    {
        // Reading a namespace function reference without calling is fine.
        var diagnostics = Validate("let f = io.println;");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0846");
    }
}
