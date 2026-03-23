using System;

namespace Stash.Registry.Database.Models;

/// <summary>
/// Database entity representing a registered user of the registry.
/// </summary>
/// <remarks>
/// The username serves as the primary key. Passwords are stored as bcrypt hashes —
/// the plaintext password is never persisted. The <see cref="Role"/> field controls
/// access level: <c>"user"</c> for standard publish access, <c>"admin"</c> for
/// administrative endpoints. Column names use <c>snake_case</c>.
/// </remarks>
public sealed class UserRecord
{
    /// <summary>The unique username (primary key, mapped to <c>username</c> column).</summary>
    public string Username { get; set; } = "";

    /// <summary>The bcrypt hash of the user's password (required; plaintext is never stored).</summary>
    public string PasswordHash { get; set; } = "";

    /// <summary>
    /// The user's role: <c>"user"</c> (default) or <c>"admin"</c>.
    /// Controls which authorization policies the user satisfies.
    /// </summary>
    public string Role { get; set; } = "user"; // "user" or "admin"

    /// <summary>The UTC timestamp at which the account was created.</summary>
    public DateTime CreatedAt { get; set; }
}
