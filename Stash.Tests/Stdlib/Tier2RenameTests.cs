namespace Stash.Tests.Stdlib;

/// <summary>
/// Tests for the Tier 2 renames introduced in the Stdlib Namespace Audit:
/// <list type="bullet">
///   <item><c>arr.create</c> (new canonical name for <c>arr.new</c>)</item>
///   <item><c>str.charCode</c> (moved from <c>conv.charCode</c>)</item>
///   <item><c>str.fromCharCode</c> (moved from <c>conv.fromCharCode</c>)</item>
/// </list>
/// Also verifies that the deprecated aliases still function correctly.
/// </summary>
public class Tier2RenameTests : Stash.Tests.Interpreting.StashTestBase
{
    // =========================================================================
    // arr.create — new canonical name
    // =========================================================================

    [Fact]
    public void ArrCreate_IntType_ReturnsTypedArrayOfLength()
    {
        var result = Run("""
            let a = arr.create("int", 5);
            let result = len(a);
        """);
        Assert.Equal(5L, result);
    }

    [Fact]
    public void ArrCreate_StringType_ReturnsTypedArrayOfLength()
    {
        var result = Run("""
            let a = arr.create("string", 3);
            let result = len(a);
        """);
        Assert.Equal(3L, result);
    }

    [Fact]
    public void ArrCreate_NegativeSize_ThrowsValueError()
    {
        RunExpectingError("""arr.create("int", -1);""");
    }

    // =========================================================================
    // arr.new — deprecated alias
    // =========================================================================

    [Fact]
    public void ArrNew_DeprecatedAlias_StillWorks()
    {
        var result = Run("""
            let a = arr.new("float", 4);
            let result = len(a);
        """);
        Assert.Equal(4L, result);
    }

    // =========================================================================
    // str.charCode — new canonical name
    // =========================================================================

    [Fact]
    public void StrCharCode_UppercaseA_Returns65()
    {
        var result = Run("""let result = str.charCode("A");""");
        Assert.Equal(65L, result);
    }

    [Fact]
    public void StrCharCode_EmptyString_ThrowsValueError()
    {
        RunExpectingError("""str.charCode("");""");
    }

    // =========================================================================
    // str.fromCharCode — new canonical name
    // =========================================================================

    [Fact]
    public void StrFromCharCode_65_ReturnsUppercaseA()
    {
        var result = Run("""let result = str.fromCharCode(65);""");
        Assert.Equal("A", result);
    }

    [Fact]
    public void StrFromCharCode_NegativeValue_ThrowsValueError()
    {
        RunExpectingError("""str.fromCharCode(-1);""");
    }

    // =========================================================================
    // conv.charCode / conv.fromCharCode — deprecated aliases
    // =========================================================================

    [Fact]
    public void ConvCharCode_DeprecatedAlias_StillWorks()
    {
        var result = Run("""let result = conv.charCode("A");""");
        Assert.Equal(65L, result);
    }

    [Fact]
    public void ConvFromCharCode_DeprecatedAlias_StillWorks()
    {
        var result = Run("""let result = conv.fromCharCode(65);""");
        Assert.Equal("A", result);
    }
}
