using Stash.Runtime;

namespace Stash.Tests.Core;

/// <summary>
/// Unit tests for <see cref="ErrorTypeRegistry"/> — the single source of truth for
/// built-in error type names and matching semantics.
/// </summary>
public class ErrorTypeRegistryTests
{
    // =========================================================================
    // IsBuiltInSubtype
    // =========================================================================

    [Theory]
    [InlineData("ValueError")]
    [InlineData("TypeError")]
    [InlineData("ParseError")]
    [InlineData("IndexError")]
    [InlineData("IOError")]
    [InlineData("NotSupportedError")]
    [InlineData("TimeoutError")]
    [InlineData("CommandError")]
    [InlineData("LockError")]
    public void IsBuiltInSubtype_KnownType_ReturnsTrue(string typeName)
    {
        Assert.True(ErrorTypeRegistry.IsBuiltInSubtype(typeName));
    }

    [Theory]
    [InlineData("Error")]
    [InlineData("error")]
    [InlineData("valueerror")]
    [InlineData("NetworkError")]
    [InlineData("ConfigError")]
    [InlineData("")]
    [InlineData("RuntimeError")]
    public void IsBuiltInSubtype_UnknownOrBaseType_ReturnsFalse(string typeName)
    {
        Assert.False(ErrorTypeRegistry.IsBuiltInSubtype(typeName));
    }

    // =========================================================================
    // IsBaseType
    // =========================================================================

    [Fact]
    public void IsBaseType_Error_ReturnsTrue()
    {
        Assert.True(ErrorTypeRegistry.IsBaseType("Error"));
    }

    [Theory]
    [InlineData("error")]
    [InlineData("ERROR")]
    [InlineData("ValueError")]
    [InlineData("")]
    [InlineData("RuntimeError")]
    public void IsBaseType_NonBaseType_ReturnsFalse(string typeName)
    {
        Assert.False(ErrorTypeRegistry.IsBaseType(typeName));
    }

    // =========================================================================
    // Matches
    // =========================================================================

    [Fact]
    public void Matches_SameType_ReturnsTrue()
    {
        Assert.True(ErrorTypeRegistry.Matches("ValueError", "ValueError"));
    }

    [Fact]
    public void Matches_BaseTypeTarget_ReturnsTrueForAnyError()
    {
        Assert.True(ErrorTypeRegistry.Matches("ValueError", "Error"));
        Assert.True(ErrorTypeRegistry.Matches("TypeError", "Error"));
        Assert.True(ErrorTypeRegistry.Matches("IOError", "Error"));
        Assert.True(ErrorTypeRegistry.Matches("LockError", "Error"));
        Assert.True(ErrorTypeRegistry.Matches("RuntimeError", "Error"));
    }

    [Fact]
    public void Matches_DifferentSubtype_ReturnsFalse()
    {
        Assert.False(ErrorTypeRegistry.Matches("ValueError", "TypeError"));
        Assert.False(ErrorTypeRegistry.Matches("IOError", "ValueError"));
    }

    [Fact]
    public void Matches_UnknownTypes_FalseUnlessSame()
    {
        Assert.False(ErrorTypeRegistry.Matches("NetworkError", "ConfigError"));
        Assert.True(ErrorTypeRegistry.Matches("NetworkError", "NetworkError"));
    }

    [Fact]
    public void Matches_BaseTypeError_MatchesBaseType()
    {
        // "Error" errorType matched against "Error" target
        Assert.True(ErrorTypeRegistry.Matches("Error", "Error"));
    }
}
