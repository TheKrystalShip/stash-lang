using System.Collections.Generic;
using System.Text.Json.Serialization;
using Stash.Registry.Database.Models;

namespace Stash.Registry.Contracts;

public sealed class CreateUserRequest
{
    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("password")]
    public string? Password { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }
}

public sealed class CreateUserResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; } = true;

    [JsonPropertyName("username")]
    public required string Username { get; set; }

    [JsonPropertyName("role")]
    public required string Role { get; set; }
}

public sealed class OwnerUpdateRequest
{
    [JsonPropertyName("add")]
    public List<string>? Add { get; set; }

    [JsonPropertyName("remove")]
    public List<string>? Remove { get; set; }
}

public sealed class OwnerListResponse
{
    [JsonPropertyName("owners")]
    public required List<string> Owners { get; set; }
}

public sealed class StatsResponse
{
    [JsonPropertyName("users")]
    public int Users { get; set; }
}

public sealed class AuditLogResponse
{
    [JsonPropertyName("entries")]
    public required List<AuditEntry> Entries { get; set; }

    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }
}
