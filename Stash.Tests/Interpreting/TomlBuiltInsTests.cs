using Stash.Runtime.Types;

namespace Stash.Tests.Interpreting;

public class TomlBuiltInsTests : StashTestBase
{


    // ── toml.parse ────────────────────────────────────────────────────────

    [Fact]
    public void Parse_BasicTable()
    {
        var result = Run("""let result = toml.parse("title = \"Test\"\ncount = 42");""");
        var d = Assert.IsType<StashDictionary>(result);
        Assert.Equal("Test", d.Get("title").ToObject());
        Assert.Equal(42L, d.Get("count").ToObject());
    }

    [Fact]
    public void Parse_NestedTables()
    {
        var result = Run("""let result = toml.parse("[server]\nhost = \"localhost\"\nport = 8080");""");
        var d = Assert.IsType<StashDictionary>(result);
        var server = Assert.IsType<StashDictionary>(d.Get("server").ToObject());
        Assert.Equal("localhost", server.Get("host").ToObject());
        Assert.Equal(8080L, server.Get("port").ToObject());
    }

    [Fact]
    public void Parse_Array()
    {
        var result = Run("""let result = toml.parse("tags = [\"a\", \"b\", \"c\"]");""");
        var d = Assert.IsType<StashDictionary>(result);
        var tags = Assert.IsType<List<object?>>(d.Get("tags").ToObject());
        Assert.Equal(3, tags.Count);
        Assert.Equal("a", tags[0]);
        Assert.Equal("b", tags[1]);
        Assert.Equal("c", tags[2]);
    }

    [Fact]
    public void Parse_ArrayOfTables()
    {
        var result = Run("""let result = toml.parse("[[products]]\nname = \"A\"\n[[products]]\nname = \"B\"");""");
        var d = Assert.IsType<StashDictionary>(result);
        var products = Assert.IsType<List<object?>>(d.Get("products").ToObject());
        Assert.Equal(2, products.Count);
        var first = Assert.IsType<StashDictionary>(products[0]);
        Assert.Equal("A", first.Get("name").ToObject());
        var second = Assert.IsType<StashDictionary>(products[1]);
        Assert.Equal("B", second.Get("name").ToObject());
    }

    [Fact]
    public void Parse_BooleanValues()
    {
        var result = Run("""let result = toml.parse("enabled = true\nverbose = false");""");
        var d = Assert.IsType<StashDictionary>(result);
        Assert.Equal(true, d.Get("enabled").ToObject());
        Assert.Equal(false, d.Get("verbose").ToObject());
    }

    [Fact]
    public void Parse_FloatValue()
    {
        var result = Run("""let result = toml.parse("ratio = 3.14");""");
        var d = Assert.IsType<StashDictionary>(result);
        var ratio = Assert.IsType<double>(d.Get("ratio").ToObject());
        Assert.InRange(ratio, 3.13, 3.15);
    }

    [Fact]
    public void Parse_IntegerValue()
    {
        var result = Run("""let result = toml.parse("count = 42");""");
        var d = Assert.IsType<StashDictionary>(result);
        Assert.Equal(42L, d.Get("count").ToObject());
    }

    [Fact]
    public void Parse_EmptyString()
    {
        var result = Run("""let result = toml.parse("");""");
        var d = Assert.IsType<StashDictionary>(result);
        Assert.Equal(0, d.Count);
    }

    [Fact]
    public void Parse_NonStringThrows()
    {
        RunExpectingError("toml.parse(42);");
    }

    [Fact]
    public void Parse_InvalidTomlThrows()
    {
        RunExpectingError("""toml.parse("= no key");""");
    }

    // ── toml.stringify ────────────────────────────────────────────────────

    [Fact]
    public void Stringify_BasicDict()
    {
        var result = Run("""
            let d = dict.new();
            d["name"] = "Alice";
            d["age"] = 30;
            let result = toml.stringify(d);
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
            let result = toml.stringify(outer);
            """);
        var str = Assert.IsType<string>(result);
        Assert.Contains("server", str);
        Assert.Contains("host", str);
        Assert.Contains("localhost", str);
    }

    [Fact]
    public void Stringify_Array()
    {
        var result = Run("""
            let d = dict.new();
            d["items"] = [1, 2, 3];
            let result = toml.stringify(d);
            """);
        var str = Assert.IsType<string>(result);
        Assert.Contains("items", str);
        Assert.Contains("1", str);
    }

    [Fact]
    public void Stringify_Roundtrip()
    {
        var result = Run("""
            let d = dict.new();
            d["city"] = "Berlin";
            d["pop"] = 3700000;
            let serialized = toml.stringify(d);
            let result = toml.parse(serialized);
            """);
        var d = Assert.IsType<StashDictionary>(result);
        Assert.Equal("Berlin", d.Get("city").ToObject());
        Assert.Equal(3700000L, d.Get("pop").ToObject());
    }

    [Fact]
    public void Stringify_NonDictThrows()
    {
        RunExpectingError("""toml.stringify("not a dict");""");
    }

    // ── toml.valid ────────────────────────────────────────────────────────

    [Fact]
    public void Valid_ValidToml()
    {
        var result = Run("""let result = toml.valid("key = \"value\"");""");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Valid_InvalidToml()
    {
        var result = Run("""let result = toml.valid("= no key");""");
        Assert.Equal(false, result);
    }

    [Fact]
    public void Valid_EmptyString()
    {
        var result = Run("""let result = toml.valid("");""");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Valid_NonStringThrows()
    {
        RunExpectingError("toml.valid(42);");
    }

    // ── config namespace (toml format) ────────────────────────────────────

    [Fact]
    public void ConfigParse_Toml()
    {
        var result = Run("""let result = config.parse("[server]\nhost = \"localhost\"\nport = 8080", "toml");""");
        var d = Assert.IsType<StashDictionary>(result);
        var server = Assert.IsType<StashDictionary>(d.Get("server").ToObject());
        Assert.Equal("localhost", server.Get("host").ToObject());
        Assert.Equal(8080L, server.Get("port").ToObject());
    }

    [Fact]
    public void ConfigStringify_Toml()
    {
        var result = Run("""
            let d = dict.new();
            d["env"] = "production";
            let result = config.stringify(d, "toml");
            """);
        var str = Assert.IsType<string>(result);
        Assert.Contains("env", str);
        Assert.Contains("production", str);
    }

    [Fact]
    public void ConfigReadWrite_TomlRoundTrip()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".toml");
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
            Assert.Equal("stash", d.Get("app").ToObject());
            Assert.Equal(9000L, d.Get("port").ToObject());
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
