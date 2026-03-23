using System;

namespace Stash.Registry.Database.Models;

/// <summary>
/// Database entity representing a single entry in the registry audit log.
/// </summary>
/// <remarks>
/// Audit entries are written by <see cref="Services.AuditService"/> for security-relevant
/// events such as publish, unpublish, login, user creation, and ownership changes.
/// The <c>id</c> column is an auto-incrementing integer primary key. Column names
/// use <c>snake_case</c>. Entries are never updated or deleted — the log is append-only.
/// </remarks>
public sealed class AuditEntry
{
    /// <summary>The auto-incrementing integer primary key (mapped to <c>id</c> column).</summary>
    public int Id { get; set; }

    /// <summary>The action type string, e.g. <c>"publish"</c>, <c>"unpublish"</c>, <c>"user_create"</c>, <c>"token_revoke"</c>.</summary>
    public string Action { get; set; } = "";

    /// <summary>The package name involved in this action, or <c>null</c> for non-package events.</summary>
    public string? Package { get; set; }

    /// <summary>The package version involved in this action, or <c>null</c> for non-version events.</summary>
    public string? Version { get; set; }

    /// <summary>The username of the actor who performed the action, or <c>null</c> if not applicable.</summary>
    public string? User { get; set; }

    /// <summary>
    /// An optional secondary target of the action (e.g. the username being created or the token ID
    /// being revoked), or <c>null</c> if not applicable.
    /// </summary>
    public string? Target { get; set; }

    /// <summary>The IP address of the client that triggered the action, or <c>null</c> if not available.</summary>
    public string? Ip { get; set; }

    /// <summary>The UTC timestamp at which this audit event occurred.</summary>
    public DateTime Timestamp { get; set; }
}
