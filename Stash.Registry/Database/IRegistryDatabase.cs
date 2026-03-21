using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Stash.Registry.Database.Models;

namespace Stash.Registry.Database;

public interface IRegistryDatabase
{
    // Init
    void Initialize();

    // Package operations
    Task<PackageRecord?> GetPackageAsync(string name);
    Task<VersionRecord?> GetPackageVersionAsync(string name, string version);
    Task<bool> PackageExistsAsync(string name);
    Task<bool> VersionExistsAsync(string name, string version);
    Task CreatePackageAsync(PackageRecord package);
    Task AddVersionAsync(string packageName, VersionRecord version);
    Task DeleteVersionAsync(string packageName, string version);
    Task<SearchResult> SearchPackagesAsync(string query, int page, int pageSize);
    Task<List<string>> GetAllVersionsAsync(string name);
    Task UpdatePackageTimestampAsync(string name);
    Task UpdatePackageLatestAsync(string name, string latest);
    Task UpdatePackageReadmeAsync(string name, string? readme);

    // User operations
    Task<UserRecord?> GetUserAsync(string username);
    Task CreateUserAsync(string username, string passwordHash, string role);
    Task UpdateUserRoleAsync(string username, string role);
    Task DeleteUserAsync(string username);
    Task<List<string>> ListUsersAsync();

    // Token operations
    Task CreateTokenAsync(TokenRecord token);
    Task<TokenRecord?> GetTokenByHashAsync(string tokenHash);
    Task<TokenRecord?> GetTokenByIdAsync(string tokenId);
    Task DeleteTokenAsync(string tokenId);
    Task<List<TokenRecord>> GetUserTokensAsync(string username);
    Task CleanExpiredTokensAsync();

    // Ownership operations
    Task<List<string>> GetOwnersAsync(string packageName);
    Task AddOwnerAsync(string packageName, string username);
    Task RemoveOwnerAsync(string packageName, string username);
    Task<bool> IsOwnerAsync(string packageName, string username);

    // Audit operations
    Task AddAuditEntryAsync(AuditEntry entry);
    Task<SearchResult<AuditEntry>> GetAuditLogAsync(int page, int pageSize, string? packageName, string? action);
}
