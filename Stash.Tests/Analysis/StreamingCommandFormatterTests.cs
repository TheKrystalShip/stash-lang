using Stash.Analysis;

namespace Stash.Tests.Analysis;

public class StreamingCommandFormatterTests
{
    private static string Format(string source, int indentSize = 2) =>
        new StashFormatter(indentSize, useTabs: false).Format(source);

    [Fact]
    public void Format_StreamingCommand_RoundTripsExactly()
    {
        var result = Format("let s = $<(echo hi);");
        Assert.Contains("$<(echo hi)", result);
    }

    [Fact]
    public void Format_StrictStreamingCommand_RoundTripsExactly()
    {
        var result = Format("let s = $!<(echo hi);");
        Assert.Contains("$!<(echo hi)", result);
    }
}
