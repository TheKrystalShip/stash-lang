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

    /// <summary>
    /// Creates a new user, assigning role=admin if this is the first user, otherwise role=user.
    /// The assignment is performed inside a database transaction to prevent races.
    /// </summary>
    /// <returns>The role assigned: "admin" or "user".</returns>
    Task<string> CreateUserBootstrappingAdminAsync(string username, string password);

    /// <summary>
    /// Creates a new user and atomically provisions the <c>@&lt;username&gt;</c> personal scope
    /// in a single database transaction. Throws <see cref="InvalidOperationException"/> if the
    /// username already exists as a user or if a scope of the same name exists (user, org, or system).
    /// </summary>
    /// <returns>The role assigned: "admin" (first user) or "user".</returns>
    Task<string> CreateUserWithScopeAsync(string username, string password);
}
