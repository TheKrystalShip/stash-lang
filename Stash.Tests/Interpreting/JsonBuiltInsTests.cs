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
}
