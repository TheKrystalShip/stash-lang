using System;
using System.Threading.Tasks;

namespace Stash.Registry.Auth;

public interface IAuthProvider
{
    /// <summary>
    /// Authenticate a user with credentials. Returns true if valid.
    /// </summary>
    Task<bool> AuthenticateAsync(string username, string password);

    /// <summary>
    /// Create a new user account. Throws if user already exists or registration disabled.
    /// </summary>
    Task CreateUserAsync(string username, string password);

    /// <summary>
    /// Check if a user exists in the auth backend.
    /// </summary>
    Task<bool> UserExistsAsync(string username);
}
