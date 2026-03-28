using System.Collections.Generic;
using System.Threading.Tasks;
using Stash.Registry.Database.Models;

namespace Stash.Registry.Database;

/// <summary>
/// Abstraction over the registry's persistent data store.
/// </summary>
/// <remarks>
/// <para>
/// Provides CRUD and query operations for all registry entities: packages, versions,
/// users, API tokens, package ownership, and the audit log. The EF Core implementation
/// is <see cref="StashRegistryDatabase"/>; alternative implementations (e.g. in-memory
/// fakes for testing) may be substituted via dependency injection.
/// </para>
/// <para>
/// All async methods are non-blocking and safe to await on any thread. The
/// <see cref="Initialize"/> method must be called once at application startup before
/// any other member is used.
/// </para>
/// </remarks>
public interface IRegistryDatabase
{
    /// <summary>
    /// Ensures the database schema exists and the data directory is created.
    /// </summary>
    /// <remarks>Must be called once at application startup.</remarks>
    // Init
    void Initialize();

    // Package operations
    /// <summary>
    /// Retrieves a package record by name.
    /// </summary>
    /// <param name="name">The package name to look up.</param>
    /// <returns>The <see cref="PackageRecord"/> if found, or <c>null</c> if no package with that name exists.</returns>
    Task<PackageRecord?> GetPackageAsync(string name);

    /// <summary>
    /// Retrieves a specific version record for a package.
    /// </summary>
    /// <param name="name">The package name.</param>
    /// <param name="version">The exact semantic version string.</param>
    /// <returns>The <see cref="VersionRecord"/> if found, or <c>null</c> if the version does not exist.</returns>
    Task<VersionRecord?> GetPackageVersionAsync(string name, string version);

    /// <summary>
    /// Checks whether a package with the given name exists.
    /// </summary>
    /// <param name="name">The package name to test.</param>
    /// <returns><c>true</c> if the package exists; otherwise <c>false</c>.</returns>
    Task<bool> PackageExistsAsync(string name);

    /// <summary>
    /// Checks whether a specific version of a package exists.
    /// </summary>
    /// <param name="name">The package name.</param>
    /// <param name="version">The exact semantic version string.</param>
    /// <returns><c>true</c> if the version record exists; otherwise <c>false</c>.</returns>
    Task<bool> VersionExistsAsync(string name, string version);

    /// <summary>
    /// Inserts a new package record into the database.
    /// </summary>
    /// <param name="package">The <see cref="PackageRecord"/> to persist.</param>
    Task CreatePackageAsync(PackageRecord package);

    /// <summary>
    /// Appends a new version record to an existing package.
    /// </summary>
    /// <param name="packageName">The name of the owning package.</param>
    /// <param name="version">The <see cref="VersionRecord"/> to persist; its <c>PackageName</c> field is set automatically.</param>
    Task AddVersionAsync(string packageName, VersionRecord version);

    /// <summary>
    /// Deletes a specific version record from the database.
    /// </summary>
    /// <param name="packageName">The package name.</param>
    /// <param name="version">The exact semantic version string to remove.</param>
    Task DeleteVersionAsync(string packageName, string version);

    /// <summary>
    /// Searches packages by name or description and returns a paginated result set.
    /// </summary>
    /// <param name="query">A partial name or description to match (SQL <c>LIKE</c> pattern).</param>
    /// <param name="page">The 1-based page number.</param>
    /// <param name="pageSize">The maximum number of results per page.</param>
    /// <returns>A <see cref="SearchResult"/> with the matching <see cref="PackageRecord"/> list and total count.</returns>
    Task<SearchResult> SearchPackagesAsync(string query, int page, int pageSize);

    /// <summary>
    /// Returns all published version strings for a package, ordered by publish date descending.
    /// </summary>
    /// <param name="name">The package name.</param>
    /// <returns>A list of version strings, newest first.</returns>
    Task<List<string>> GetAllVersionsAsync(string name);

    /// <summary>
    /// Updates the <c>updated_at</c> timestamp of a package to the current UTC time.
    /// </summary>
    /// <param name="name">The package name to touch.</param>
    Task UpdatePackageTimestampAsync(string name);

    /// <summary>
    /// Sets the <c>latest</c> version tag on a package and updates its timestamp.
    /// </summary>
    /// <param name="name">The package name.</param>
    /// <param name="latest">The version string to mark as latest.</param>
    Task UpdatePackageLatestAsync(string name, string latest);

    /// <summary>
    /// Replaces the readme content of a package and updates its timestamp.
    /// </summary>
    /// <param name="name">The package name.</param>
    /// <param name="readme">The new readme text, or <c>null</c> to clear it.</param>
    Task UpdatePackageReadmeAsync(string name, string? readme);

    // Deprecation operations

    /// <summary>
    /// Marks an entire package as deprecated with the supplied message and optional alternative.
    /// </summary>
    /// <param name="name">The package name.</param>
    /// <param name="message">A human-readable deprecation message.</param>
    /// <param name="alternative">The suggested replacement package name, or <c>null</c>.</param>
    /// <param name="deprecatedBy">The username of the user performing the deprecation.</param>
    Task DeprecatePackageAsync(string name, string message, string? alternative, string deprecatedBy);

    /// <summary>
    /// Removes the deprecation status from a package.
    /// </summary>
    /// <param name="name">The package name to undeprecate.</param>
    Task UndeprecatePackageAsync(string name);

    /// <summary>
    /// Marks a specific version of a package as deprecated.
    /// </summary>
    /// <param name="name">The package name.</param>
    /// <param name="version">The version string to deprecate.</param>
    /// <param name="message">A human-readable deprecation message.</param>
    /// <param name="deprecatedBy">The username of the user performing the deprecation.</param>
    Task DeprecateVersionAsync(string name, string version, string message, string deprecatedBy);

    /// <summary>
    /// Removes the deprecation status from a specific version.
    /// </summary>
    /// <param name="name">The package name.</param>
    /// <param name="version">The version string to undeprecate.</param>
    Task UndeprecateVersionAsync(string name, string version);

    // User operations
    /// <summary>
    /// Retrieves a user record by username.
    /// </summary>
    /// <param name="username">The username to look up.</param>
    /// <returns>The <see cref="UserRecord"/> if found, or <c>null</c> if the user does not exist.</returns>
    Task<UserRecord?> GetUserAsync(string username);

    /// <summary>
    /// Creates a new user record with the supplied credentials and role.
    /// </summary>
    /// <param name="username">The unique username.</param>
    /// <param name="passwordHash">The pre-hashed password (e.g. bcrypt).</param>
    /// <param name="role">The initial role — typically <c>"user"</c> or <c>"admin"</c>.</param>
    Task CreateUserAsync(string username, string passwordHash, string role);

    /// <summary>
    /// Updates the role of an existing user.
    /// </summary>
    /// <param name="username">The username whose role is being changed.</param>
    /// <param name="role">The new role string, e.g. <c>"admin"</c>.</param>
    Task UpdateUserRoleAsync(string username, string role);

    /// <summary>
    /// Deletes a user account and its associated ownership entries.
    /// </summary>
    /// <param name="username">The username to remove.</param>
    Task DeleteUserAsync(string username);

    /// <summary>
    /// Returns an alphabetically ordered list of all registered usernames.
    /// </summary>
    /// <returns>A list of username strings.</returns>
    Task<List<string>> ListUsersAsync();

    // Token operations
    /// <summary>
    /// Persists a new API token record.
    /// </summary>
    /// <param name="token">The <see cref="TokenRecord"/> to store.</param>
    Task CreateTokenAsync(TokenRecord token);

    /// <summary>
    /// Looks up a token record by its SHA-256 hash.
    /// </summary>
    /// <param name="tokenHash">The SHA-256 hex digest of the raw token.</param>
    /// <returns>The matching <see cref="TokenRecord"/>, or <c>null</c> if not found.</returns>
    Task<TokenRecord?> GetTokenByHashAsync(string tokenHash);

    /// <summary>
    /// Looks up a token record by its unique identifier.
    /// </summary>
    /// <param name="tokenId">The token GUID.</param>
    /// <returns>The matching <see cref="TokenRecord"/>, or <c>null</c> if not found.</returns>
    Task<TokenRecord?> GetTokenByIdAsync(string tokenId);

    /// <summary>
    /// Deletes a token record by its unique identifier.
    /// </summary>
    /// <param name="tokenId">The token GUID to remove.</param>
    Task DeleteTokenAsync(string tokenId);

    /// <summary>
    /// Returns all token records associated with a given user.
    /// </summary>
    /// <param name="username">The username whose tokens should be returned.</param>
    /// <returns>A list of <see cref="TokenRecord"/> objects.</returns>
    Task<List<TokenRecord>> GetUserTokensAsync(string username);

    /// <summary>
    /// Removes all token records whose <c>expires_at</c> timestamp is in the past.
    /// </summary>
    Task CleanExpiredTokensAsync();

    // Refresh token operations

    /// <summary>
    /// Persists a new refresh token record.
    /// </summary>
    /// <param name="token">The <see cref="RefreshTokenRecord"/> to store.</param>
    Task CreateRefreshTokenAsync(RefreshTokenRecord token);

    /// <summary>
    /// Looks up a refresh token record by its SHA-256 hash.
    /// </summary>
    /// <param name="tokenHash">The SHA-256 hex digest of the refresh token.</param>
    /// <returns>The matching <see cref="RefreshTokenRecord"/>, or <c>null</c> if not found.</returns>
    Task<RefreshTokenRecord?> GetRefreshTokenByHashAsync(string tokenHash);

    /// <summary>
    /// Atomically marks a refresh token as consumed. Returns <c>true</c> if the
    /// token was successfully consumed, or <c>false</c> if it was already consumed.
    /// </summary>
    /// <param name="id">The refresh token ID to mark as consumed.</param>
    /// <returns><c>true</c> if the update affected exactly one row; <c>false</c> otherwise.</returns>
    Task<bool> ConsumeRefreshTokenAsync(string id);

    /// <summary>
    /// Deletes all refresh tokens associated with a specific access token.
    /// </summary>
    /// <param name="accessTokenId">The access token ID whose refresh tokens should be removed.</param>
    Task DeleteRefreshTokensByAccessTokenAsync(string accessTokenId);

    /// <summary>
    /// Returns all refresh token records belonging to a specific token family.
    /// </summary>
    /// <param name="familyId">The family identifier shared across rotations.</param>
    Task<List<RefreshTokenRecord>> GetRefreshTokensByFamilyAsync(string familyId);

    /// <summary>
    /// Deletes all refresh tokens belonging to a specific token family.
    /// </summary>
    /// <param name="familyId">The family identifier shared across rotations.</param>
    Task DeleteRefreshTokensByFamilyAsync(string familyId);

    /// <summary>
    /// Deletes all refresh tokens for a given user.
    /// </summary>
    /// <param name="username">The username whose refresh tokens should be removed.</param>
    Task DeleteUserRefreshTokensAsync(string username);

    /// <summary>
    /// Removes all refresh token records that have expired.
    /// </summary>
    Task CleanExpiredRefreshTokensAsync();

    // Ownership operations
    /// <summary>
    /// Returns the list of owners for a package, ordered alphabetically.
    /// </summary>
    /// <param name="packageName">The package name.</param>
    /// <returns>A list of usernames that own the package.</returns>
    Task<List<string>> GetOwnersAsync(string packageName);

    /// <summary>
    /// Adds a user as an owner of a package (no-op if already an owner).
    /// </summary>
    /// <param name="packageName">The package name.</param>
    /// <param name="username">The username to add as an owner.</param>
    Task AddOwnerAsync(string packageName, string username);

    /// <summary>
    /// Removes an owner from a package (no-op if the user is not an owner).
    /// </summary>
    /// <param name="packageName">The package name.</param>
    /// <param name="username">The username to remove from the owner list.</param>
    Task RemoveOwnerAsync(string packageName, string username);

    /// <summary>
    /// Checks whether a user is an owner of a package.
    /// </summary>
    /// <param name="packageName">The package name.</param>
    /// <param name="username">The username to test.</param>
    /// <returns><c>true</c> if the user owns the package; otherwise <c>false</c>.</returns>
    Task<bool> IsOwnerAsync(string packageName, string username);

    // Audit operations
    /// <summary>
    /// Appends a new audit entry to the audit log.
    /// </summary>
    /// <param name="entry">The <see cref="AuditEntry"/> to persist.</param>
    Task AddAuditEntryAsync(AuditEntry entry);

    /// <summary>
    /// Returns a paginated, filtered view of the audit log, ordered by timestamp descending.
    /// </summary>
    /// <param name="page">The 1-based page number.</param>
    /// <param name="pageSize">The maximum number of entries per page.</param>
    /// <param name="packageName">Optional package name filter; pass <c>null</c> to include all packages.</param>
    /// <param name="action">Optional action string filter; pass <c>null</c> to include all actions.</param>
    /// <returns>A <see cref="SearchResult{T}"/> of <see cref="AuditEntry"/> items with the total count.</returns>
    Task<SearchResult<AuditEntry>> GetAuditLogAsync(int page, int pageSize, string? packageName, string? action);
}
