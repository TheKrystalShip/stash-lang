using Stash.Analysis;

namespace Stash.Tests.Format;

public class ReexportFormatterTests
{
    private static string Format(string source, int printWidth = 80) =>
        new StashFormatter(new FormatConfig { PrintWidth = printWidth }).Format(source);

    // ─── ExportModuleAsStmt ────────────────────────────────────────────────────

    [Fact]
    public void Format_ExportModuleAs_AlwaysOnOneLine()
    {
        // export <path> as <alias>; is always a single line — no Group needed.
        var result = Format("export \"utils\" as utils;");
        Assert.Equal("export \"utils\" as utils;\n", result);
    }

    [Fact]
    public void Format_ExportModuleAs_IdempotentFormat()
    {
        // Formatting the already-formatted output yields the same result.
        var source = "export \"utils\" as utils;";
        var once = Format(source);
        var twice = Format(once);
        Assert.Equal(once, twice);
    }

    // ─── ExportFromStmt — single-line ─────────────────────────────────────────

    [Fact]
    public void Format_ExportFrom_ShortListStaysOnOneLine()
    {
        // Short list fits within the 80-column print width — stays on one line with spaces.
        var result = Format("export {a,b,c} from \"p\";");
        Assert.Equal("export { a, b, c } from \"p\";\n", result);
    }

    [Fact]
    public void Format_ExportFrom_SingleName_SingleLine()
    {
        var result = Format("export { encode } from \"codec\";");
        Assert.Equal("export { encode } from \"codec\";\n", result);
    }

    [Fact]
    public void Format_ExportFrom_EmptyList_FormatsAsEmptyBraces()
    {
        // An empty list (SA0812 is a semantic warning, not a parse error) formats as `export {} from "p";`.
        var result = Format("export {} from \"p\";");
        Assert.Equal("export {} from \"p\";\n", result);
    }

    [Fact]
    public void Format_ExportFrom_TrailingCommaInSourceCollapsedWhenFits()
    {
        // Source has trailing comma; when it fits on one line the trailing comma is dropped.
        var result = Format("export { a, b, } from \"p\";");
        Assert.Equal("export { a, b } from \"p\";\n", result);
    }

    // ─── ExportFromStmt — multi-line ──────────────────────────────────────────

    [Fact]
    public void Format_ExportFrom_LongListBreaksWithTrailingComma()
    {
        // Reduce print width to force a break.  One name per line, trailing comma on last.
        var formatter = new StashFormatter(new FormatConfig { PrintWidth = 20 });
        var result = formatter.Format("export { diff, VERSION } from \"math\";");
        Assert.Equal("export {\n  diff,\n  VERSION,\n} from \"math\";\n", result);
    }

    [Fact]
    public void Format_ExportFrom_MultiLineInputCollapsesWhenFits()
    {
        // Even if the source is written across multiple lines, the formatter collapses it
        // when the result fits within the print width.
        var source = "export {\n  a,\n  b,\n  c,\n} from \"p\";";
        var result = Format(source);
        Assert.Equal("export { a, b, c } from \"p\";\n", result);
    }

    // ─── Round-trip idempotence ────────────────────────────────────────────────

    [Fact]
    public void Format_ExportFrom_IdempotentSingleLine()
    {
        var source = "export {a,b,c} from \"p\";";
        var once = Format(source);
        var twice = Format(once);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void Format_ExportFrom_IdempotentMultiLine()
    {
        var formatter = new StashFormatter(new FormatConfig { PrintWidth = 20 });
        var source = "export { diff, VERSION } from \"math\";";
        var once = formatter.Format(source);
        var twice = formatter.Format(once);
        Assert.Equal(once, twice);
    }

    // ─── Mixed statements ─────────────────────────────────────────────────────

    [Fact]
    public void Format_MixedImportAndReexport_AllFormattedPredictably()
    {
        // A file containing import, export-as, and export-from all in sequence.
        // The formatter inserts a blank line between import blocks and export declarations
        // (matching the spacing rule for top-level declaration boundaries).
        var source =
            "import { foo } from \"foo\";\n" +
            "export \"bar\" as bar;\n" +
            "export { a, b } from \"baz\";";
        var result = Format(source);
        // export "bar" as bar; and export { a, b } from "baz"; must appear formatted correctly.
        Assert.Contains("import { foo } from \"foo\";", result);
        Assert.Contains("export \"bar\" as bar;", result);
        Assert.Contains("export { a, b } from \"baz\";", result);
        // Idempotence: re-formatting the result must yield the same output.
        Assert.Equal(result, Format(result));
    }
}
