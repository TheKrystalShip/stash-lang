using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Stash.Registry.Database;

namespace Stash.Registry.Auth;

public sealed class LocalAuthProvider : IAuthProvider
{
    private readonly IRegistryDatabase _db;

    public LocalAuthProvider(IRegistryDatabase db)
    {
        _db = db;
    }

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

    public async Task CreateUserAsync(string username, string password)
    {
        if (await _db.GetUserAsync(username) != null)
        {
            throw new InvalidOperationException($"User '{username}' already exists.");
        }

        string hash = HashPassword(password);
        await _db.CreateUserAsync(username, hash, "user");
    }

    public async Task<bool> UserExistsAsync(string username)
    {
        return await _db.GetUserAsync(username) != null;
    }

    // BLOCKED: PA-3 — password hashing algorithm TBD.
    // SHA-256 is insufficient for human-chosen passwords (low entropy, fast to brute-force).
    // Must be replaced with a memory-hard algorithm (argon2id, bcrypt, or scrypt)
    // before production use. This is a temporary dev-only solution.
    private static string HashPassword(string password)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexStringLower(bytes);
    }
}
