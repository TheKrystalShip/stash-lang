namespace Stash.Tests.Stdlib.SourceGenerator;

using Stash.Stdlib.Generators;
using Xunit;

public class NamingRulesTests
{
    [Theory]
    [InlineData("Push", "push")]
    [InlineData("ReadAll", "readAll")]
    [InlineData("X", "x")]
    [InlineData("xy", "xy")]
    [InlineData("", "")]
    public void ToCamelCase_LowercasesFirstChar(string input, string expected)
    {
        Assert.Equal(expected, NamingRules.ToCamelCase(input));
    }

    [Theory]
    [InlineData("readURL", true)]
    [InlineData("URLpath", true)]
    [InlineData("readUrl", false)]
    [InlineData("readAllText", false)]
    [InlineData("a", false)]
    [InlineData("", false)]
    public void HasConsecutiveUppercase_DetectsAllCapsRuns(string input, bool expected)
    {
        Assert.Equal(expected, NamingRules.HasConsecutiveUppercase(input));
    }

    [Theory]
    [InlineData("ArrBuiltIns", "arr")]
    [InlineData("StrBuiltIns", "str")]
    [InlineData("MarshalFixture", "marshalfixture")]
    [InlineData("BuiltIns", "builtins")]
    public void NamespaceFromClass_StripsSuffixAndLowercases(string className, string expected)
    {
        Assert.Equal(expected, NamingRules.NamespaceFromClass(className));
    }
}
