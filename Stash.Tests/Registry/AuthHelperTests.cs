using System;
using Stash.Registry.Endpoints;
using Xunit;

namespace Stash.Tests.Registry;

public sealed class AuthHelperTests
{
    [Fact]
    public void ParseTokenExpiry_Days_ReturnsCorrectTimeSpan()
    {
        DateTime before = DateTime.UtcNow;
        DateTime result = AuthHelper.ParseTokenExpiry("30d");
        DateTime after = DateTime.UtcNow;

        Assert.True(result >= before.AddDays(30));
        Assert.True(result <= after.AddDays(30));
    }

    [Fact]
    public void ParseTokenExpiry_Hours_ReturnsCorrectTimeSpan()
    {
        DateTime before = DateTime.UtcNow;
        DateTime result = AuthHelper.ParseTokenExpiry("12h");
        DateTime after = DateTime.UtcNow;

        Assert.True(result >= before.AddHours(12));
        Assert.True(result <= after.AddHours(12));
    }

    [Fact]
    public void ParseTokenExpiry_Minutes_ReturnsCorrectTimeSpan()
    {
        DateTime before = DateTime.UtcNow;
        DateTime result = AuthHelper.ParseTokenExpiry("30m");
        DateTime after = DateTime.UtcNow;

        Assert.True(result >= before.AddMinutes(30));
        Assert.True(result <= after.AddMinutes(30));
    }

    [Fact]
    public void ParseTokenExpiry_DefaultFormat_ReturnsDays()
    {
        // A bare integer (no suffix) is treated as days.
        DateTime before = DateTime.UtcNow;
        DateTime result = AuthHelper.ParseTokenExpiry("90");
        DateTime after = DateTime.UtcNow;

        Assert.True(result >= before.AddDays(90));
        Assert.True(result <= after.AddDays(90));
    }

    [Theory]
    [InlineData("xd")]
    [InlineData("1.5d")]
    [InlineData("abc")]
    [InlineData("")]
    public void ParseTokenExpiry_InvalidInput_Throws(string input)
    {
        Assert.ThrowsAny<Exception>(() => AuthHelper.ParseTokenExpiry(input));
    }
}
