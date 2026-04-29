using System.Linq;
using Stash.Analysis;
using Xunit;

namespace Stash.Tests.Analysis;

/// <summary>
/// Tests for SA0820: Unquoted glob pattern in command literal.
/// </summary>
public class CommandAnalysisTests : AnalysisTestBase
{
    [Fact]
    public void SA0820_UnquotedStarInCommand_Emitted()
    {
        var diagnostics = Validate("$(echo *.txt);");
        Assert.Contains(diagnostics, d => d.Code == "SA0820");
    }

    [Fact]
    public void SA0820_QuotedStar_NotEmitted()
    {
        var diagnostics = Validate("$(echo \"*.txt\");");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0820");
    }

    [Fact]
    public void SA0820_SingleQuotedStar_NotEmitted()
    {
        var diagnostics = Validate("$(echo '*.txt');");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0820");
    }

    [Fact]
    public void SA0820_PlainCommand_NotEmitted()
    {
        var diagnostics = Validate("$(echo hello);");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0820");
    }

    [Fact]
    public void SA0820_QuestionMarkAndBracket_AlsoTriggers()
    {
        var diags1 = Validate("$(ls ?.txt);");
        Assert.Contains(diags1, d => d.Code == "SA0820");

        var diags2 = Validate("$(ls [abc].txt);");
        Assert.Contains(diags2, d => d.Code == "SA0820");
    }

    [Fact]
    public void SA0820_OnlyInLiteralParts_NotInterpolations()
    {
        // The name variable might hold "*.txt" at runtime but we can't know statically
        var diagnostics = Validate("let name = \"*.txt\"; $(echo ${name});");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0820");
    }

    [Fact]
    public void SA0820_MessageIncludesPattern()
    {
        var diagnostics = Validate("$(echo *.log);");
        var diag = diagnostics.FirstOrDefault(d => d.Code == "SA0820");
        Assert.NotNull(diag);
        Assert.Contains("*.log", diag.Message);
    }
}
