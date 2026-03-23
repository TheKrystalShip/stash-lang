using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Stash.Registry.Database;

namespace Stash.Registry.Auth;

/// <summary>
/// The built-in <see cref="IAuthProvider"/> implementation that stores user credentials
/// in the local <see cref="Database.IRegistryDatabase"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the default and currently only production-ready authentication backend.
/// It is selected when <c>Registry:Auth:Type</c> is set to <c>"local"</c> (or is absent)
/// in <c>appsettings.json</c>.
/// </para>
/// <para>
/// <b>Password hashing — current state:</b> passwords are hashed with SHA-256
/// (<see cref="System.Security.Cryptography.SHA256.HashData"/>) and stored as lowercase
/// hexadecimal strings. SHA-256 is a cryptographic hash function but is <em>not</em>
/// suitable for storing human-chosen passwords in production because it is fast to compute,
/// making brute-force attacks practical.
/// </para>
/// <para>
/// <b>Planned migration (blocked on PA-3):</b> the hashing algorithm must be replaced with
/// a memory-hard, password-specific KDF — Argon2id, bcrypt, or scrypt — before this
/// provider is used in any internet-facing deployment.
/// </para>
/// <para>
/// After successful authentication the caller (typically a login controller) issues a JWT
/// via <see cref="JwtTokenService.CreateToken"/> and stores the token record in
/// <see cref="Database.IRegistryDatabase"/> for later revocation checks.
/// </para>
/// </remarks>
public sealed class LocalAuthProvider : IAuthProvider
{
    /// <summary>
    /// The registry database used to load and persist user records.
    /// </summary>
    private readonly IRegistryDatabase _db;

    /// <summary>
    /// Initialises a new <see cref="LocalAuthProvider"/> backed by the given database.
    /// </summary>
    /// <param name="db">
    /// The <see cref="Database.IRegistryDatabase"/> instance injected by the DI container.
    /// Used to look up users during authentication and to persist new accounts.
    /// </param>
    public LocalAuthProvider(IRegistryDatabase db)
    {
        _db = db;
    }

    /// <summary>
    /// Authenticates <paramref name="username"/> by hashing <paramref name="password"/>
    /// with SHA-256 and comparing the result against the stored hash.
    /// </summary>
    /// <param name="username">The username to look up in the local database.</param>
    /// <param name="password">The plaintext password provided by the user at login.</param>
    /// <returns>
    /// <see langword="true"/> if the user exists and the SHA-256 hash of
    /// <paramref name="password"/> matches the stored hash; <see langword="false"/> otherwise.
    /// </returns>
    public async Task<bool> AuthenticateAsync(string username, string password)
    {
        var user = await _db.GetUserAsync(username);
        if (user == null)
        {
            return false;
        }

        string hash = HashPassword(password);
        return string.Equals(user.PasswordHash, hash, StringComparison.Ordinal);
    }

    /// <summary>
    /// Creates a new user account in the local database with a SHA-256-hashed password.
    /// </summary>
    /// <param name="username">The desired username for the new account. Must be unique.</param>
    /// <param name="password">The plaintext password; hashed with SHA-256 before storage.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a user with <paramref name="username"/> already exists in the database.
    /// </exception>
    /// <remarks>
    /// The new account is assigned the default role <c>"user"</c>. Administrator accounts
    /// must be promoted separately via the admin API.
    /// </remarks>
    public async Task CreateUserAsync(string username, string password)
    {
        if (await _db.GetUserAsync(username) != null)
        {
            throw new InvalidOperationException($"User '{username}' already exists.");
        }

        string hash = HashPassword(password);
        await _db.CreateUserAsync(username, hash, "user");
    }

    /// <summary>
    /// Checks whether a user account with <paramref name="username"/> exists in the local database.
    /// </summary>
    /// <param name="username">The username to search for.</param>
    /// <returns>
    /// <see langword="true"/> if a matching user record is found; <see langword="false"/> otherwise.
    /// </returns>
    public async Task<bool> UserExistsAsync(string username)
    {
        return await _db.GetUserAsync(username) != null;
    }

    // BLOCKED: PA-3 — password hashing algorithm TBD.
    // SHA-256 is insufficient for human-chosen passwords (low entropy, fast to brute-force).
    // Must be replaced with a memory-hard algorithm (argon2id, bcrypt, or scrypt)
    // before production use. This is a temporary dev-only solution.
    /// <summary>
    /// Computes a SHA-256 hex digest of <paramref name="password"/>.
    /// </summary>
    /// <param name="password">The plaintext password to hash.</param>
    /// <returns>
    /// A 64-character lowercase hexadecimal string representing the SHA-256 digest of
    /// the UTF-8 encoding of <paramref name="password"/>.
    /// </returns>
    /// <remarks>
    /// <b>⚠ Security warning:</b> SHA-256 is a general-purpose hash; it is fast and therefore
    /// unsuitable for password storage against offline brute-force attacks. This method will be
    /// replaced with Argon2id (or an equivalent memory-hard KDF) as part of PA-3.
    /// </remarks>
    private static string HashPassword(string password)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexStringLower(bytes);
    }
}
