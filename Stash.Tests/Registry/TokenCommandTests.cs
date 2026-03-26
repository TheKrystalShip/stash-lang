using System;
using System.Collections.Generic;
using System.Text.Json;
using Stash.Cli.PackageManager;
using Xunit;

namespace Stash.Tests.Registry;

public sealed class TokenCommandTests
{
    [Fact]
    public void TokenCreateRequest_Serialization_RoundTrips()
    {
        var request = new TokenCreateRequest
        {
            Scope = "publish",
            Description = "CI token",
            ExpiresIn = "30d"
        };

        string json = JsonSerializer.Serialize(request, CliJsonContext.Default.TokenCreateRequest);
        var deserialized = JsonSerializer.Deserialize(json, CliJsonContext.Default.TokenCreateRequest);

        Assert.NotNull(deserialized);
        Assert.Equal("publish", deserialized.Scope);
        Assert.Equal("CI token", deserialized.Description);
        Assert.Equal("30d", deserialized.ExpiresIn);
    }

    [Fact]
    public void TokenCreateResult_Deserialization_ParsesAllFields()
    {
        string json = """{"token":"jwt.value","tokenId":"abc-123","scope":"publish","expiresAt":"2026-06-01T00:00:00Z","description":"test"}""";

        var result = JsonSerializer.Deserialize(json, CliJsonContext.Default.TokenCreateResult);

        Assert.NotNull(result);
        Assert.Equal("jwt.value", result.Token);
        Assert.Equal("abc-123", result.TokenId);
        Assert.Equal("publish", result.Scope);
        Assert.Equal("test", result.Description);
    }

    [Fact]
    public void TokenListResult_Deserialization_ParsesTokenList()
    {
        string json = """{"tokens":[{"tokenId":"id1","scope":"publish","createdAt":"2026-01-01T00:00:00Z","expiresAt":"2026-04-01T00:00:00Z","description":"CI"}]}""";

        var result = JsonSerializer.Deserialize(json, CliJsonContext.Default.TokenListResult);

        Assert.NotNull(result);
        Assert.Single(result.Tokens);
        Assert.Equal("id1", result.Tokens[0].TokenId);
        Assert.Equal("publish", result.Tokens[0].Scope);
        Assert.Equal("CI", result.Tokens[0].Description);
    }

    [Fact]
    public void TokenCreateRequest_NullFields_OmittedInSerialization()
    {
        var request = new TokenCreateRequest { Scope = "publish" };

        string json = JsonSerializer.Serialize(request, CliJsonContext.Default.TokenCreateRequest);

        Assert.DoesNotContain("description", json);
        Assert.DoesNotContain("expiresIn", json);
    }
}
