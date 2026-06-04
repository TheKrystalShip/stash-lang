using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Stash.Registry.Auth;
using Stash.Registry.Contracts;
using Stash.Registry.Database.Models;

namespace Stash.Registry.Database;

/// <summary>
/// EF Core implementation of <see cref="IRegistryDatabase"/> backed by SQLite.
/// </summary>
/// <remarks>
/// <para>
/// All queries use <c>AsNoTracking()</c> for read operations to avoid unnecessary
/// change-tracking overhead. Write operations call <c>SaveChangesAsync()</c> immediately
/// after each mutation so that the database is always in a consistent state.
/// </para>
/// <para>
/// The <see cref="Initialize"/> method must be called once at startup to ensure the
/// SQLite data directory exists and <c>EnsureCreated()</c> has run.
/// </para>
/// </remarks>
public sealed class StashRegistryDatabase : IRegistryDatabase
{
    private readonly RegistryDbContext _context;

    /// <summary>
    /// Initialises the database implementation with an EF Core context.
    /// </summary>
    /// <param name="context">The <see cref="RegistryDbContext"/> to use for all queries.</param>
    public StashRegistryDatabase(RegistryDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Creates the SQLite data directory if it does not exist and ensures the schema
    /// is up to date by calling <see cref="DatabaseFacade.EnsureCreated"/>.
    /// </summary>
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

    /// <inheritdoc/>
    public async Task<PackageRecord?> GetPackageAsync(string name)
    {
        return await _context.Packages.AsNoTracking().FirstOrDefaultAsync(p => p.Name == name);
    }

    /// <inheritdoc/>
    public async Task<VersionRecord?> GetPackageVersionAsync(string name, string version)
    {
        return await _context.Versions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.PackageName == name && v.Version == version);
    }

    /// <inheritdoc/>
    public async Task<bool> PackageExistsAsync(string name)
    {
        return await _context.Packages.AnyAsync(p => p.Name == name);
    }

    /// <inheritdoc/>
    public async Task<bool> VersionExistsAsync(string name, string version)
    {
        return await _context.Versions.AnyAsync(v => v.PackageName == name && v.Version == version);
    }

    /// <inheritdoc/>
    public async Task CreatePackageAsync(PackageRecord package)
    {
        _context.Packages.Add(package);
        await _context.SaveChangesAsync();
    }

    /// <inheritdoc/>
    public async Task<bool> TryCreatePackageAsync(PackageRecord package)
    {
        _context.Packages.Add(package);
        try
        {
            await _context.SaveChangesAsync();
            return true;
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
            when (ex.InnerException is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 19 }
                  || (ex.InnerException?.Message?.Contains("UNIQUE constraint failed") ?? false)
                  || (ex.InnerException?.Message?.Contains("unique constraint") ?? false))
        {
            // Unique constraint violation: another concurrent insert won the race.
            // Detach the failed entity to leave the context in a clean state.
            _context.Entry(package).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task AddVersionAsync(string packageName, VersionRecord version)
    {
        version.PackageName = packageName;
        _context.Versions.Add(version);
        await _context.SaveChangesAsync();
    }

    /// <inheritdoc/>
    public async Task DeleteVersionAsync(string packageName, string version)
    {
        var record = await _context.Versions.FindAsync(packageName, version);
        if (record != null)
        {
            _context.Versions.Remove(record);
            await _context.SaveChangesAsync();
        }
    }

    /// <inheritdoc/>
    public async Task<SearchResult> SearchPackagesAsync(
        string query,
        int page,
        int pageSize,
        string? callerUsername,
        string? keyword = null,
        string? license = null,
        bool? deprecated = null,
        string? owner = null,
        PackageSortOrder sort = PackageSortOrder.Relevance)
    {
        string pattern = $"%{query}%";
        var queryable = _context.Packages.Where(p =>
            EF.Functions.Like(p.Name, pattern) ||
            EF.Functions.Like(p.Description ?? "", pattern));

        // Visibility filter: unauthenticated callers see only public packages;
        // authenticated callers also see private/internal packages they have at least reader on.
        if (callerUsername == null)
        {
            queryable = queryable.Where(p => p.Visibility == Visibilities.Public);
        }
        else
        {
            // Include public packages + private/internal packages the caller can read.
            // Access is determined by the PDP permission resolver's three branches:
            //   1. Direct user-principal role
            //   2. Team-mediated role (caller is a member of a team with a role on the package)
            //   3. Org-mediated: the package's scope is org-owned AND the caller is an org member
            //      (org_member→reader floor), OR an explicit org-principal role row exists.
            // internal and private are treated identically here (matching today's controller);
            // the internal-specific shortcut is tracked in F04.
            queryable = queryable.Where(p =>
                p.Visibility == Visibilities.Public ||
                // Branch 1: direct user role
                _context.PackageRoles.Any(r =>
                    r.PackageName == p.Name &&
                    r.PrincipalType == PrincipalTypes.User &&
                    r.PrincipalId == callerUsername) ||
                // Branch 2: team-mediated role
                _context.PackageRoles.Any(r =>
                    r.PackageName == p.Name &&
                    r.PrincipalType == PrincipalTypes.Team &&
                    _context.TeamMembers.Any(tm =>
                        tm.TeamId == r.PrincipalId &&
                        tm.Username == callerUsername)) ||
                // Branch 3: org-mediated (scope is org-owned; caller is org member OR explicit org role)
                _context.Scopes.Any(s =>
                    s.OwnerType == ScopeOwnerTypes.Org &&
                    s.OwnerOrgId != null &&
                    p.Name.StartsWith("@" + s.Name + "/") &&
                    (_context.OrgMembers.Any(m =>
                        m.OrgId == s.OwnerOrgId &&
                        m.Username == callerUsername) ||
                     _context.PackageRoles.Any(r =>
                        r.PackageName == p.Name &&
                        r.PrincipalType == PrincipalTypes.Org &&
                        r.PrincipalId == s.OwnerOrgId))) ||
                // Branch 4: internal + user-owned scope: caller is the scope owner (brief branch (b))
                (p.Visibility == Visibilities.Internal &&
                    _context.Scopes.Any(s =>
                        s.OwnerType == ScopeOwnerTypes.User &&
                        s.OwnerUsername == callerUsername &&
                        p.Name.StartsWith("@" + s.Name + "/"))));
        }

        // ── Bucket-A filters (column-backed, compose with AND) ────────────────

        // keyword: exact match against one element of the JSON-array keywords string.
        // The keywords column stores a JSON array (e.g. '["foo","bar"]'); we match
        // the quoted substring "%\"<keyword>\"%" to avoid false partial-word hits.
        //
        // The keyword is escaped so that LIKE metacharacters (%, _, \) in the input
        // are treated as literals — preventing over-matching.  The ESCAPE '\' clause
        // is supported by both SQLite and PostgreSQL.
        if (!string.IsNullOrEmpty(keyword))
        {
            string esc = keyword
                .Replace("\\", "\\\\")
                .Replace("%", "\\%")
                .Replace("_", "\\_")
                .Replace("\"", "\\\"");
            queryable = queryable.Where(p =>
                p.Keywords != null &&
                EF.Functions.Like(p.Keywords, $"%\"{esc}\"%", "\\"));
        }

        // license: exact match against PackageRecord.License (SPDX identifier).
        if (!string.IsNullOrEmpty(license))
        {
            string lic = license;
            queryable = queryable.Where(p => p.License == lic);
        }

        // deprecated: boolean filter.
        if (deprecated.HasValue)
        {
            bool dep = deprecated.Value;
            queryable = queryable.Where(p => p.Deprecated == dep);
        }

        // owner: exact match against a user principal with Owner role on the package.
        // Uses PrincipalTypes.User and PackageRoles.Owner — named enum values, never literals.
        if (!string.IsNullOrEmpty(owner))
        {
            string ownerName = owner;
            queryable = queryable.Where(p =>
                _context.PackageRoles.Any(r =>
                    r.PackageName == p.Name &&
                    r.PrincipalType == PrincipalTypes.User &&
                    r.Role == PackageRoles.Owner &&
                    r.PrincipalId == ownerName));
        }

        int totalCount = await queryable.CountAsync();

        // ── Sort order ────────────────────────────────────────────────────────
        // Relevance (default) and Name both order by Name ascending — matching the
        // pre-P5 behavior so the q/page/pageSize-only path remains byte-identical.

        // ── Project to PackageSearchRow — single query, no N+1 ────────────────
        // ownerCount is a correlated-subquery count in the SELECT so we never issue
        // a separate round-trip per result row.
        var rowsQuery = sort switch
        {
            PackageSortOrder.Name => queryable
                .OrderBy(p => p.Name),
            PackageSortOrder.Updated => queryable
                .OrderByDescending(p => p.UpdatedAt)
                .ThenBy(p => p.Name),
            PackageSortOrder.Published => queryable
                .OrderByDescending(p => p.CreatedAt)
                .ThenBy(p => p.Name),
            _ => queryable   // Relevance (default)
                .OrderBy(p => p.Name),
        };

        var rows = await rowsQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .Select(p => new PackageSearchRow
            {
                Package = p,
                OwnerCount = _context.PackageRoles.Count(r =>
                    r.PackageName == p.Name &&
                    r.PrincipalType == PrincipalTypes.User &&
                    r.Role == PackageRoles.Owner),
            })
            .ToListAsync();

        return new SearchResult { Packages = rows, TotalCount = totalCount };
    }

    /// <inheritdoc/>
    public async Task SetPackageVisibilityAsync(string name, string visibility)
    {
        var package = await _context.Packages.FindAsync(name);
        if (package != null)
        {
            package.Visibility = visibility.ToVisibility();
            package.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    /// <inheritdoc/>
    public async Task<List<string>> GetAllVersionsAsync(string name)
    {
        return await _context.Versions
            .AsNoTracking()
            .Where(v => v.PackageName == name)
            .OrderByDescending(v => v.PublishedAt)
            .Select(v => v.Version)
            .ToListAsync();
    }

    /// <inheritdoc/>
    public async Task<SearchResult<VersionRecord>> GetPackageVersionsAsync(string name, int page, int pageSize)
    {
        var queryable = _context.Versions
            .AsNoTracking()
            .Where(v => v.PackageName == name);

        int totalCount = await queryable.CountAsync();
        var items = await queryable
            .OrderByDescending(v => v.PublishedAt)
            .ThenByDescending(v => v.Version)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new SearchResult<VersionRecord> { Items = items, TotalCount = totalCount };
    }

    /// <inheritdoc/>
    public async Task UpdatePackageTimestampAsync(string name)
    {
        var package = await _context.Packages.FindAsync(name);
        if (package != null)
        {
            package.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    // ── Deprecation operations ─────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task DeprecatePackageAsync(string name, string message, string? alternative, string deprecatedBy)
    {
        var package = await _context.Packages.FindAsync(name);
        if (package != null)
        {
            package.Deprecated = true;
            package.DeprecationMessage = message;
            package.DeprecationAlternative = alternative;
            package.DeprecatedBy = deprecatedBy;
            package.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    /// <inheritdoc/>
    public async Task UndeprecatePackageAsync(string name)
    {
        var package = await _context.Packages.FindAsync(name);
        if (package != null)
        {
            package.Deprecated = false;
            package.DeprecationMessage = null;
            package.DeprecationAlternative = null;
            package.DeprecatedBy = null;
            package.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    /// <inheritdoc/>
    public async Task DeprecateVersionAsync(string name, string version, string message, string deprecatedBy)
    {
        var record = await _context.Versions.FindAsync(name, version);
        if (record != null)
        {
            record.Deprecated = true;
            record.DeprecationMessage = message;
            record.DeprecatedBy = deprecatedBy;
            // Bump the parent package timestamp so the /versions ETag reflects this change.
            var package = await _context.Packages.FindAsync(name);
            if (package != null)
                package.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    /// <inheritdoc/>
    public async Task UndeprecateVersionAsync(string name, string version)
    {
        var record = await _context.Versions.FindAsync(name, version);
        if (record != null)
        {
            record.Deprecated = false;
            record.DeprecationMessage = null;
            record.DeprecatedBy = null;
            // Bump the parent package timestamp so the /versions ETag reflects this change.
            var package = await _context.Packages.FindAsync(name);
            if (package != null)
                package.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    // ── User operations ───────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<UserRecord?> GetUserAsync(string username)
    {
        return await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == username);
    }

    /// <inheritdoc/>
    public async Task CreateUserAsync(string username, string passwordHash, string role)
    {
        var user = new UserRecord
        {
            Username = username,
            PasswordHash = passwordHash,
            Role = role.ToUserRole(),
            CreatedAt = DateTime.UtcNow
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
    }

    /// <inheritdoc/>
    public async Task UpdateUserRoleAsync(string username, string role)
    {
        var user = await _context.Users.FindAsync(username);
        if (user != null)
        {
            user.Role = role.ToUserRole();
            await _context.SaveChangesAsync();
        }
    }

    /// <inheritdoc/>
    public async Task DeleteUserAsync(string username)
    {
        // Remove user's direct package role entries (no FK to users).
        // Tokens cascade via FK, so EF handles those automatically.
        var roleEntries = await _context.PackageRoles
            .Where(r => r.PrincipalType == PrincipalTypes.User && r.PrincipalId == username)
            .ToListAsync();
        _context.PackageRoles.RemoveRange(roleEntries);

        var user = await _context.Users.FindAsync(username);
        if (user != null)
        {
            _context.Users.Remove(user);
        }

        await _context.SaveChangesAsync();
    }

    /// <inheritdoc/>
    public async Task<List<string>> ListUsersAsync()
    {
        return await _context.Users
            .AsNoTracking()
            .OrderBy(u => u.Username)
            .Select(u => u.Username)
            .ToListAsync();
    }

    /// <inheritdoc/>
    public async Task<string> CreateUserBootstrappingAdminAsync(string username, string passwordHash)
    {
        using var tx = await _context.Database.BeginTransactionAsync();
        var exists = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (exists is not null)
            throw new InvalidOperationException($"User '{username}' already exists.");

        long count = await _context.Users.LongCountAsync();
        UserRoles roleEnum = count == 0 ? UserRoles.Admin : UserRoles.User;
        _context.Users.Add(new UserRecord
        {
            Username = username,
            PasswordHash = passwordHash,
            Role = roleEnum,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        await tx.CommitAsync();
        return roleEnum.ToWire();
    }

    /// <inheritdoc/>
    public async Task<long> GetAdminCountAsync()
    {
        return await _context.Users.LongCountAsync(u => u.Role == UserRoles.Admin);
    }

    // ── Token operations ──────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task CreateTokenAsync(TokenRecord token)
    {
        _context.Tokens.Add(token);
        await _context.SaveChangesAsync();
    }

    /// <inheritdoc/>
    public async Task<TokenRecord?> GetTokenByHashAsync(string tokenHash)
    {
        return await _context.Tokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == tokenHash);
    }

    /// <inheritdoc/>
    public async Task<TokenRecord?> GetTokenByIdAsync(string tokenId)
    {
        return await _context.Tokens.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tokenId);
    }

    /// <inheritdoc/>
    public async Task DeleteTokenAsync(string tokenId)
    {
        var token = await _context.Tokens.FindAsync(tokenId);
        if (token != null)
        {
            _context.Tokens.Remove(token);
            await _context.SaveChangesAsync();
        }
    }

    /// <inheritdoc/>
    public async Task<List<TokenRecord>> GetUserTokensAsync(string username)
    {
        return await _context.Tokens.AsNoTracking().Where(t => t.Username == username).ToListAsync();
    }

    /// <inheritdoc/>
    public async Task CleanExpiredTokensAsync()
    {
        var expired = await _context.Tokens.Where(t => t.ExpiresAt < DateTime.UtcNow).ToListAsync();
        _context.Tokens.RemoveRange(expired);
        await _context.SaveChangesAsync();
    }

    // ── Refresh token operations ──────────────────────────────────────────

    /// <inheritdoc/>
    public async Task CreateRefreshTokenAsync(RefreshTokenRecord token)
    {
        _context.RefreshTokens.Add(token);
        await _context.SaveChangesAsync();
    }

    /// <inheritdoc/>
    public async Task<RefreshTokenRecord?> GetRefreshTokenByHashAsync(string tokenHash)
    {
        return await _context.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == tokenHash);
    }

    /// <inheritdoc/>
    public async Task<bool> ConsumeRefreshTokenAsync(string id)
    {
        int rows = await _context.RefreshTokens
            .Where(t => t.Id == id && !t.Consumed)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Consumed, true));
        return rows > 0;
    }

    /// <inheritdoc/>
    public async Task DeleteRefreshTokensByAccessTokenAsync(string accessTokenId)
    {
        await _context.RefreshTokens.Where(t => t.AccessTokenId == accessTokenId).ExecuteDeleteAsync();
    }

    /// <inheritdoc/>
    public async Task<List<RefreshTokenRecord>> GetRefreshTokensByFamilyAsync(string familyId)
    {
        return await _context.RefreshTokens.AsNoTracking().Where(t => t.FamilyId == familyId).ToListAsync();
    }

    /// <inheritdoc/>
    public async Task DeleteRefreshTokensByFamilyAsync(string familyId)
    {
        await _context.RefreshTokens.Where(t => t.FamilyId == familyId).ExecuteDeleteAsync();
    }

    /// <inheritdoc/>
    public async Task DeleteUserRefreshTokensAsync(string username)
    {
        var tokens = await _context.RefreshTokens.Where(t => t.Username == username).ToListAsync();
        _context.RefreshTokens.RemoveRange(tokens);
        await _context.SaveChangesAsync();
    }

    /// <inheritdoc/>
    public async Task CleanExpiredRefreshTokensAsync()
    {
        var expired = await _context.RefreshTokens.Where(t => t.ExpiresAt < DateTime.UtcNow).ToListAsync();
        _context.RefreshTokens.RemoveRange(expired);
        await _context.SaveChangesAsync();
    }

    // ── Scope operations ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<bool> ScopeExistsAsync(string name)
    {
        return await _context.Scopes.AnyAsync(s => s.Name == name);
    }

    /// <inheritdoc/>
    public async Task CreateScopeAsync(ScopeRecord scope)
    {
        _context.Scopes.Add(scope);
        await _context.SaveChangesAsync();
    }

    /// <inheritdoc/>
    public async Task<bool> TryCreateScopeAsync(ScopeRecord scope)
    {
        _context.Scopes.Add(scope);
        try
        {
            await _context.SaveChangesAsync();
            return true;
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
            when (ex.InnerException is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 19 }
                  || (ex.InnerException?.Message?.Contains("UNIQUE constraint failed") ?? false)
                  || (ex.InnerException?.Message?.Contains("unique constraint") ?? false))
        {
            // Unique-constraint violation: another concurrent insert won the race.
            _context.Entry(scope).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task DeleteScopeAsync(string name)
    {
        int pkgCount = await CountPackagesByScopeAsync(name);
        if (pkgCount > 0)
            throw new InvalidOperationException($"Scope '@{name}' still owns {pkgCount} package(s); delete them first.");

        var record = await _context.Scopes.FindAsync(name);
        if (record != null)
        {
            _context.Scopes.Remove(record);
            await _context.SaveChangesAsync();
        }
    }

    /// <inheritdoc/>
    public async Task<int> CountPackagesByScopeAsync(string scope)
    {
        string prefix = $"@{scope}/";
        return await _context.Packages.CountAsync(p => p.Name.StartsWith(prefix));
    }

    /// <inheritdoc/>
    public async Task<ScopeRecord?> GetScopeAsync(string name)
    {
        return await _context.Scopes.AsNoTracking().FirstOrDefaultAsync(s => s.Name == name);
    }

    /// <inheritdoc/>
    public async Task SeedSystemScopesAsync()
    {
        foreach (string scopeName in ReservedScopes.All)
        {
            bool exists = await _context.Scopes.AnyAsync(s => s.Name == scopeName);
            if (!exists)
            {
                _context.Scopes.Add(new ScopeRecord
                {
                    Name = scopeName,
                    OwnerType = ScopeOwnerTypes.System,
                    OwnerUsername = null,
                    OwnerOrgId = null
                });
            }
        }
        await _context.SaveChangesAsync();
    }

    /// <inheritdoc/>
    public async Task<string> CreateUserWithScopeAsync(string username, string passwordHash)
    {
        using var tx = await _context.Database.BeginTransactionAsync();

        // Check for existing user
        var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (existingUser is not null)
            throw new InvalidOperationException($"Username '{username}' is already taken.");

        // Check for scope collision (covers user, org, and system scopes — e.g. @stash, @admin)
        bool scopeExists = await _context.Scopes.AnyAsync(s => s.Name == username);
        if (scopeExists)
            throw new InvalidOperationException($"Username '{username}' conflicts with an existing scope.");

        // First user becomes admin
        long count = await _context.Users.LongCountAsync();
        UserRoles roleEnum = count == 0 ? UserRoles.Admin : UserRoles.User;

        _context.Users.Add(new UserRecord
        {
            Username = username,
            PasswordHash = passwordHash,
            Role = roleEnum,
            CreatedAt = DateTime.UtcNow
        });

        // Auto-provision the @<username> personal scope
        _context.Scopes.Add(new ScopeRecord
        {
            Name = username,
            OwnerType = ScopeOwnerTypes.User,
            OwnerUsername = username,
            OwnerOrgId = null
        });

        await _context.SaveChangesAsync();
        await tx.CommitAsync();
        return roleEnum.ToWire();
    }

    // ── Package role operations ───────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task AssignPackageRoleAsync(
        string packageName, string principalType, string principalId, string role)
    {
        var ptEnum = principalType.ToPrincipalType();
        var roleEnum = role.ToPackageRole();
        var existing = await _context.PackageRoles
            .FindAsync(packageName, ptEnum, principalId);
        if (existing != null)
        {
            existing.Role = roleEnum;
        }
        else
        {
            _context.PackageRoles.Add(new PackageRoleEntry
            {
                PackageName = packageName,
                PrincipalType = ptEnum,
                PrincipalId = principalId,
                Role = roleEnum
            });
        }
        await _context.SaveChangesAsync();
    }

    /// <inheritdoc/>
    public async Task RevokePackageRoleAsync(string packageName, string principalType, string principalId)
    {
        var ptEnum = principalType.ToPrincipalType();
        var entry = await _context.PackageRoles.FindAsync(packageName, ptEnum, principalId);
        if (entry != null)
        {
            _context.PackageRoles.Remove(entry);
            await _context.SaveChangesAsync();
        }
    }

    /// <inheritdoc/>
    public async Task<List<PackageRoleEntry>> GetPackageRolesAsync(string packageName)
    {
        return await _context.PackageRoles
            .AsNoTracking()
            .Where(r => r.PackageName == packageName)
            .ToListAsync();
    }

    // ── Audit operations ──────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task AddAuditEntryAsync(AuditEntry entry)
    {
        _context.AuditLog.Add(entry);
        await _context.SaveChangesAsync();
    }

    /// <inheritdoc/>
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

    // ── Organization operations ───────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<OrganizationRecord> CreateOrgAsync(string name, string? displayName, string createdBy)
    {
        using var tx = await _context.Database.BeginTransactionAsync();

        // Check for scope collision (covers user, org, and system)
        bool scopeExists = await _context.Scopes.AnyAsync(s => s.Name == name);
        if (scopeExists)
            throw new InvalidOperationException($"A scope named '{name}' already exists.");

        // Check for org name collision
        bool orgExists = await _context.Organizations.AnyAsync(o => o.Name == name);
        if (orgExists)
            throw new InvalidOperationException($"An organization named '{name}' already exists.");

        var org = new OrganizationRecord
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            DisplayName = displayName,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy
        };
        _context.Organizations.Add(org);

        // Auto-provision the org scope
        _context.Scopes.Add(new ScopeRecord
        {
            Name = name,
            OwnerType = ScopeOwnerTypes.Org,
            OwnerOrgId = org.Id,
            OwnerUsername = null
        });

        // Add creator as org owner
        _context.OrgMembers.Add(new OrgMemberEntry
        {
            OrgId = org.Id,
            Username = createdBy,
            OrgRole = OrgRoles.Owner,
            JoinedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();
        await tx.CommitAsync();
        return org;
    }

    /// <inheritdoc/>
    public async Task<OrganizationRecord?> GetOrgAsync(string name)
    {
        return await _context.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Name == name);
    }

    /// <inheritdoc/>
    public async Task<List<OrgMemberEntry>> GetOrgMembersAsync(string orgId)
    {
        return await _context.OrgMembers.AsNoTracking().Where(m => m.OrgId == orgId).ToListAsync();
    }

    /// <inheritdoc/>
    public async Task AddOrgMemberAsync(string orgId, string username, string orgRole)
    {
        bool alreadyMember = await _context.OrgMembers.AnyAsync(m => m.OrgId == orgId && m.Username == username);
        if (alreadyMember)
            throw new InvalidOperationException($"User '{username}' is already a member of this organization.");

        _context.OrgMembers.Add(new OrgMemberEntry
        {
            OrgId = orgId,
            Username = username,
            OrgRole = orgRole.ToOrgRole(),
            JoinedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
    }

    /// <inheritdoc/>
    public async Task RemoveOrgMemberAsync(string orgId, string username)
    {
        var entry = await _context.OrgMembers.FirstOrDefaultAsync(m => m.OrgId == orgId && m.Username == username);
        if (entry != null)
        {
            _context.OrgMembers.Remove(entry);
            await _context.SaveChangesAsync();
        }
    }

    /// <inheritdoc/>
    public async Task<bool> IsOrgOwnerAsync(string orgId, string username)
    {
        return await _context.OrgMembers.AnyAsync(m =>
            m.OrgId == orgId && m.Username == username && m.OrgRole == OrgRoles.Owner);
    }

    /// <inheritdoc/>
    public async Task DeleteOrgAsync(string name)
    {
        var org = await _context.Organizations.FirstOrDefaultAsync(o => o.Name == name);
        if (org == null)
            return;

        // Cascade refusal: deny if the org still owns scopes or packages.
        int scopeCount = await CountScopesByOrgAsync(org.Id);
        if (scopeCount > 0)
            throw new InvalidOperationException($"Organization '{name}' still owns {scopeCount} scope(s); delete them first.");

        // Also check packages in org-owned scopes (belt-and-suspenders; scopes should catch it first)
        var orgScopeNames = await _context.Scopes
            .AsNoTracking()
            .Where(s => s.OwnerOrgId == org.Id)
            .Select(s => s.Name)
            .ToListAsync();
        foreach (string scopeName in orgScopeNames)
        {
            int pkgCount = await CountPackagesByScopeAsync(scopeName);
            if (pkgCount > 0)
                throw new InvalidOperationException($"Organization '{name}' still owns packages under '@{scopeName}'; delete them first.");
        }

        _context.Organizations.Remove(org);
        await _context.SaveChangesAsync();
    }

    /// <inheritdoc/>
    public async Task<int> CountScopesByOrgAsync(string orgId)
    {
        return await _context.Scopes.CountAsync(s => s.OwnerOrgId == orgId);
    }

    // ── Team operations ───────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<TeamRecord> CreateTeamAsync(string orgId, string name)
    {
        bool exists = await _context.Teams.AnyAsync(t => t.OrgId == orgId && t.Name == name);
        if (exists)
            throw new InvalidOperationException($"A team named '{name}' already exists in this organization.");

        var team = new TeamRecord
        {
            Id = Guid.NewGuid().ToString(),
            OrgId = orgId,
            Name = name,
            CreatedAt = DateTime.UtcNow
        };
        _context.Teams.Add(team);
        await _context.SaveChangesAsync();
        return team;
    }

    /// <inheritdoc/>
    public async Task<TeamRecord?> GetTeamAsync(string teamId)
    {
        return await _context.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == teamId);
    }

    /// <inheritdoc/>
    public async Task<TeamRecord?> GetTeamByNameAsync(string orgId, string name)
    {
        return await _context.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.OrgId == orgId && t.Name == name);
    }

    /// <inheritdoc/>
    public async Task AddTeamMemberAsync(string teamId, string username)
    {
        bool alreadyMember = await _context.TeamMembers.AnyAsync(m => m.TeamId == teamId && m.Username == username);
        if (!alreadyMember)
        {
            _context.TeamMembers.Add(new TeamMemberEntry
            {
                TeamId = teamId,
                Username = username,
                JoinedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
        }
    }
}
