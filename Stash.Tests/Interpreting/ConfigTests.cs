using System;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Interpreting;
using Stash.Interpreting.Types;

namespace Stash.Tests.Interpreting;

public class ConfigTests
{
    private static object? Eval(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var expr = parser.Parse();
        var interpreter = new Interpreter();
        return interpreter.Interpret(expr);
    }

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

    // ===== Category 1: Dictionary Dot Access =====

    [Fact]
    public void DictDotAccess_ReadsExistingKey()
    {
        var result = Run("""
            let d = dict.new();
            d["name"] = "Alice";
            let result = d.name;
            """);
        Assert.Equal("Alice", result);
    }

    [Fact]
    public void DictDotAccess_ReturnsNullForMissingKey()
    {
        var result = Run("""
            let d = dict.new();
            let result = d.missing;
            """);
        Assert.Null(result);
    }

    [Fact]
    public void DictDotAssign_SetsField()
    {
        var result = Run("""
            let d = dict.new();
            d.name = "Bob";
            let result = d["name"];
            """);
        Assert.Equal("Bob", result);
    }

    [Fact]
    public void DictDotAssign_OverwritesExistingField()
    {
        var result = Run("""
            let d = dict.new();
            d["x"] = 1;
            d.x = 2;
            let result = d.x;
            """);
        Assert.Equal(2L, result);
    }

    [Fact]
    public void DictDotAccess_NestedDicts()
    {
        var result = Run("""
            let d = dict.new();
            let inner = dict.new();
            inner["value"] = 42;
            d["nested"] = inner;
            let result = d.nested.value;
            """);
        Assert.Equal(42L, result);
    }

    [Fact]
    public void DictDotAccess_WorksWithJsonParse()
    {
        var result = Run("""
            let d = json.parse("{\"name\": \"Alice\", \"age\": 30}");
            let result = d.name;
            """);
        Assert.Equal("Alice", result);
    }

    [Fact]
    public void DictDotAccess_NestedJsonParse()
    {
        var result = Run("""
            let d = json.parse("{\"user\": {\"name\": \"Bob\"}}");
            let result = d.user.name;
            """);
        Assert.Equal("Bob", result);
    }

    [Fact]
    public void DictDotAssign_UpdatesNestedJsonParse()
    {
        var result = Run("""
            let d = json.parse("{\"user\": {\"name\": \"Bob\"}}");
            d.user.name = "Alice";
            let result = d.user.name;
            """);
        Assert.Equal("Alice", result);
    }

    // ===== Category 2: INI Parsing (ini.parse) =====

    [Fact]
    public void IniParse_BasicSections()
    {
        var result = Run("""
            let text = "[owner]\nname = John Doe\norganization = Acme\n\n[database]\nserver = 192.0.2.62\nport = 143";
            let cfg = ini.parse(text);
            let result = cfg.owner.name;
            """);
        Assert.Equal("John Doe", result);
    }

    [Fact]
    public void IniParse_NumberCoercion()
    {
        var result = Run("""
            let cfg = ini.parse("[db]\nport = 143");
            let result = cfg.db.port;
            """);
        Assert.Equal(143L, result);
    }

    [Fact]
    public void IniParse_BoolCoercion()
    {
        var result = Run("""
            let cfg = ini.parse("[settings]\nenabled = true\nverbose = false");
            let result = cfg.settings.enabled;
            """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void IniParse_QuotedString()
    {
        var result = Run("""
            let cfg = ini.parse("[db]\npath = \"payroll.dat\"");
            let result = cfg.db.path;
            """);
        Assert.Equal("payroll.dat", result);
    }

    [Fact]
    public void IniParse_SkipsComments()
    {
        var result = Run("""
            let cfg = ini.parse("; comment\n# another\n[sec]\nkey = val");
            let result = cfg.sec.key;
            """);
        Assert.Equal("val", result);
    }

    [Fact]
    public void IniParse_GlobalKeys()
    {
        var result = Run("""
            let cfg = ini.parse("global = yes\n[sec]\nkey = val");
            let result = cfg.global;
            """);
        Assert.Equal("yes", result);
    }

    [Fact]
    public void IniParse_FloatCoercion()
    {
        var result = Run("""
            let cfg = ini.parse("[math]\nratio = 3.14");
            let result = cfg.math.ratio;
            """);
        Assert.Equal(3.14, result);
    }

    [Fact]
    public void IniParse_EmptyString()
    {
        var result = Run("""
            let cfg = ini.parse("");
            let result = len(dict.keys(cfg));
            """);
        Assert.Equal(0L, result);
    }

    [Fact]
    public void IniParse_WrongType()
    {
        RunExpectingError("ini.parse(42);");
    }

    // ===== Category 3: INI Serialization (ini.stringify) =====

    [Fact]
    public void IniStringify_BasicSections()
    {
        var result = Run("""
            let text = "[owner]\nname = John Doe\n\n[db]\nport = 143";
            let cfg = ini.parse(text);
            let output = ini.stringify(cfg);
            let cfg2 = ini.parse(output);
            let result = cfg2.db.port;
            """);
        Assert.Equal(143L, result);
    }

    [Fact]
    public void IniStringify_GlobalAndSections()
    {
        var result = Run("""
            let d = dict.new();
            d["globalKey"] = "globalVal";
            let sec = dict.new();
            sec["k"] = "v";
            d["section"] = sec;
            let output = ini.stringify(d);
            let result = str.contains(output, "globalKey = globalVal");
            """);
        Assert.Equal(true, result);
    }

    [Fact]
    public void IniStringify_WrongType()
    {
        RunExpectingError("""ini.stringify("not a dict");""");
    }

    // ===== Category 4: Config Namespace (config.parse / config.stringify) =====

    [Fact]
    public void ConfigParse_Ini()
    {
        var result = Run("""
            let cfg = config.parse("[sec]\nk = v", "ini");
            let result = cfg.sec.k;
            """);
        Assert.Equal("v", result);
    }

    [Fact]
    public void ConfigParse_Json()
    {
        var result = Run("""
            let cfg = config.parse("{\"name\": \"Alice\"}", "json");
            let result = cfg.name;
            """);
        Assert.Equal("Alice", result);
    }

    [Fact]
    public void ConfigStringify_Ini()
    {
        var result = Run("""
            let d = dict.new();
            let sec = dict.new();
            sec["port"] = 80;
            d["server"] = sec;
            let result = config.stringify(d, "ini");
            """);
        var str = Assert.IsType<string>(result);
        Assert.Contains("[server]", str);
        Assert.Contains("port = 80", str);
    }

    [Fact]
    public void ConfigStringify_Json()
    {
        var result = Run("""
            let d = dict.new();
            d["name"] = "test";
            let result = config.stringify(d, "json");
            """);
        var str = Assert.IsType<string>(result);
        Assert.Contains("\"name\"", str);
        Assert.Contains("\"test\"", str);
    }

    [Fact]
    public void ConfigParse_UnknownFormat()
    {
        RunExpectingError("""config.parse("text", "xml");""");
    }

    [Fact]
    public void ConfigStringify_UnknownFormat()
    {
        RunExpectingError("""config.stringify(dict.new(), "xml");""");
    }

    // ===== Category 5: Config File I/O (config.read / config.write) =====

    [Fact]
    public void ConfigReadWrite_IniRoundTrip()
    {
        var result = Run("""
            let d = dict.new();
            let sec = dict.new();
            sec["port"] = 8080;
            sec["host"] = "localhost";
            d["server"] = sec;
            let path = fs.tempFile() + ".ini";
            config.write(path, d);
            let cfg = config.read(path);
            fs.delete(path);
            let result = cfg.server.port;
            """);
        Assert.Equal(8080L, result);
    }

    [Fact]
    public void ConfigReadWrite_JsonRoundTrip()
    {
        var result = Run("""
            let d = dict.new();
            d["name"] = "test";
            d["version"] = 1;
            let path = fs.tempFile() + ".json";
            config.write(path, d);
            let cfg = config.read(path);
            fs.delete(path);
            let result = cfg.name;
            """);
        Assert.Equal("test", result);
    }

    [Fact]
    public void ConfigRead_NonexistentFile()
    {
        RunExpectingError("""config.read("/nonexistent/path/file.ini");""");
    }

    [Fact]
    public void ConfigRead_ExplicitFormat()
    {
        var result = Run("""
            let d = dict.new();
            let sec = dict.new();
            sec["k"] = "v";
            d["s"] = sec;
            let path = fs.tempFile() + ".txt";
            config.write(path, d, "ini");
            let cfg = config.read(path, "ini");
            fs.delete(path);
            let result = cfg.s.k;
            """);
        Assert.Equal("v", result);
    }
}
