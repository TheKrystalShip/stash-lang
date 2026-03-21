using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Stash.Registry.Database.Models;

namespace Stash.Registry.Database;

public sealed class StashRegistryDatabase : IRegistryDatabase
{
    private readonly RegistryDbContext _context;

    public StashRegistryDatabase(RegistryDbContext context)
    {
        _context = context;
    }

    public void Initialize()
    {
        string? connectionString = _context.Database.GetConnectionString();
        if (connectionString != null)
        {
            string? dataSource = connectionString
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
                .FirstOrDefault(part => part.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase));
            if (dataSource != null)
            {
                string dbPath = dataSource["Data Source=".Length..];
                string? dir = Path.GetDirectoryName(dbPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }
        }

        _context.Database.EnsureCreated();
    }

    // ── Package operations ────────────────────────────────────────────────────

    public async Task<PackageRecord?> GetPackageAsync(string name)
    {
        return await _context.Packages.AsNoTracking().FirstOrDefaultAsync(p => p.Name == name);
    }

    public async Task<VersionRecord?> GetPackageVersionAsync(string name, string version)
    {
        return await _context.Versions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.PackageName == name && v.Version == version);
    }

    public async Task<bool> PackageExistsAsync(string name)
    {
        return await _context.Packages.AnyAsync(p => p.Name == name);
    }

    public async Task<bool> VersionExistsAsync(string name, string version)
    {
        return await _context.Versions.AnyAsync(v => v.PackageName == name && v.Version == version);
    }

    public async Task CreatePackageAsync(PackageRecord package)
    {
        _context.Packages.Add(package);
        await _context.SaveChangesAsync();
    }

    public async Task AddVersionAsync(string packageName, VersionRecord version)
    {
        version.PackageName = packageName;
        _context.Versions.Add(version);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteVersionAsync(string packageName, string version)
    {
        var record = await _context.Versions.FindAsync(packageName, version);
        if (record != null)
        {
            _context.Versions.Remove(record);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<SearchResult> SearchPackagesAsync(string query, int page, int pageSize)
    {
        string pattern = $"%{query}%";
        var queryable = _context.Packages.Where(p =>
            EF.Functions.Like(p.Name, pattern) ||
            EF.Functions.Like(p.Description ?? "", pattern));

        int totalCount = await queryable.CountAsync();
        var packages = await queryable
            .OrderBy(p => p.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync();

        return new SearchResult { Packages = packages, TotalCount = totalCount };
    }

    public async Task<List<string>> GetAllVersionsAsync(string name)
    {
        return await _context.Versions
            .AsNoTracking()
            .Where(v => v.PackageName == name)
            .OrderByDescending(v => v.PublishedAt)
            .Select(v => v.Version)
            .ToListAsync();
    }

    public async Task UpdatePackageTimestampAsync(string name)
    {
        var package = await _context.Packages.FindAsync(name);
        if (package != null)
        {
            package.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    public async Task UpdatePackageLatestAsync(string name, string latest)
    {
        var package = await _context.Packages.FindAsync(name);
        if (package != null)
        {
            package.Latest = latest;
            package.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    public async Task UpdatePackageReadmeAsync(string name, string? readme)
    {
        var package = await _context.Packages.FindAsync(name);
        if (package != null)
        {
            package.Readme = readme;
            package.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    // ── User operations ───────────────────────────────────────────────────────

    public async Task<UserRecord?> GetUserAsync(string username)
    {
        return await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == username);
    }

    public async Task CreateUserAsync(string username, string passwordHash, string role)
    {
        var user = new UserRecord
        {
            Username = username,
            PasswordHash = passwordHash,
            Role = role,
            CreatedAt = DateTime.UtcNow
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateUserRoleAsync(string username, string role)
    {
        var user = await _context.Users.FindAsync(username);
        if (user != null)
        {
            user.Role = role;
            await _context.SaveChangesAsync();
        }
    }

    public async Task DeleteUserAsync(string username)
    {
        // Owners have no FK to users, so remove manually.
        // Tokens cascade via FK, so EF handles those automatically.
        var ownerEntries = await _context.Owners.Where(o => o.Username == username).ToListAsync();
        _context.Owners.RemoveRange(ownerEntries);

        var user = await _context.Users.FindAsync(username);
        if (user != null)
        {
            _context.Users.Remove(user);
        }

        await _context.SaveChangesAsync();
    }

    public async Task<List<string>> ListUsersAsync()
    {
        return await _context.Users
            .AsNoTracking()
            .OrderBy(u => u.Username)
            .Select(u => u.Username)
            .ToListAsync();
    }

    // ── Token operations ──────────────────────────────────────────────────────

    public async Task CreateTokenAsync(TokenRecord token)
    {
        _context.Tokens.Add(token);
        await _context.SaveChangesAsync();
    }

    public async Task<TokenRecord?> GetTokenByHashAsync(string tokenHash)
    {
        return await _context.Tokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == tokenHash);
    }

    public async Task<TokenRecord?> GetTokenByIdAsync(string tokenId)
    {
        return await _context.Tokens.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tokenId);
    }

    public async Task DeleteTokenAsync(string tokenId)
    {
        var token = await _context.Tokens.FindAsync(tokenId);
        if (token != null)
        {
            _context.Tokens.Remove(token);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<List<TokenRecord>> GetUserTokensAsync(string username)
    {
        return await _context.Tokens.AsNoTracking().Where(t => t.Username == username).ToListAsync();
    }

    public async Task CleanExpiredTokensAsync()
    {
        var expired = await _context.Tokens.Where(t => t.ExpiresAt < DateTime.UtcNow).ToListAsync();
        _context.Tokens.RemoveRange(expired);
        await _context.SaveChangesAsync();
    }

    // ── Ownership operations ──────────────────────────────────────────────────

    public async Task<List<string>> GetOwnersAsync(string packageName)
    {
        return await _context.Owners
            .AsNoTracking()
            .Where(o => o.PackageName == packageName)
            .OrderBy(o => o.Username)
            .Select(o => o.Username)
            .ToListAsync();
    }

    public async Task AddOwnerAsync(string packageName, string username)
    {
        bool exists = await _context.Owners.AnyAsync(o => o.PackageName == packageName && o.Username == username);
        if (!exists)
        {
            _context.Owners.Add(new OwnerEntry { PackageName = packageName, Username = username });
            await _context.SaveChangesAsync();
        }
    }

    public async Task RemoveOwnerAsync(string packageName, string username)
    {
        var entry = await _context.Owners.FindAsync(packageName, username);
        if (entry != null)
        {
            _context.Owners.Remove(entry);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<bool> IsOwnerAsync(string packageName, string username)
    {
        return await _context.Owners.AnyAsync(o => o.PackageName == packageName && o.Username == username);
    }

    // ── Audit operations ──────────────────────────────────────────────────────

    public async Task AddAuditEntryAsync(AuditEntry entry)
    {
        _context.AuditLog.Add(entry);
        await _context.SaveChangesAsync();
    }

    public async Task<SearchResult<AuditEntry>> GetAuditLogAsync(int page, int pageSize, string? packageName, string? action)
    {
        var queryable = _context.AuditLog.AsQueryable();

        if (packageName != null)
        {
            queryable = queryable.Where(e => e.Package == packageName);
        }

        if (action != null)
        {
            queryable = queryable.Where(e => e.Action == action);
        }

        int totalCount = await queryable.CountAsync();
        var items = await queryable
            .OrderByDescending(e => e.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync();

        return new SearchResult<AuditEntry> { Items = items, TotalCount = totalCount };
    }
}
