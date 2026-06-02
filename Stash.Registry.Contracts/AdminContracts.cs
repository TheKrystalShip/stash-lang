using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Stash.Registry.Contracts;

/// <summary>
/// Request body for the <c>POST /api/v1/admin/users</c> endpoint.
/// </summary>
public sealed class CreateUserRequest
{
    /// <summary>The username for the new account.</summary>
    [JsonPropertyName("username")]
    public string? Username { get; set; }

    /// <summary>The plaintext password for the new account.</summary>
    [JsonPropertyName("password")]
    public string? Password { get; set; }

    /// <summary>The role to assign to the new account (e.g. <c>"user"</c> or <c>"admin"</c>).</summary>
    [JsonPropertyName("role")]
    public string? Role { get; set; }
}

/// <summary>
/// Response body returned by the <c>POST /api/v1/admin/users</c> endpoint on success.
/// </summary>
public sealed class CreateUserResponse
{
    /// <summary>Indicates whether the user creation succeeded. Always <c>true</c> for a 200 response.</summary>
    [JsonPropertyName("ok")]
    public bool Ok { get; set; } = true;

    /// <summary>The username of the newly created account.</summary>
    [JsonPropertyName("username")]
    public required string Username { get; set; }

    /// <summary>The role assigned to the newly created account.</summary>
    [JsonPropertyName("role")]
    public required string Role { get; set; }
}

/// <summary>
/// Request body for the <c>PATCH /api/v1/packages/{name}/owners</c> endpoint.
/// </summary>
public sealed class OwnerUpdateRequest
{
    /// <summary>The list of usernames to add as package owners.</summary>
    [JsonPropertyName("add")]
    public List<string>? Add { get; set; }

    /// <summary>The list of usernames to remove from the package owners.</summary>
    [JsonPropertyName("remove")]
    public List<string>? Remove { get; set; }
}

/// <summary>
/// Response body returned by the <c>GET /api/v1/packages/{name}/owners</c> endpoint.
/// </summary>
public sealed class OwnerListResponse
{
    /// <summary>The list of usernames that currently own the package.</summary>
    [JsonPropertyName("owners")]
    public required List<string> Owners { get; set; }
}

/// <summary>
/// Response body returned by the <c>GET /api/v1/admin/stats</c> endpoint.
/// </summary>
public sealed class StatsResponse
{
    /// <summary>The total number of registered user accounts.</summary>
    [JsonPropertyName("users")]
    public int Users { get; set; }
}

/// <summary>
/// Wire DTO for a single audit log entry returned by the <c>GET /api/v1/admin/audit-log</c> endpoint.
/// Maps from the EF <c>AuditEntry</c> entity; the <c>id</c> database column is intentionally excluded
/// from the wire shape (internal surrogate key, not part of the public API contract).
/// </summary>
public sealed class AuditEntryResponse
{
    /// <summary>The action type string, e.g. <c>"publish"</c>, <c>"unpublish"</c>, <c>"user.create"</c>.</summary>
    [JsonPropertyName("action")]
    public string Action { get; set; } = "";

    /// <summary>The package name involved in this action, or <c>null</c> for non-package events.</summary>
    [JsonPropertyName("package")]
    public string? Package { get; set; }

    /// <summary>The package version involved in this action, or <c>null</c> for non-version events.</summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    /// <summary>The username of the actor who performed the action, or <c>null</c> if not applicable.</summary>
    [JsonPropertyName("user")]
    public string? User { get; set; }

    /// <summary>An optional secondary target of the action (e.g. username being created or token ID), or <c>null</c>.</summary>
    [JsonPropertyName("target")]
    public string? Target { get; set; }

    /// <summary>The IP address of the client that triggered the action, or <c>null</c> if not available.</summary>
    [JsonPropertyName("ip")]
    public string? Ip { get; set; }

    /// <summary>The UTC timestamp at which this audit event occurred.</summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    /// <summary>The authorization decision: <c>"allow"</c> or <c>"deny"</c>, or <c>null</c> for legacy entries.</summary>
    [JsonPropertyName("decision")]
    public string? Decision { get; set; }

    /// <summary>The typed deny reason when <see cref="Decision"/> is <c>"deny"</c>, or <c>null</c> for allow entries.</summary>
    [JsonPropertyName("denyReason")]
    public string? DenyReason { get; set; }
}

/// <summary>
/// Response body returned by the <c>GET /api/v1/admin/audit-log</c> endpoint.
/// </summary>
public sealed class AuditLogResponse
{
    /// <summary>The list of audit log entries for the current page.</summary>
    [JsonPropertyName("entries")]
    public required List<AuditEntryResponse> Entries { get; set; }

    /// <summary>The total number of audit log entries across all pages.</summary>
    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

    /// <summary>The 1-based page index of the current result set.</summary>
    [JsonPropertyName("page")]
    public int Page { get; set; }

    /// <summary>The number of entries included per page.</summary>
    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    /// <summary>The total number of pages available given <see cref="TotalCount"/> and <see cref="PageSize"/>.</summary>
    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }
}
