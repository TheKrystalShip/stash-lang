namespace Stash.Tests.Interpreting;

public class JsonBuiltInsTests : StashTestBase
{
    // ── json.valid ────────────────────────────────────────────────────────

    [Fact]
    public void Valid_ValidObject()
    {
        var result = Run("let result = json.valid(\"{}\");");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Valid_ValidArray()
    {
        var result = Run("let result = json.valid(\"[1, 2, 3]\");");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Valid_ValidString()
    {
        var result = Run("let result = json.valid(\"\\\"hello\\\"\");");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Valid_ValidNumber()
    {
        var result = Run("let result = json.valid(\"42\");");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Valid_InvalidJson()
    {
        var result = Run("let result = json.valid(\"not json at all\");");
        Assert.Equal(false, result);
    }

    [Fact]
    public void Valid_InvalidBraces()
    {
        var result = Run("let result = json.valid(\"{ bad }\");");
        Assert.Equal(false, result);
    }

    [Fact]
    public void Valid_EmptyString()
    {
        var result = Run("let result = json.valid(\"\");");
        Assert.Equal(false, result);
    }

    [Fact]
    public void Valid_NonStringThrows()
    {
        RunExpectingError("json.valid(42);");
    }

    // ── Optional Args ────────────────────────────────────────────────────────

    [Fact]
    public void Stringify_WithIndent_ProducesPrettyOutput()
    {
        var result = Run("let result = json.stringify({a: 1}, 2);");
        var json = Assert.IsType<string>(result);
        Assert.Contains("\n", json);
    }

    [Fact]
    public void Stringify_NoIndent_ProducesCompactOutput()
    {
        var result = Run("let result = json.stringify({a: 1});");
        var json = Assert.IsType<string>(result);
        Assert.DoesNotContain("\n", json);
    }

    [Fact]
    public void Pretty_WithCustomIndent_UsesFourSpaces()
    {
        var result = Run("let result = json.pretty({a: 1}, 4);");
        var json = Assert.IsType<string>(result);
        Assert.Contains("    ", json);
    }
}
