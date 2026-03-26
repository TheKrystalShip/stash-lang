using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Konscious.Security.Cryptography;
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
/// Passwords are hashed with Argon2id using OWASP-recommended parameters (m=19456, t=2, p=1)
/// and a random 128-bit salt, stored in PHC string format.
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
    /// Authenticates by verifying the password against the stored Argon2id hash.
    /// </summary>
    /// <param name="username">The username to look up in the local database.</param>
    /// <param name="password">The plaintext password provided by the user at login.</param>
    /// <returns>
    /// <see langword="true"/> if the user exists and the password matches the stored
    /// Argon2id hash; <see langword="false"/> otherwise.
    /// </returns>
    public async Task<bool> AuthenticateAsync(string username, string password)
    {
        var user = await _db.GetUserAsync(username);
        if (user == null)
        {
            return false;
        }

        return VerifyPassword(password, user.PasswordHash);
    }

    /// <summary>
    /// Creates a new user with an Argon2id-hashed password.
    /// </summary>
    /// <param name="username">The desired username for the new account. Must be unique.</param>
    /// <param name="password">The plaintext password; hashed with Argon2id before storage.</param>
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

    /// <summary>
    /// Hashes a password with Argon2id using a random 128-bit salt and OWASP-recommended parameters.
    /// Returns a PHC-format string: $argon2id$v=19$m=19456,t=2,p=1$&lt;base64-salt&gt;$&lt;base64-hash&gt;
    /// </summary>
    private static string HashPassword(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = ComputeArgon2id(password, salt);
        string saltB64 = Convert.ToBase64String(salt);
        string hashB64 = Convert.ToBase64String(hash);
        return $"$argon2id$v=19$m=19456,t=2,p=1${saltB64}${hashB64}";
    }

    /// <summary>
    /// Verifies a password against a stored PHC-format Argon2id hash.
    /// </summary>
    private static bool VerifyPassword(string password, string storedHash)
    {
        // PHC format: $argon2id$v=19$m=19456,t=2,p=1$<salt>$<hash>
        string[] parts = storedHash.Split('$');
        if (parts.Length != 6 || parts[1] != "argon2id" || parts[2] != "v=19")
        {
            return false;
        }

        try
        {
            byte[] salt = Convert.FromBase64String(parts[4]);
            byte[] expectedHash = Convert.FromBase64String(parts[5]);
            byte[] computedHash = ComputeArgon2id(password, salt, parts[3]);

            return CryptographicOperations.FixedTimeEquals(computedHash, expectedHash);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            return false;
        }
    }

    /// <summary>
    /// Computes an Argon2id hash with the given salt, using parameters from the PHC params string
    /// or OWASP defaults (m=19456, t=2, p=1).
    /// </summary>
    private static byte[] ComputeArgon2id(string password, byte[] salt, string? paramsString = null)
    {
        int memorySize = 19456;
        int iterations = 2;
        int parallelism = 1;

        if (paramsString != null)
        {
            foreach (string param in paramsString.Split(','))
            {
                string[] kv = param.Split('=');
                if (kv.Length == 2)
                {
                    switch (kv[0])
                    {
                        case "m": memorySize = int.Parse(kv[1]); break;
                        case "t": iterations = int.Parse(kv[1]); break;
                        case "p": parallelism = int.Parse(kv[1]); break;
                    }
                }
            }
        }

        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password));
        argon2.Salt = salt;
        argon2.MemorySize = memorySize;
        argon2.Iterations = iterations;
        argon2.DegreeOfParallelism = parallelism;

        return argon2.GetBytes(32);
    }
}
