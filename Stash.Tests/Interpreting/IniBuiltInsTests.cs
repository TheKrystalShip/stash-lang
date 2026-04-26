using Stash.Runtime.Types;

namespace Stash.Tests.Interpreting;

public class IniBuiltInsTests : TempDirectoryFixture
{
    public IniBuiltInsTests() : base("stash_ini_test") { }

    // ── ini.parse ─────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_BasicKeyValue_ReturnsDict()
    {
        var result = Run("let d = ini.parse(\"key=value\"); let result = d.key;");
        Assert.Equal("value", result);
    }

    [Fact]
    public void Parse_WithSection_ReturnsSectionAsNestedDict()
    {
        var result = Run("let d = ini.parse(\"[section]\\nkey=val\"); let result = d.section.key;");
        Assert.Equal("val", result);
    }

    [Fact]
    public void Parse_CommentWithHash_IsIgnored()
    {
        var result = Run("let d = ini.parse(\"# comment\\nkey=value\"); let result = d.key;");
        Assert.Equal("value", result);
    }

    [Fact]
    public void Parse_CommentWithSemicolon_IsIgnored()
    {
        var result = Run("let d = ini.parse(\"; comment\\nkey=value\"); let result = d.key;");
        Assert.Equal("value", result);
    }

    [Fact]
    public void Parse_EmptyValue_ReturnsEmptyString()
    {
        var result = Run("let d = ini.parse(\"key=\"); let result = d.key;");
        Assert.Equal("", result);
    }

    [Fact]
    public void Parse_IntegerValue_ReturnsInt()
    {
        var result = Run("let d = ini.parse(\"num=42\"); let result = d.num;");
        Assert.Equal(42L, result);
    }

    [Fact]
    public void Parse_FloatValue_ReturnsFloat()
    {
        var result = Run("let d = ini.parse(\"pi=3.14\"); let result = d.pi;");
        Assert.Equal(3.14, result);
    }

    [Fact]
    public void Parse_BooleanValue_ReturnsBool()
    {
        var result = Run("let d = ini.parse(\"flag=true\"); let result = d.flag;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Parse_MultipleKeys_ReturnsAllKeys()
    {
        var result = Run("let d = ini.parse(\"a=1\\nb=2\\nc=3\"); let result = d.a + d.b + d.c;");
        Assert.Equal(6L, result);
    }

    [Fact]
    public void Parse_DuplicateKeys_LastWins()
    {
        var result = Run("let d = ini.parse(\"key=first\\nkey=last\"); let result = d.key;");
        Assert.Equal("last", result);
    }

    [Fact]
    public void Parse_NoSection_ReturnsTopLevelKeys()
    {
        var result = Run("let d = ini.parse(\"host=localhost\\nport=3306\"); let result = d.host;");
        Assert.Equal("localhost", result);
    }

    [Fact]
    public void Parse_EmptyString_ReturnsEmptyDict()
    {
        var result = Run("let d = ini.parse(\"\"); let result = len(d);");
        Assert.Equal(0L, result);
    }

    [Fact]
    public void Parse_UnicodeValues_Handled()
    {
        var result = Run("let d = ini.parse(\"greeting=héllo wörld\"); let result = d.greeting;");
        Assert.Equal("héllo wörld", result);
    }

    [Fact]
    public void Parse_MultipleSections_ReturnsSeparateNestedDicts()
    {
        var result = Run("let d = ini.parse(\"[a]\\nx=1\\n[b]\\nx=2\"); let result = d.a.x + d.b.x;");
        Assert.Equal(3L, result);
    }

    [Fact]
    public void Parse_SectionThenGlobal_GlobalBelongsToSection()
    {
        // Keys after a section header belong to that section
        var result = Run("let d = ini.parse(\"[db]\\nhost=localhost\"); let result = d.db.host;");
        Assert.Equal("localhost", result);
    }

    // ── ini.stringify ─────────────────────────────────────────────────────────

    [Fact]
    public void Stringify_SimpleDict_ReturnsKeyValueLines()
    {
        var result = Run("let result = ini.stringify({key: \"value\"});");
        Assert.IsType<string>(result);
        Assert.Contains("key = value", (string)result!);
    }

    [Fact]
    public void Stringify_NestedDict_ProducesSectionHeader()
    {
        var result = Run("let result = ini.stringify({section: {key: \"value\"}});");
        Assert.IsType<string>(result);
        Assert.Contains("[section]", (string)result!);
        Assert.Contains("key = value", (string)result!);
    }

    [Fact]
    public void Stringify_RoundTrip_ParseThenStringify()
    {
        var result = Run(
            "let original = {host: \"localhost\", port: 3306};" +
            "let text = ini.stringify(original);" +
            "let parsed = ini.parse(text);" +
            "let result = parsed.host;");
        Assert.Equal("localhost", result);
    }

    [Fact]
    public void Stringify_EmptyDict_ReturnsEmptyString()
    {
        var result = Run("let result = ini.stringify({});");
        Assert.Equal("", result);
    }

    [Fact]
    public void Stringify_IntValue_PreservesInteger()
    {
        var result = Run("let result = ini.stringify({count: 99});");
        Assert.IsType<string>(result);
        Assert.Contains("count = 99", (string)result!);
    }

    [Fact]
    public void Stringify_BoolValue_WritesLiteralTrueFalse()
    {
        var result = Run("let result = ini.stringify({enabled: true, disabled: false});");
        Assert.IsType<string>(result);
        Assert.Contains("enabled = true", (string)result!);
        Assert.Contains("disabled = false", (string)result!);
    }

    [Fact]
    public void Stringify_Null_ThrowsError()
    {
        RunExpectingError("ini.stringify(null);");
    }

    // ── ini file I/O (via fs namespace) ───────────────────────────────────────

    [Fact]
    public void ParseAfterFsWrite_ValidIniContent_ReturnsDict()
    {
        var filePath = Path.Combine(TestDir, "config.ini");
        RunStatements($"fs.writeFile(\"{filePath}\", \"[db]\\nhost=localhost\\nport=5432\");");
        var result = Run($"let d = ini.parse(fs.readFile(\"{filePath}\")); let result = d.db.host;");
        Assert.Equal("localhost", result);
    }

    [Fact]
    public void StringifyAndWrite_CreatesFile()
    {
        var filePath = Path.Combine(TestDir, "out.ini");
        RunStatements($"let text = ini.stringify({{host: \"server\", port: 8080}}); fs.writeFile(\"{filePath}\", text);");
        Assert.True(File.Exists(filePath));
        var contents = File.ReadAllText(filePath);
        Assert.Contains("host = server", contents);
    }

    [Fact]
    public void StringifyParseRoundTrip_WithFile_PreservesValues()
    {
        var filePath = Path.Combine(TestDir, "roundtrip.ini");
        var result = Run(
            $"let d = {{name: \"test\", value: 42}};" +
            $"let text = ini.stringify(d);" +
            $"fs.writeFile(\"{filePath}\", text);" +
            $"let d2 = ini.parse(fs.readFile(\"{filePath}\"));" +
            $"let result = d2.name;");
        Assert.Equal("test", result);
    }

    [Fact]
    public void FsReadFile_MissingFile_ThrowsError()
    {
        var filePath = Path.Combine(TestDir, "nonexistent.ini");
        RunExpectingError($"fs.readFile(\"{filePath}\");");
    }
}
