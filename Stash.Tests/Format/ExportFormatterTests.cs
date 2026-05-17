using Stash.Analysis;

namespace Stash.Tests.Format;

public class ExportFormatterTests
{
    private static string Format(string source, int printWidth = 80) =>
        new StashFormatter(new FormatConfig { PrintWidth = printWidth }).Format(source);

    [Fact]
    public void Format_ExportFn_PreservesAndAttachesDocComments()
    {
        // Doc-comment trivia must appear before the export keyword (the formatter attaches it
        // to the whole export statement, which is the natural trivia ordering). Multiple doc
        // comment lines that directly follow each other are kept together without a blank line
        // in between; a blank line naturally separates a standalone comment from declarations
        // at top level, which is the formatter's standard behavior.
        var source = "/// Returns the difference.\n/// @param a first operand\nexport fn diff(a, b) {\n  return a - b;\n}";
        var result = Format(source);
        // Both doc-comment lines must appear in the output before the export keyword.
        Assert.Contains("/// Returns the difference.", result);
        Assert.Contains("/// @param a first operand", result);
        Assert.Contains("export fn diff(a, b) {", result);
        // The doc comments must come before the export declaration.
        int docIdx = result.IndexOf("/// Returns", StringComparison.Ordinal);
        int exportIdx = result.IndexOf("export fn", StringComparison.Ordinal);
        Assert.True(docIdx < exportIdx, "Doc comment must appear before export fn");
    }

    [Fact]
    public void Format_ExportBlock_SingleLineWhenItFits()
    {
        // A short export block fits within the print width and must stay on one line.
        var result = Format("export { diff, VERSION };");
        Assert.Equal("export { diff, VERSION };\n", result);
    }

    [Fact]
    public void Format_ExportBlock_MultiLineWhenItOverflowsPrintWidth()
    {
        // A print width smaller than the inline length forces one name per line
        // with a trailing comma on the last entry.
        var formatter = new StashFormatter(new FormatConfig { PrintWidth = 20 });
        var result = formatter.Format("export { diff, VERSION };");
        Assert.Equal("export {\n  diff,\n  VERSION,\n};\n", result);
    }
}
