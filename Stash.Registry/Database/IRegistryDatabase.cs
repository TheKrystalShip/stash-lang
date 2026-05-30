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
    /// Attempts to insert a new package record.  Returns <c>true</c> on success or
    /// <c>false</c> when a unique-constraint violation indicates the row already exists
    /// (insert-then-handle-unique-violation pattern per D20).
    /// The underlying <see cref="DbContext"/> is left in a clean state after a collision.
    /// </summary>
    Task<bool> TryCreatePackageAsync(PackageRecord package);

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

    /// <summary>
    /// Creates a new user, assigning role=admin if this is the first user in the DB,
    /// otherwise role=user. The count and insert are performed inside a single transaction
    /// to prevent race conditions on concurrent first-registration requests.
    /// </summary>
    /// <param name="username">The unique username.</param>
    /// <param name="passwordHash">The pre-hashed password.</param>
    /// <returns>The role assigned: "admin" or "user".</returns>
    /// <exception cref="InvalidOperationException">Thrown if the username already exists.</exception>
    Task<string> CreateUserBootstrappingAdminAsync(string username, string passwordHash);

    /// <summary>
    /// Returns the count of users whose role equals "admin".
    /// </summary>
    Task<long> GetAdminCountAsync();

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

    // Scope operations (P3 — bootstrap + registration provisioning)

    /// <summary>
    /// Returns <c>true</c> if a scope with the given name (without the leading <c>@</c>) exists.
    /// The check covers all owner types: <c>user</c>, <c>org</c>, and <c>system</c>.
    /// </summary>
    /// <param name="name">The bare scope name (e.g. <c>"alice"</c>, <c>"stash"</c>).</param>
    Task<bool> ScopeExistsAsync(string name);

    /// <summary>
    /// Persists a new <see cref="ScopeRecord"/>. Throws if the name is already taken.
    /// </summary>
    /// <param name="scope">The scope record to insert.</param>
    Task CreateScopeAsync(ScopeRecord scope);

    /// <summary>
    /// Retrieves a scope record by its bare name (without the leading <c>@</c>).
    /// </summary>
    /// <param name="name">The bare scope name.</param>
    /// <returns>The <see cref="ScopeRecord"/> if found, or <c>null</c>.</returns>
    Task<ScopeRecord?> GetScopeAsync(string name);

    /// <summary>
    /// Seeds the reserved system scopes (<c>@stash</c> and <c>@admin</c>) if they do not
    /// already exist. Safe to call on every startup — idempotent.
    /// </summary>
    Task SeedSystemScopesAsync();

    /// <summary>
    /// Creates a new user and atomically provisions the <c>@&lt;username&gt;</c> personal scope
    /// in a single database transaction. If the scope name already exists (any owner type)
    /// the operation rolls back and throws <see cref="InvalidOperationException"/> so that the
    /// caller can surface an HTTP 409.
    /// </summary>
    /// <param name="username">The unique username (also becomes the scope name).</param>
    /// <param name="passwordHash">The pre-hashed password.</param>
    /// <returns>The role assigned: <c>"admin"</c> (first user) or <c>"user"</c>.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the username already exists as a user, or when a scope named
    /// <paramref name="username"/> already exists (user, org, or system).
    /// </exception>
    Task<string> CreateUserWithScopeAsync(string username, string passwordHash);

    // Package role operations (replaces the old owner operations — D3 clean break)

    /// <summary>
    /// Assigns a role to a principal on a package, replacing any existing role for that principal.
    /// </summary>
    /// <param name="packageName">The package name.</param>
    /// <param name="principalType">The principal type: <c>user</c>, <c>team</c>, or <c>org</c>.</param>
    /// <param name="principalId">The principal identifier (username, team ID, or org ID).</param>
    /// <param name="role">The role to assign: <c>owner</c>, <c>maintainer</c>, <c>publisher</c>, or <c>reader</c>.</param>
    Task AssignPackageRoleAsync(string packageName, string principalType, string principalId, string role);

    /// <summary>
    /// Revokes the role of a principal on a package (no-op if no role is assigned).
    /// </summary>
    /// <param name="packageName">The package name.</param>
    /// <param name="principalType">The principal type: <c>user</c>, <c>team</c>, or <c>org</c>.</param>
    /// <param name="principalId">The principal identifier.</param>
    Task RevokePackageRoleAsync(string packageName, string principalType, string principalId);

    /// <summary>
    /// Returns all role entries for a package.
    /// </summary>
    /// <param name="packageName">The package name.</param>
    /// <returns>A list of <see cref="PackageRoleEntry"/> objects for the package.</returns>
    Task<List<PackageRoleEntry>> GetPackageRolesAsync(string packageName);

    /// <summary>
    /// Checks whether a user (directly or via a matching principal entry) has at least
    /// the specified role on the package. In P2 only direct user-principal role entries
    /// are checked; team and org inheritance is added in P5.
    /// </summary>
    /// <param name="packageName">The package name.</param>
    /// <param name="username">The username to test.</param>
    /// <param name="role">The minimum role required: <c>owner</c>, <c>maintainer</c>, <c>publisher</c>, or <c>reader</c>.</param>
    /// <returns><c>true</c> if the user has the specified or higher role; otherwise <c>false</c>.</returns>
    Task<bool> HasPackagePermissionAsync(string packageName, string username, string role);

    /// <summary>
    /// Sets the visibility of a package to the specified value.
    /// </summary>
    /// <param name="name">The package name.</param>
    /// <param name="visibility">The new visibility: <c>public</c>, <c>private</c>, or <c>internal</c>.</param>
    Task SetPackageVisibilityAsync(string name, string visibility);

    // Organization operations (P5)

    /// <summary>
    /// Creates a new organization and its associated scope in a single transaction.
    /// The creator is automatically added as an <c>owner</c> of the org and the org's scope.
    /// </summary>
    /// <param name="name">The org name (lower-case, same grammar as a scope name).</param>
    /// <param name="displayName">An optional human-readable display name.</param>
    /// <param name="createdBy">The username of the creating user.</param>
    /// <returns>The newly created <see cref="OrganizationRecord"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a scope or org with the same name already exists.
    /// </exception>
    Task<OrganizationRecord> CreateOrgAsync(string name, string? displayName, string createdBy);

    /// <summary>
    /// Retrieves an organization by its name.
    /// </summary>
    /// <param name="name">The org name (without the leading <c>@</c>).</param>
    /// <returns>The <see cref="OrganizationRecord"/> if found, or <c>null</c>.</returns>
    Task<OrganizationRecord?> GetOrgAsync(string name);

    /// <summary>
    /// Returns all members of an organization.
    /// </summary>
    /// <param name="orgId">The organization identifier.</param>
    /// <returns>A list of <see cref="OrgMemberEntry"/> records.</returns>
    Task<List<OrgMemberEntry>> GetOrgMembersAsync(string orgId);

    /// <summary>
    /// Adds a user to an organization with the specified role.
    /// </summary>
    /// <param name="orgId">The organization identifier.</param>
    /// <param name="username">The username to add.</param>
    /// <param name="orgRole">The role: <c>owner</c> or <c>member</c>.</param>
    /// <exception cref="InvalidOperationException">If the user is already a member.</exception>
    Task AddOrgMemberAsync(string orgId, string username, string orgRole);

    /// <summary>
    /// Removes a user from an organization.
    /// </summary>
    /// <param name="orgId">The organization identifier.</param>
    /// <param name="username">The username to remove.</param>
    Task RemoveOrgMemberAsync(string orgId, string username);

    /// <summary>
    /// Returns whether the given user is an owner of the specified organization.
    /// </summary>
    /// <param name="orgId">The organization identifier.</param>
    /// <param name="username">The username to check.</param>
    Task<bool> IsOrgOwnerAsync(string orgId, string username);

    // Team operations (P5)

    /// <summary>
    /// Creates a new team within an organization.
    /// </summary>
    /// <param name="orgId">The organization identifier.</param>
    /// <param name="name">The team name (unique within the org).</param>
    /// <returns>The newly created <see cref="TeamRecord"/>.</returns>
    /// <exception cref="InvalidOperationException">If a team with the same name already exists in the org.</exception>
    Task<TeamRecord> CreateTeamAsync(string orgId, string name);

    /// <summary>
    /// Retrieves a team by its identifier.
    /// </summary>
    /// <param name="teamId">The team identifier.</param>
    /// <returns>The <see cref="TeamRecord"/> if found, or <c>null</c>.</returns>
    Task<TeamRecord?> GetTeamAsync(string teamId);

    /// <summary>
    /// Retrieves a team by org ID and name.
    /// </summary>
    /// <param name="orgId">The organization identifier.</param>
    /// <param name="name">The team name.</param>
    /// <returns>The <see cref="TeamRecord"/> if found, or <c>null</c>.</returns>
    Task<TeamRecord?> GetTeamByNameAsync(string orgId, string name);

    /// <summary>
    /// Adds a user to a team.
    /// </summary>
    /// <param name="teamId">The team identifier.</param>
    /// <param name="username">The username to add.</param>
    Task AddTeamMemberAsync(string teamId, string username);

    /// <summary>
    /// Searches packages by name or description with visibility filtering.
    /// </summary>
    /// <param name="query">A partial name or description to match (SQL <c>LIKE</c> pattern).</param>
    /// <param name="page">The 1-based page number.</param>
    /// <param name="pageSize">The maximum number of results per page.</param>
    /// <param name="callerUsername">
    /// The authenticated caller's username, or <c>null</c> if unauthenticated. Unauthenticated
    /// callers see only <c>public</c> packages.
    /// </param>
    /// <returns>A <see cref="SearchResult"/> with the matching <see cref="PackageRecord"/> list and total count.</returns>
    Task<SearchResult> SearchPackagesAsync(string query, int page, int pageSize, string? callerUsername);

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
