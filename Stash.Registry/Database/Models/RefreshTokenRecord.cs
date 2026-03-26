using System;

namespace Stash.Registry.Database.Models;

/// <summary>
/// Database entity representing a refresh token issued alongside an access token.
/// </summary>
/// <remarks>
/// Refresh tokens are opaque strings stored as SHA-256 hashes in the database.
/// They are bound to a specific machine fingerprint and can only be used to obtain
/// new access/refresh token pairs from the same machine that originally authenticated.
/// Token rotation is enforced: each refresh consumes the current token and issues a new one.
/// </remarks>
public sealed class RefreshTokenRecord
{
    /// <summary>The unique identifier for this refresh token (UUID, primary key).</summary>
    public string Id { get; set; } = "";

    /// <summary>The username of the user who owns this refresh token.</summary>
    public string Username { get; set; } = "";

    /// <summary>The SHA-256 hex digest of the refresh token value.</summary>
    public string TokenHash { get; set; } = "";

    /// <summary>The ID of the associated access token (foreign key to TokenRecord.Id).</summary>
    public string AccessTokenId { get; set; } = "";

    /// <summary>Token family identifier shared across all rotations of a token pair.</summary>
    public string FamilyId { get; set; } = "";

    /// <summary>The SHA-256 hex digest of the client machine fingerprint.</summary>
    public string MachineId { get; set; } = "";

    /// <summary>The permission scope inherited from the access token.</summary>
    public string Scope { get; set; } = "publish";

    /// <summary>The UTC timestamp at which this refresh token was created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>The UTC timestamp at which this refresh token expires.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Whether this refresh token has been consumed (used to get a new token pair).</summary>
    public bool Consumed { get; set; }
}
