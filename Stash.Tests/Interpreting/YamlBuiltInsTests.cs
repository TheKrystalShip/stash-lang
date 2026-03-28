using System.IO;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Interpreting;
using Stash.Interpreting.Types;

namespace Stash.Tests.Interpreting;

public class YamlBuiltInsTests
{
    private static object? Run(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        interpreter.Interpret(statements);

        var resultLexer = new Lexer("result");
        var resultTokens = resultLexer.ScanTokens();
        var resultParser = new Parser(resultTokens);
        var resultExpr = resultParser.Parse();
        return interpreter.Interpret(resultExpr);
    }

    private static void RunExpectingError(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        Assert.Throws<RuntimeError>(() => interpreter.Interpret(statements));
    }

    // ── yaml.parse ────────────────────────────────────────────────────────

    [Fact]
    public void Parse_BasicMapping()
    {
        var result = Run("""let result = yaml.parse("name: Alice\nage: 30");""");
        var d = Assert.IsType<StashDictionary>(result);
        Assert.Equal("Alice", d.Get("name"));
        Assert.Equal(30L, d.Get("age"));
    }

    [Fact]
    public void Parse_NestedMapping()
    {
        var result = Run("""let result = yaml.parse("server:\n  host: localhost\n  port: 8080");""");
        var d = Assert.IsType<StashDictionary>(result);
        var server = Assert.IsType<StashDictionary>(d.Get("server"));
        Assert.Equal("localhost", server.Get("host"));
        Assert.Equal(8080L, server.Get("port"));
    }

    [Fact]
    public void Parse_Sequence()
    {
        var result = Run("""let result = yaml.parse("- one\n- two\n- three");""");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
        Assert.Equal("one", list[0]);
    }

    [Fact]
    public void Parse_MappingWithArray()
    {
        var result = Run("""let result = yaml.parse("tags:\n  - a\n  - b");""");
        var d = Assert.IsType<StashDictionary>(result);
        var tags = Assert.IsType<List<object?>>(d.Get("tags"));
        Assert.Equal("a", tags[0]);
        Assert.Equal("b", tags[1]);
    }

    [Fact]
    public void Parse_BooleanValues()
    {
        var result = Run("""let result = yaml.parse("enabled: true\nverbose: false");""");
        var d = Assert.IsType<StashDictionary>(result);
        Assert.Equal(true, d.Get("enabled"));
        Assert.Equal(false, d.Get("verbose"));
    }

    [Fact]
    public void Parse_NullValue()
    {
        var result = Run("""let result = yaml.parse("data: null");""");
        var d = Assert.IsType<StashDictionary>(result);
        Assert.Null(d.Get("data"));
    }

    [Fact]
    public void Parse_FloatValue()
    {
        var result = Run("""let result = yaml.parse("ratio: 3.14");""");
        var d = Assert.IsType<StashDictionary>(result);
        var ratio = Assert.IsType<double>(d.Get("ratio"));
        Assert.InRange(ratio, 3.13, 3.15);
    }

    [Fact]
    public void Parse_EmptyString()
    {
        var result = Run("""let result = yaml.parse("");""");
        Assert.Null(result);
    }

    [Fact]
    public void Parse_NonStringThrows()
    {
        RunExpectingError("yaml.parse(42);");
    }

    [Fact]
    public void Parse_InvalidYamlThrows()
    {
        RunExpectingError("""yaml.parse("[invalid: yaml: {{{");""");
    }

    [Fact]
    public void Parse_ArrayOfMappings()
    {
        var result = Run("""let result = yaml.parse("- name: a\n  val: 1\n- name: b\n  val: 2");""");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(2, list.Count);
        var first = Assert.IsType<StashDictionary>(list[0]);
        Assert.Equal("a", first.Get("name"));
        var second = Assert.IsType<StashDictionary>(list[1]);
        Assert.Equal("b", second.Get("name"));
    }

    // ── yaml.stringify ────────────────────────────────────────────────────

    [Fact]
    public void Stringify_Dict()
    {
        var result = Run("""
            let d = dict.new();
            d["name"] = "Alice";
            d["age"] = 30;
            let result = yaml.stringify(d);
            """);
        var str = Assert.IsType<string>(result);
        Assert.Contains("name", str);
        Assert.Contains("Alice", str);
        Assert.Contains("age", str);
    }

    [Fact]
    public void Stringify_NestedDict()
    {
        var result = Run("""
            let inner = dict.new();
            inner["host"] = "localhost";
            let outer = dict.new();
            outer["server"] = inner;
            let result = yaml.stringify(outer);
            """);
        var str = Assert.IsType<string>(result);
        Assert.Contains("server", str);
        Assert.Contains("host", str);
        Assert.Contains("localhost", str);
    }

    [Fact]
    public void Stringify_Array()
    {
        var result = Run("""let result = yaml.stringify([1, 2, 3]);""");
        var str = Assert.IsType<string>(result);
        Assert.Contains("1", str);
        Assert.Contains("2", str);
        Assert.Contains("3", str);
    }

    [Fact]
    public void Stringify_Roundtrip()
    {
        var result = Run("""
            let d = dict.new();
            d["city"] = "Paris";
            d["pop"] = 2100000;
            let serialized = yaml.stringify(d);
            let result = yaml.parse(serialized);
            """);
        var d = Assert.IsType<StashDictionary>(result);
        Assert.Equal("Paris", d.Get("city"));
        Assert.Equal(2100000L, d.Get("pop"));
    }

    [Fact]
    public void Stringify_ScalarTypes()
    {
        var result = Run("""
            let d = dict.new();
            d["count"] = 5;
            d["ratio"] = 3.14;
            d["flag"] = true;
            d["label"] = "hello";
            let result = yaml.stringify(d);
            """);
        var str = Assert.IsType<string>(result);
        Assert.Contains("count", str);
        Assert.Contains("flag", str);
        Assert.Contains("label", str);
        Assert.Contains("hello", str);
    }

    // ── yaml.valid ────────────────────────────────────────────────────────

    [Fact]
    public void Valid_ValidMapping()
    {
        var result = Run("""let result = yaml.valid("name: Alice");""");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Valid_ValidSequence()
    {
        var result = Run("""let result = yaml.valid("- a\n- b");""");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Valid_EmptyString()
    {
        var result = Run("""let result = yaml.valid("");""");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Valid_InvalidYaml()
    {
        var result = Run("""let result = yaml.valid("key: [unterminated");""");
        Assert.Equal(false, result);
    }

    [Fact]
    public void Valid_NonStringThrows()
    {
        RunExpectingError("yaml.valid(42);");
    }

    // ── config namespace (yaml format) ────────────────────────────────────

    [Fact]
    public void ConfigParse_Yaml()
    {
        var result = Run("""let result = config.parse("name: test\nport: 80", "yaml");""");
        var d = Assert.IsType<StashDictionary>(result);
        Assert.Equal("test", d.Get("name"));
        Assert.Equal(80L, d.Get("port"));
    }

    [Fact]
    public void ConfigStringify_Yaml()
    {
        var result = Run("""
            let d = dict.new();
            d["env"] = "production";
            let result = config.stringify(d, "yaml");
            """);
        var str = Assert.IsType<string>(result);
        Assert.Contains("env", str);
        Assert.Contains("production", str);
    }

    [Fact]
    public void ConfigReadWrite_YamlRoundTrip()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".yaml");
        try
        {
            var result = Run($"""
                let d = dict.new();
                d["app"] = "stash";
                d["port"] = 9000;
                config.write("{tempPath}", d);
                let loaded = config.read("{tempPath}");
                let result = loaded;
                """);
            var d = Assert.IsType<StashDictionary>(result);
            Assert.Equal("stash", d.Get("app"));
            Assert.Equal(9000L, d.Get("port"));
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
