using System;

namespace Stash.Registry.Database.Models;

/// <summary>
/// Database entity representing a persisted API token issued to a user.
/// </summary>
/// <remarks>
/// Tokens are issued as signed JWTs; only the token's UUID and metadata are stored
/// here — the raw JWT string is returned at creation time and is not re-retrievable.
/// The <see cref="TokenHash"/> field is reserved for future SHA-256 validation flows
/// and is currently stored as an empty string. Expired tokens are cleaned up by
/// <see cref="IRegistryDatabase.CleanExpiredTokensAsync"/>. Column names use
/// <c>snake_case</c>.
/// </remarks>
public sealed class TokenRecord
{
    /// <summary>The unique token identifier (UUID, primary key, mapped to <c>id</c> column).</summary>
    public string Id { get; set; } = "";

    /// <summary>The username of the user who owns this token (foreign key to <see cref="UserRecord.Username"/>).</summary>
    public string Username { get; set; } = "";

    /// <summary>
    /// The SHA-256 hex digest of the raw token value, or an empty string if hash-based
    /// lookup is not in use.
    /// </summary>
    public string TokenHash { get; set; } = ""; // SHA-256 of the actual token

    /// <summary>
    /// The permission scope of this token: <c>"read"</c>, <c>"publish"</c> (default), or
    /// <c>"admin"</c>.
    /// </summary>
    public string Scope { get; set; } = "publish"; // "read", "publish", "admin"

    /// <summary>The UTC timestamp at which this token was created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>The UTC timestamp at which this token expires and should be rejected.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>An optional human-readable description set by the user at token creation time.</summary>
    public string? Description { get; set; }
}
