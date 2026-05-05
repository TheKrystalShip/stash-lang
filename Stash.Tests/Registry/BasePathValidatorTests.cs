using System;
using Stash.Registry.Configuration;
using Xunit;

namespace Stash.Tests.Registry;

public class BasePathValidatorTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("/", "")]
    [InlineData("/registry", "/registry")]
    [InlineData("/api/v1", "/api/v1")]
    [InlineData("/foo/bar/baz", "/foo/bar/baz")]
    public void Normalize_ValidValues_ReturnsExpected(string? input, string expected)
    {
        Assert.Equal(expected, BasePathValidator.Normalize(input));
    }

    [Theory]
    [InlineData("registry")]
    [InlineData("api/v1")]
    public void Normalize_MissingLeadingSlash_Throws(string input)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => BasePathValidator.Normalize(input));
        Assert.Contains("must start with '/'", ex.Message);
    }

    [Theory]
    [InlineData("/registry/")]
    [InlineData("/api/v1/")]
    public void Normalize_TrailingSlash_Throws(string input)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => BasePathValidator.Normalize(input));
        Assert.Contains("must not end with '/'", ex.Message);
    }
}
