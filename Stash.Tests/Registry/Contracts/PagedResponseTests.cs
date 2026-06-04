using System.Collections.Generic;
using System.Text.Json;
using Stash.Registry.Contracts;
using Xunit;

namespace Stash.Tests.Registry.Contracts;

/// <summary>
/// Unit tests for the shared <see cref="PagedResponse{T}"/> pagination envelope.
/// Validates the JSON wire shape (collection key is <c>"items"</c>), the presence of all
/// required metadata fields, and round-trip serialization / deserialization behavior.
/// </summary>
public sealed class PagedResponseTests
{
    // ── Wire shape ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that the serialized JSON uses the key <c>"items"</c> for the collection,
    /// not any former per-endpoint key like <c>"packages"</c> or <c>"entries"</c>.
    /// </summary>
    [Fact]
    public void PagedResponse_SerializesToItemsKey()
    {
        var response = new PagedResponse<PackageSummaryResponse>
        {
            Items = new List<PackageSummaryResponse>
            {
                new PackageSummaryResponse
                {
                    Name = "@alice/widget",
                    Keywords = new List<string> { "ui" },
                    UpdatedAt = "2024-01-01T00:00:00Z"
                }
            },
            TotalCount = 1,
            Page = 1,
            PageSize = 20,
            TotalPages = 1
        };

        string json = JsonSerializer.Serialize(response);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("items", out _),
            "Serialized JSON must contain the key 'items' (not 'packages', 'entries', etc.).");
        Assert.False(root.TryGetProperty("packages", out _),
            "Serialized JSON must NOT contain the old key 'packages'.");
        Assert.False(root.TryGetProperty("entries", out _),
            "Serialized JSON must NOT contain the old key 'entries'.");
    }

    /// <summary>
    /// Verifies that all five metadata fields (totalCount, page, pageSize, totalPages, items)
    /// are present in the serialized JSON.
    /// </summary>
    [Fact]
    public void PagedResponse_ContainsAllMetadataFields()
    {
        var response = new PagedResponse<string>
        {
            Items = new List<string> { "a", "b" },
            TotalCount = 42,
            Page = 2,
            PageSize = 10,
            TotalPages = 5
        };

        string json = JsonSerializer.Serialize(response);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("items", out _), "Expected 'items' field.");
        Assert.True(root.TryGetProperty("totalCount", out var tc), "Expected 'totalCount' field.");
        Assert.True(root.TryGetProperty("page", out var pg), "Expected 'page' field.");
        Assert.True(root.TryGetProperty("pageSize", out var ps), "Expected 'pageSize' field.");
        Assert.True(root.TryGetProperty("totalPages", out var tp), "Expected 'totalPages' field.");

        Assert.Equal(42, tc.GetInt32());
        Assert.Equal(2, pg.GetInt32());
        Assert.Equal(10, ps.GetInt32());
        Assert.Equal(5, tp.GetInt32());
    }

    // ── Round-trip ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that a <see cref="PagedResponse{T}"/> round-trips through JSON correctly.
    /// </summary>
    [Fact]
    public void PagedResponse_RoundTrips_ThroughJson()
    {
        var original = new PagedResponse<string>
        {
            Items = new List<string> { "x", "y", "z" },
            TotalCount = 3,
            Page = 1,
            PageSize = 10,
            TotalPages = 1
        };

        string json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<PagedResponse<string>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(deserialized);
        Assert.Equal(3, deserialized!.Items.Count);
        Assert.Equal(3, deserialized.TotalCount);
        Assert.Equal(1, deserialized.Page);
        Assert.Equal(10, deserialized.PageSize);
        Assert.Equal(1, deserialized.TotalPages);
        Assert.Equal(new List<string> { "x", "y", "z" }, deserialized.Items);
    }

    /// <summary>
    /// Verifies that an empty items list serializes to an empty JSON array, not null.
    /// </summary>
    [Fact]
    public void PagedResponse_EmptyItems_SerializesToEmptyArray()
    {
        var response = new PagedResponse<string>
        {
            Items = new List<string>(),
            TotalCount = 0,
            Page = 1,
            PageSize = 20,
            TotalPages = 0
        };

        string json = JsonSerializer.Serialize(response);
        using var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.TryGetProperty("items", out var items));
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        Assert.Equal(0, items.GetArrayLength());
    }

    // ── Type-independence ──────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the envelope works correctly with <see cref="PackageSummaryResponse"/>
    /// as the element type — the primary consumer in the search endpoint.
    /// </summary>
    [Fact]
    public void PagedResponse_OfPackageSummary_SerializesItemsAsArray()
    {
        var response = new PagedResponse<PackageSummaryResponse>
        {
            Items = new List<PackageSummaryResponse>
            {
                new PackageSummaryResponse
                {
                    Name = "@org/lib",
                    Description = "A test library",
                    Latest = "1.0.0",
                    Keywords = new List<string> { "test" },
                    UpdatedAt = "2024-06-01T00:00:00Z",
                    Deprecated = false
                }
            },
            TotalCount = 1,
            Page = 1,
            PageSize = 20,
            TotalPages = 1
        };

        string json = JsonSerializer.Serialize(response);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("items", out var items));
        Assert.Equal(1, items.GetArrayLength());

        var first = items[0];
        Assert.Equal("@org/lib", first.GetProperty("name").GetString());
        Assert.Equal("1.0.0", first.GetProperty("latest").GetString());
    }

    /// <summary>
    /// Verifies the envelope works correctly with <see cref="AuditEntryResponse"/>
    /// as the element type — the primary consumer in the audit-log endpoint.
    /// </summary>
    [Fact]
    public void PagedResponse_OfAuditEntry_SerializesItemsAsArray()
    {
        var response = new PagedResponse<AuditEntryResponse>
        {
            Items = new List<AuditEntryResponse>
            {
                new AuditEntryResponse
                {
                    Action = "publish",
                    Package = "@alice/widget",
                    Version = "1.0.0",
                    User = "alice",
                    Timestamp = System.DateTime.UtcNow
                }
            },
            TotalCount = 1,
            Page = 1,
            PageSize = 50,
            TotalPages = 1
        };

        string json = JsonSerializer.Serialize(response);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("items", out var items));
        Assert.Equal(1, items.GetArrayLength());

        var first = items[0];
        Assert.Equal("publish", first.GetProperty("action").GetString());
    }
}
