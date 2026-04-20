namespace Stash.Tests.Interpreting;

using System.Collections.Generic;
using Stash.Runtime.Types;

public class CsvBuiltInsTests : TempDirectoryFixture
{
    public CsvBuiltInsTests() : base("stash_csv_test") { }

    private static string Escape(string path) => path.Replace("\\", "\\\\");

    // ── csv.parse — basic ─────────────────────────────────────────────────────

    [Fact]
    public void Parse_SimpleRow_ReturnsArray()
    {
        var result = Run("let result = csv.parse(\"a,b,c\");");
        var rows = Assert.IsType<List<object?>>(Normalize(result));
        Assert.Single(rows);
        var row = Assert.IsType<List<object?>>(rows[0]);
        Assert.Equal(new List<object?> { "a", "b", "c" }, row);
    }

    [Fact]
    public void Parse_MultipleRows_ReturnsArrayOfArrays()
    {
        var result = Run("let result = csv.parse(\"a,b\\n1,2\");");
        var rows = Assert.IsType<List<object?>>(Normalize(result));
        Assert.Equal(2, rows.Count);
        Assert.Equal(new List<object?> { "a", "b" }, rows[0]);
        Assert.Equal(new List<object?> { "1", "2" }, rows[1]);
    }

    [Fact]
    public void Parse_QuotedField_Success()
    {
        var result = Run("let result = csv.parse(\"\\\"hello\\\",world\");");
        var rows = Assert.IsType<List<object?>>(Normalize(result));
        var row = Assert.IsType<List<object?>>(rows[0]);
        Assert.Equal("hello", row[0]);
        Assert.Equal("world", row[1]);
    }

    [Fact]
    public void Parse_QuotedFieldWithComma_Success()
    {
        var result = Run("let result = csv.parse(\"\\\"hello,world\\\",end\");");
        var rows = Assert.IsType<List<object?>>(Normalize(result));
        var row = Assert.IsType<List<object?>>(rows[0]);
        Assert.Equal("hello,world", row[0]);
        Assert.Equal("end", row[1]);
    }

    [Fact]
    public void Parse_QuotedFieldWithNewline_Success()
    {
        var result = Run("let result = csv.parse(\"\\\"line1\\nline2\\\",b\");");
        var rows = Assert.IsType<List<object?>>(Normalize(result));
        Assert.Single(rows);
        var row = Assert.IsType<List<object?>>(rows[0]);
        Assert.Equal("line1\nline2", row[0]);
        Assert.Equal("b", row[1]);
    }

    [Fact]
    public void Parse_EscapedQuote_DoubledQuote()
    {
        // RFC 4180: "" inside a quoted field represents a single "
        // Write CSV to file to avoid multi-level string escaping complexity:
        // File content: "say ""hi""" which parses to the field value: say "hi"
        var csvPath = Path.Combine(TestDir, "quoted.csv");
        File.WriteAllText(csvPath, "\"say \"\"hi\"\"\"\n");

        var result = Run($"let result = csv.parseFile(\"{Escape(csvPath)}\");");
        var rows = Assert.IsType<List<object?>>(Normalize(result));
        var row = Assert.IsType<List<object?>>(rows[0]);
        Assert.Equal("say \"hi\"", row[0]);
    }

    [Fact]
    public void Parse_EmptyField_PreservesEmptyString()
    {
        var result = Run("let result = csv.parse(\"a,,c\");");
        var rows = Assert.IsType<List<object?>>(Normalize(result));
        var row = Assert.IsType<List<object?>>(rows[0]);
        Assert.Equal(3, row.Count);
        Assert.Equal("a", row[0]);
        Assert.Equal("", row[1]);
        Assert.Equal("c", row[2]);
    }

    [Fact]
    public void Parse_EmptyString_ReturnsEmptyArray()
    {
        var result = Run("let result = csv.parse(\"\");");
        var rows = Assert.IsType<List<object?>>(Normalize(result));
        Assert.Empty(rows);
    }

    [Fact]
    public void Parse_TrailingNewline_Ignored()
    {
        var result = Run("let result = csv.parse(\"a,b\\n\");");
        var rows = Assert.IsType<List<object?>>(Normalize(result));
        Assert.Single(rows);
    }

    [Fact]
    public void Parse_CrlfLineEnding_Success()
    {
        var result = Run("let result = csv.parse(\"a,b\\r\\n1,2\");");
        var rows = Assert.IsType<List<object?>>(Normalize(result));
        Assert.Equal(2, rows.Count);
        Assert.Equal(new List<object?> { "a", "b" }, rows[0]);
        Assert.Equal(new List<object?> { "1", "2" }, rows[1]);
    }

    [Fact]
    public void Parse_WithHeader_ReturnsDicts()
    {
        var result = Run(@"
            let opts = csv.CsvOptions { header: true };
            let result = csv.parse(""name,age\nAlice,30\nBob,25"", opts);
        ");
        var rows = Assert.IsType<List<object?>>(Normalize(result));
        Assert.Equal(2, rows.Count);

        var row0 = Assert.IsType<StashDictionary>(rows[0]);
        Assert.Equal("Alice", row0.Get("name").ToObject());
        Assert.Equal("30", row0.Get("age").ToObject());

        var row1 = Assert.IsType<StashDictionary>(rows[1]);
        Assert.Equal("Bob", row1.Get("name").ToObject());
        Assert.Equal("25", row1.Get("age").ToObject());
    }

    [Fact]
    public void Parse_CustomDelimiterTab_Success()
    {
        var result = Run(@"
            let opts = csv.CsvOptions { delimiter: ""\t"" };
            let result = csv.parse(""a\tb\tc"", opts);
        ");
        var rows = Assert.IsType<List<object?>>(Normalize(result));
        var row = Assert.IsType<List<object?>>(rows[0]);
        Assert.Equal(new List<object?> { "a", "b", "c" }, row);
    }

    [Fact]
    public void Parse_CustomDelimiterSemicolon_Success()
    {
        var result = Run(@"
            let opts = csv.CsvOptions { delimiter: "";"" };
            let result = csv.parse(""a;b;c"", opts);
        ");
        var rows = Assert.IsType<List<object?>>(Normalize(result));
        var row = Assert.IsType<List<object?>>(rows[0]);
        Assert.Equal(new List<object?> { "a", "b", "c" }, row);
    }

    [Fact]
    public void Parse_CustomQuoteChar_Success()
    {
        var result = Run(@"
            let opts = csv.CsvOptions { quote: ""'"" };
            let result = csv.parse(""'hello,world',end"", opts);
        ");
        var rows = Assert.IsType<List<object?>>(Normalize(result));
        var row = Assert.IsType<List<object?>>(rows[0]);
        Assert.Equal("hello,world", row[0]);
        Assert.Equal("end", row[1]);
    }

    [Fact]
    public void Parse_ExplicitColumns_ReturnsDictsWithoutConsumingFirstRow()
    {
        var result = Run(@"
            let opts = csv.CsvOptions { columns: [""name"", ""age""] };
            let result = csv.parse(""Alice,30"", opts);
        ");
        var rows = Assert.IsType<List<object?>>(Normalize(result));
        Assert.Single(rows);
        var row = Assert.IsType<StashDictionary>(rows[0]);
        Assert.Equal("Alice", row.Get("name").ToObject());
        Assert.Equal("30", row.Get("age").ToObject());
    }

    [Fact]
    public void Parse_InvalidOptions_DelimiterTooLong_ThrowsError()
    {
        // "ab" is a 2-char delimiter — must throw
        RunExpectingError("let opts = csv.CsvOptions { delimiter: \"ab\" }; csv.parse(\"a,b\", opts);");
    }

    // ── csv.stringify ─────────────────────────────────────────────────────────

    [Fact]
    public void Stringify_SimpleArray_Success()
    {
        var result = Run("let result = csv.stringify([[\"a\", \"b\", \"c\"]]);");
        Assert.Equal("a,b,c", result);
    }

    [Fact]
    public void Stringify_ArrayOfArrays_Success()
    {
        var result = Run("let result = csv.stringify([[\"a\", \"b\"], [\"1\", \"2\"]]);");
        Assert.Equal("a,b\n1,2", result);
    }

    [Fact]
    public void Stringify_ArrayOfDicts_Success()
    {
        var result = Run(@"
            let rows = [{ name: ""Alice"", age: ""30"" }, { name: ""Bob"", age: ""25"" }];
            let result = csv.stringify(rows);
        ");
        Assert.IsType<string>(result);
        var csv = (string)result!;
        Assert.Contains("Alice", csv);
        Assert.Contains("Bob", csv);
    }

    [Fact]
    public void Stringify_WithHeader_IncludesHeader()
    {
        var result = Run(@"
            let opts = csv.CsvOptions { header: true };
            let rows = [{ name: ""Alice"", age: ""30"" }];
            let result = csv.stringify(rows, opts);
        ");
        var csv = Assert.IsType<string>(result);
        var lines = csv.Split('\n');
        Assert.True(lines.Length >= 2);
        Assert.Contains("name", lines[0]);
        Assert.Contains("age", lines[0]);
    }

    [Fact]
    public void Stringify_FieldNeedsQuoting_Quoted()
    {
        var result = Run("let result = csv.stringify([[\"hello,world\"]]);");
        Assert.Equal("\"hello,world\"", result);
    }

    [Fact]
    public void Stringify_FieldWithQuote_Escaped()
    {
        var result = Run("let result = csv.stringify([[\"say \\\"hi\\\"\"]]);");
        var csv = Assert.IsType<string>(result);
        Assert.Contains("\"say \"\"hi\"\"\"", csv);
    }

    [Fact]
    public void Stringify_EmptyArray_ReturnsEmptyString()
    {
        var result = Run("let result = csv.stringify([]);");
        Assert.Equal("", result);
    }

    [Fact]
    public void Stringify_NullValueBecomesEmptyString()
    {
        var result = Run("let result = csv.stringify([[null, \"b\"]]);");
        Assert.Equal(",b", result);
    }

    [Fact]
    public void Stringify_WithExplicitColumns_WritesHeader()
    {
        var result = Run(@"
            let opts = csv.CsvOptions { columns: [""x"", ""y""] };
            let result = csv.stringify([[""1"", ""2""], [""3"", ""4""]], opts);
        ");
        var csv = Assert.IsType<string>(result);
        var lines = csv.Split('\n');
        Assert.Equal("x,y", lines[0]);
        Assert.Equal("1,2", lines[1]);
        Assert.Equal("3,4", lines[2]);
    }

    // ── csv.parseFile ─────────────────────────────────────────────────────────

    [Fact]
    public void ParseFile_Success()
    {
        var csvPath = Path.Combine(TestDir, "data.csv");
        File.WriteAllText(csvPath, "a,b,c\n1,2,3\n");

        var result = Run($"let result = csv.parseFile(\"{Escape(csvPath)}\");");
        var rows = Assert.IsType<List<object?>>(Normalize(result));
        Assert.Equal(2, rows.Count);
        Assert.Equal(new List<object?> { "a", "b", "c" }, rows[0]);
    }

    [Fact]
    public void ParseFile_WithHeader_ReturnsDicts()
    {
        var csvPath = Path.Combine(TestDir, "header.csv");
        File.WriteAllText(csvPath, "name,age\nAlice,30\n");

        var result = Run($@"
            let opts = csv.CsvOptions {{ header: true }};
            let result = csv.parseFile(""{Escape(csvPath)}"", opts);
        ");
        var rows = Assert.IsType<List<object?>>(Normalize(result));
        Assert.Single(rows);
        var row = Assert.IsType<StashDictionary>(rows[0]);
        Assert.Equal("Alice", row.Get("name").ToObject());
    }

    [Fact]
    public void ParseFile_NonExistent_ThrowsError()
    {
        RunExpectingError($"csv.parseFile(\"{Escape(Path.Combine(TestDir, "nonexistent.csv"))}\");");
    }

    // ── csv.writeFile ─────────────────────────────────────────────────────────

    [Fact]
    public void WriteFile_Success()
    {
        var csvPath = Path.Combine(TestDir, "output.csv");

        var result = Run($"let result = csv.writeFile(\"{Escape(csvPath)}\", [[\"a\", \"b\"], [\"1\", \"2\"]]);");
        Assert.Equal(csvPath, result);
        Assert.True(File.Exists(csvPath));

        var content = File.ReadAllText(csvPath);
        Assert.Contains("a,b", content);
        Assert.Contains("1,2", content);
    }

    [Fact]
    public void WriteFile_OverwriteExisting_Success()
    {
        var csvPath = Path.Combine(TestDir, "overwrite.csv");
        File.WriteAllText(csvPath, "old content");

        Run($"let result = csv.writeFile(\"{Escape(csvPath)}\", [[\"new\", \"data\"]]);");
        var content = File.ReadAllText(csvPath);
        Assert.Contains("new,data", content);
        Assert.DoesNotContain("old content", content);
    }

    // ── Roundtrip ─────────────────────────────────────────────────────────────

    [Fact]
    public void Roundtrip_ParseStringify_Identical()
    {
        var original = "name,age\nAlice,30\nBob,25";

        var result = Run($@"
            let parsed = csv.parse(""{original.Replace("\n", "\\n")}"");
            let result = csv.stringify(parsed);
        ");
        Assert.Equal(original, result);
    }

    [Fact]
    public void Roundtrip_QuotedFields_Preserved()
    {
        var csvPath = Path.Combine(TestDir, "roundtrip.csv");
        File.WriteAllText(csvPath, "\"hello, world\",normal\n\"say \"\"hi\"\"\",end\n");

        Run($@"
            let rows = csv.parseFile(""{Escape(csvPath)}"");
            csv.writeFile(""{Escape(Path.Combine(TestDir, "rt_out.csv"))}"", rows);
            let result = rows;
        ");

        var reparsed = Run($"let result = csv.parseFile(\"{Escape(Path.Combine(TestDir, "rt_out.csv"))}\");");
        var rows = Assert.IsType<List<object?>>(Normalize(reparsed));
        Assert.Equal(2, rows.Count);
        var row0 = Assert.IsType<List<object?>>(rows[0]);
        Assert.Equal("hello, world", row0[0]);
        var row1 = Assert.IsType<List<object?>>(rows[1]);
        Assert.Equal("say \"hi\"", row1[0]);
    }
}
