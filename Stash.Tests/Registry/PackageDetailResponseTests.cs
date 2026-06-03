using System.Collections.Generic;
using System.Text.Json;
using Stash.Registry.Contracts;
using Xunit;

namespace Stash.Tests.Registry;

/// <summary>
/// Verifies the serialized shape of <see cref="PackageDetailResponse"/> —
/// specifically that the <c>"owners"</c> key is absent after its removal (P1).
/// </summary>
public sealed class PackageDetailResponseTests
{
    private static PackageDetailResponse BuildMinimal() => new PackageDetailResponse
    {
        Name = "@myorg/my-lib",
        Keywords = new List<string>(),
        Versions = new Dictionary<string, VersionDetailResponse>(),
        CreatedAt = "2024-01-01T00:00:00Z",
        UpdatedAt = "2024-01-02T00:00:00Z",
    };

    [Fact]
    public void Serialize_NoOwnersKey_JsonDoesNotContainOwnersProperty()
    {
        var response = BuildMinimal();

        string json = JsonSerializer.Serialize(response);

        using var doc = JsonDocument.Parse(json);
        bool hasOwners = doc.RootElement.TryGetProperty("owners", out _);
        Assert.False(hasOwners,
            "Serialized PackageDetailResponse must not contain an 'owners' key. " +
            $"Actual JSON: {json}");
    }

    [Fact]
    public void Serialize_RequiredFields_PresentInOutput()
    {
        var response = BuildMinimal();
        response.Description = "A test package";
        response.License = "MIT";
        response.Latest = "1.0.0";

        string json = JsonSerializer.Serialize(response);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("name", out var name));
        Assert.Equal("@myorg/my-lib", name.GetString());

        Assert.True(root.TryGetProperty("description", out var desc));
        Assert.Equal("A test package", desc.GetString());

        Assert.True(root.TryGetProperty("license", out var lic));
        Assert.Equal("MIT", lic.GetString());

        Assert.True(root.TryGetProperty("latest", out var latest));
        Assert.Equal("1.0.0", latest.GetString());

        // Negative guard — still no owners key even with full optional fields set.
        Assert.False(root.TryGetProperty("owners", out _),
            "PackageDetailResponse must not emit an 'owners' key regardless of other fields.");
    }

    [Fact]
    public void Deserialize_JsonWithOwnersKey_DoesNotMapToAnyProperty()
    {
        // Old wire responses from the server (or cached) might still contain "owners";
        // deserializing them must not throw — unknown properties are silently ignored.
        const string legacyJson = """
            {
                "name": "@org/pkg",
                "owners": ["alice", "bob"],
                "keywords": [],
                "versions": {},
                "createdAt": "2024-01-01T00:00:00Z",
                "updatedAt": "2024-01-02T00:00:00Z"
            }
            """;

        var response = JsonSerializer.Deserialize<PackageDetailResponse>(legacyJson);

        Assert.NotNull(response);
        Assert.Equal("@org/pkg", response!.Name);
        // No "Owners" member to assert on — this just confirms no exception was thrown.
    }
}
