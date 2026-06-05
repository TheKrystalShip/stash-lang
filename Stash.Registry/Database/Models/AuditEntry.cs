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

    /// <summary>The action type string from <see cref="Services.AuditActions"/>, e.g. <c>"package.publish"</c>, <c>"user.create"</c>, <c>"token.revoke"</c>, <c>"auth.login.success"</c>.</summary>
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

    /// <summary>
    /// The authorization decision: <c>"allow"</c> for successful mutations or
    /// <c>"deny"</c> for authenticated-deny entries. <c>null</c> for legacy entries
    /// written before the PDP seam was introduced.
    /// </summary>
    public string? Decision { get; set; }

    /// <summary>
    /// The typed deny reason when <see cref="Decision"/> is <c>"deny"</c>,
    /// e.g. <c>"PackageRoleInsufficient"</c>. <c>null</c> for allow entries.
    /// </summary>
    public string? DenyReason { get; set; }

    /// <summary>
    /// The <see cref="EntryHash"/> of the immediately preceding hashed audit entry,
    /// or the genesis sentinel string for the first hashed entry in a run.
    /// <c>null</c> when tamper-evidence was disabled at write time (pre-genesis entries).
    /// </summary>
    public string? PreviousHash { get; set; }

    /// <summary>
    /// The HMAC-SHA256 (when a <c>HashSecret</c> is configured) or plain SHA-256 hash of
    /// <c>CanonicalPayload(this) || PreviousHash</c>, computed at write time by
    /// <see cref="Services.AuditChainHasher"/>.
    /// <c>null</c> when tamper-evidence was disabled at write time.
    /// </summary>
    public string? EntryHash { get; set; }
}
