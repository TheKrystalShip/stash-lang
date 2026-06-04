using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Stash.Registry.Contracts;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;

namespace Stash.Tests.Registry;

public sealed class SqliteDatabaseTests
{
    private static StashRegistryDatabase CreateTestDb()
    {
        var options = new DbContextOptionsBuilder<RegistryDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        var context = new RegistryDbContext(options);
        context.Database.OpenConnection();
        var db = new StashRegistryDatabase(context);
        db.Initialize();
        return db;
    }

    private static PackageRecord MakePackage(string name, string latest = "1.0.0") => new()
    {
        Name = name,
        Description = $"Description for {name}",
        License = "MIT",
        Latest = latest,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static VersionRecord MakeVersion(string packageName, string version, string publisher = "testuser") => new()
    {
        PackageName = packageName,
        Version = version,
        Integrity = "sha256-abc123",
        PublishedAt = DateTime.UtcNow,
        PublishedBy = publisher
    };

    // ── Initialize ──────────────────────────────────────────────────────

    [Fact]
    public void Initialize_CreatesTablesWithoutError()
    {
        CreateTestDb();
    }

    // ── Package operations ──────────────────────────────────────────────

    [Fact]
    public async Task CreatePackage_GetPackage_RoundTrips()
    {
        var db = CreateTestDb();
        var pkg = MakePackage("my-package");
        await db.CreatePackageAsync(pkg);

        PackageRecord? result = await db.GetPackageAsync("my-package");

        Assert.NotNull(result);
        Assert.Equal("my-package", result.Name);
        Assert.Equal("Description for my-package", result.Description);
        Assert.Equal("MIT", result.License);
        Assert.Equal("1.0.0", result.Latest);
    }

    [Fact]
    public async Task CreatePackage_PackageExists_ReturnsTrue()
    {
        var db = CreateTestDb();
        await db.CreatePackageAsync(MakePackage("exists-pkg"));

        Assert.True(await db.PackageExistsAsync("exists-pkg"));
    }

    [Fact]
    public async Task PackageExists_NonExistent_ReturnsFalse()
    {
        var db = CreateTestDb();

        Assert.False(await db.PackageExistsAsync("no-such-package"));
    }

    // ── Version operations ──────────────────────────────────────────────

    [Fact]
    public async Task AddVersion_GetPackageVersion_RoundTrips()
    {
        var db = CreateTestDb();
        await db.CreatePackageAsync(MakePackage("vpkg"));
        var ver = MakeVersion("vpkg", "2.0.0");
        await db.AddVersionAsync("vpkg", ver);

        VersionRecord? result = await db.GetPackageVersionAsync("vpkg", "2.0.0");

        Assert.NotNull(result);
        Assert.Equal("vpkg", result.PackageName);
        Assert.Equal("2.0.0", result.Version);
        Assert.Equal("sha256-abc123", result.Integrity);
        Assert.Equal("testuser", result.PublishedBy);
    }

    [Fact]
    public async Task VersionExists_ExistingVersion_ReturnsTrue()
    {
        var db = CreateTestDb();
        await db.CreatePackageAsync(MakePackage("vpkg2"));
        await db.AddVersionAsync("vpkg2", MakeVersion("vpkg2", "1.0.0"));

        Assert.True(await db.VersionExistsAsync("vpkg2", "1.0.0"));
    }

    [Fact]
    public async Task VersionExists_NonExistent_ReturnsFalse()
    {
        var db = CreateTestDb();
        await db.CreatePackageAsync(MakePackage("vpkg3"));

        Assert.False(await db.VersionExistsAsync("vpkg3", "9.9.9"));
    }

    [Fact]
    public async Task DeleteVersion_RemovesVersion()
    {
        var db = CreateTestDb();
        await db.CreatePackageAsync(MakePackage("delpkg"));
        await db.AddVersionAsync("delpkg", MakeVersion("delpkg", "1.0.0"));
        Assert.True(await db.VersionExistsAsync("delpkg", "1.0.0"));

        await db.DeleteVersionAsync("delpkg", "1.0.0");

        Assert.False(await db.VersionExistsAsync("delpkg", "1.0.0"));
    }

    [Fact]
    public async Task GetAllVersions_ReturnsAll()
    {
        var db = CreateTestDb();
        await db.CreatePackageAsync(MakePackage("allv"));
        await db.AddVersionAsync("allv", MakeVersion("allv", "1.0.0"));
        await db.AddVersionAsync("allv", MakeVersion("allv", "2.0.0"));
        await db.AddVersionAsync("allv", MakeVersion("allv", "3.0.0"));

        List<string> versions = await db.GetAllVersionsAsync("allv");

        Assert.Equal(3, versions.Count);
        Assert.Contains("1.0.0", versions);
        Assert.Contains("2.0.0", versions);
        Assert.Contains("3.0.0", versions);
    }

    // ── Search ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchPackages_MatchesByName()
    {
        var db = CreateTestDb();
        await db.CreatePackageAsync(MakePackage("search-alpha"));
        await db.CreatePackageAsync(MakePackage("search-beta"));

        SearchResult result = await db.SearchPackagesAsync("alpha", 1, 10, null);

        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Packages);
        Assert.Equal("search-alpha", result.Packages[0].Package.Name);
    }

    [Fact]
    public async Task SearchPackages_MatchesByDescription()
    {
        var db = CreateTestDb();
        var pkg = MakePackage("pkg-x");
        pkg.Description = "A unique-description here";
        await db.CreatePackageAsync(pkg);

        SearchResult result = await db.SearchPackagesAsync("unique-description", 1, 10, null);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("pkg-x", result.Packages[0].Package.Name);
    }

    [Fact]
    public async Task SearchPackages_NoMatch_ReturnsEmpty()
    {
        var db = CreateTestDb();
        await db.CreatePackageAsync(MakePackage("no-match-pkg"));

        SearchResult result = await db.SearchPackagesAsync("zzzzz-nonexistent", 1, 10, null);

        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Packages);
    }

    [Fact]
    public async Task SearchPackages_Pagination_Works()
    {
        var db = CreateTestDb();
        for (int i = 0; i < 5; i++)
        {
            await db.CreatePackageAsync(MakePackage($"page-pkg-{i}"));
        }

        SearchResult page1 = await db.SearchPackagesAsync("page-pkg", 1, 2, null);
        SearchResult page2 = await db.SearchPackagesAsync("page-pkg", 2, 2, null);
        SearchResult page3 = await db.SearchPackagesAsync("page-pkg", 3, 2, null);

        Assert.Equal(5, page1.TotalCount);
        Assert.Equal(2, page1.Packages.Count);
        Assert.Equal(2, page2.Packages.Count);
        Assert.Single(page3.Packages);
    }

    // ── Update operations ───────────────────────────────────────────────

    [Fact]
    public async Task UpdatePackageTimestamp_UpdatesTime()
    {
        var db = CreateTestDb();
        var pkg = MakePackage("ts-pkg");
        pkg.UpdatedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await db.CreatePackageAsync(pkg);

        await db.UpdatePackageTimestampAsync("ts-pkg");

        PackageRecord? result = await db.GetPackageAsync("ts-pkg");
        Assert.NotNull(result);
        Assert.True(result.UpdatedAt > new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task UpdatePackageLatest_UpdatesLatest()
    {
        var db = CreateTestDb();
        await db.CreatePackageAsync(MakePackage("lat-pkg", "1.0.0"));

        await db.UpdatePackageLatestAsync("lat-pkg", "2.0.0");

        PackageRecord? result = await db.GetPackageAsync("lat-pkg");
        Assert.NotNull(result);
        Assert.Equal("2.0.0", result.Latest);
    }

    [Fact]
    public async Task UpdatePackageReadme_UpdatesReadme()
    {
        var db = CreateTestDb();
        await db.CreatePackageAsync(MakePackage("readme-pkg"));

        await db.UpdatePackageReadmeAsync("readme-pkg", "# Hello World");

        PackageRecord? result = await db.GetPackageAsync("readme-pkg");
        Assert.NotNull(result);
        Assert.Equal("# Hello World", result.Readme);
    }

    // ── User operations ─────────────────────────────────────────────────

    [Fact]
    public async Task CreateUser_GetUser_RoundTrips()
    {
        var db = CreateTestDb();
        await db.CreateUserAsync("alice", "hash123", "user");

        UserRecord? result = await db.GetUserAsync("alice");

        Assert.NotNull(result);
        Assert.Equal("alice", result.Username);
        Assert.Equal("hash123", result.PasswordHash);
        Assert.Equal(UserRoles.User, result.Role);
    }

    [Fact]
    public async Task DeleteUser_RemovesUser()
    {
        var db = CreateTestDb();
        await db.CreateUserAsync("bob", "hash456", "user");
        Assert.NotNull(await db.GetUserAsync("bob"));

        await db.DeleteUserAsync("bob");

        Assert.Null(await db.GetUserAsync("bob"));
    }

    [Fact]
    public async Task ListUsers_ReturnsAll()
    {
        var db = CreateTestDb();
        await db.CreateUserAsync("alice", "h1", "user");
        await db.CreateUserAsync("bob", "h2", "admin");
        await db.CreateUserAsync("charlie", "h3", "user");

        List<string> users = await db.ListUsersAsync();

        Assert.Equal(3, users.Count);
        Assert.Contains("alice", users);
        Assert.Contains("bob", users);
        Assert.Contains("charlie", users);
    }

    // ── Token operations ────────────────────────────────────────────────

    [Fact]
    public async Task CreateToken_GetTokenByHash_RoundTrips()
    {
        var db = CreateTestDb();
        await db.CreateUserAsync("tokenuser", "h", "user");
        var token = new TokenRecord
        {
            Id = "tok-1",
            Username = "tokenuser",
            TokenHash = "hash-aaa",
            Scope = "publish",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            Description = "CI token"
        };
        await db.CreateTokenAsync(token);

        TokenRecord? result = await db.GetTokenByHashAsync("hash-aaa");

        Assert.NotNull(result);
        Assert.Equal("tok-1", result.Id);
        Assert.Equal("tokenuser", result.Username);
        Assert.Equal("publish", result.Scope);
        Assert.Equal("CI token", result.Description);
    }

    [Fact]
    public async Task DeleteToken_RemovesToken()
    {
        var db = CreateTestDb();
        await db.CreateUserAsync("tokenuser2", "h", "user");
        await db.CreateTokenAsync(new TokenRecord
        {
            Id = "tok-2",
            Username = "tokenuser2",
            TokenHash = "hash-bbb",
            Scope = "publish",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        });

        await db.DeleteTokenAsync("tok-2");

        Assert.Null(await db.GetTokenByHashAsync("hash-bbb"));
    }

    [Fact]
    public async Task GetUserTokens_ReturnsUserTokens()
    {
        var db = CreateTestDb();
        await db.CreateUserAsync("multi-tok", "h", "user");
        await db.CreateUserAsync("other-user", "h", "user");
        await db.CreateTokenAsync(new TokenRecord
        {
            Id = "t1", Username = "multi-tok", TokenHash = "h1",
            Scope = "publish", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(1)
        });
        await db.CreateTokenAsync(new TokenRecord
        {
            Id = "t2", Username = "multi-tok", TokenHash = "h2",
            Scope = "read", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(1)
        });
        await db.CreateTokenAsync(new TokenRecord
        {
            Id = "t3", Username = "other-user", TokenHash = "h3",
            Scope = "publish", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(1)
        });

        List<TokenRecord> tokens = await db.GetUserTokensAsync("multi-tok");

        Assert.Equal(2, tokens.Count);
        Assert.All(tokens, t => Assert.Equal("multi-tok", t.Username));
    }

    [Fact]
    public async Task CleanExpiredTokens_RemovesExpired()
    {
        var db = CreateTestDb();
        await db.CreateUserAsync("expuser", "h", "user");
        await db.CreateTokenAsync(new TokenRecord
        {
            Id = "expired-tok", Username = "expuser", TokenHash = "exp-hash",
            Scope = "publish", CreatedAt = DateTime.UtcNow.AddDays(-60),
            ExpiresAt = DateTime.UtcNow.AddDays(-1) // already expired
        });
        await db.CreateTokenAsync(new TokenRecord
        {
            Id = "valid-tok", Username = "expuser", TokenHash = "valid-hash",
            Scope = "publish", CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(30) // still valid
        });

        await db.CleanExpiredTokensAsync();

        Assert.Null(await db.GetTokenByHashAsync("exp-hash"));
        Assert.NotNull(await db.GetTokenByHashAsync("valid-hash"));
    }

    [Fact]
    public async Task GetUserTokens_Empty_ReturnsEmptyList()
    {
        var db = CreateTestDb();
        await db.CreateUserAsync("notokens", "h", "user");

        List<TokenRecord> tokens = await db.GetUserTokensAsync("notokens");

        Assert.Empty(tokens);
    }

    // ── Package role operations ─────────────────────────────────────────

    [Fact]
    public async Task AssignPackageRole_GetPackageRoles_RoundTrips()
    {
        var db = CreateTestDb();
        await db.CreatePackageAsync(MakePackage("@alice/widget"));
        await db.AssignPackageRoleAsync("@alice/widget", "user", "alice", "owner");
        await db.AssignPackageRoleAsync("@alice/widget", "user", "bob", "publisher");

        List<PackageRoleEntry> roles = await db.GetPackageRolesAsync("@alice/widget");

        Assert.Equal(2, roles.Count);
        Assert.Contains(roles, r => r.PrincipalId == "alice" && r.Role == PackageRoles.Owner);
        Assert.Contains(roles, r => r.PrincipalId == "bob" && r.Role == PackageRoles.Publisher);
    }

    [Fact]
    public async Task AssignPackageRole_ReplacesExistingRole()
    {
        var db = CreateTestDb();
        await db.CreatePackageAsync(MakePackage("@alice/tool"));
        await db.AssignPackageRoleAsync("@alice/tool", "user", "alice", "publisher");
        await db.AssignPackageRoleAsync("@alice/tool", "user", "alice", "owner");

        List<PackageRoleEntry> roles = await db.GetPackageRolesAsync("@alice/tool");

        Assert.Single(roles);
        Assert.Equal(PackageRoles.Owner, roles[0].Role);
    }

    [Fact]
    public async Task RevokePackageRole_RemovesEntry()
    {
        var db = CreateTestDb();
        await db.CreatePackageAsync(MakePackage("@alice/lib"));
        await db.AssignPackageRoleAsync("@alice/lib", "user", "alice", "owner");
        await db.AssignPackageRoleAsync("@alice/lib", "user", "bob", "reader");

        await db.RevokePackageRoleAsync("@alice/lib", "user", "bob");

        List<PackageRoleEntry> roles = await db.GetPackageRolesAsync("@alice/lib");
        Assert.Single(roles);
        Assert.Equal("alice", roles[0].PrincipalId);
    }

    // ── Audit operations ────────────────────────────────────────────────

    [Fact]
    public async Task AddAuditEntry_GetAuditLog_Works()
    {
        var db = CreateTestDb();
        await db.AddAuditEntryAsync(new AuditEntry
        {
            Action = "publish",
            Package = "audit-pkg",
            Version = "1.0.0",
            User = "alice",
            Timestamp = DateTime.UtcNow
        });

        SearchResult<AuditEntry> result = await db.GetAuditLogAsync(1, 10, null, null);

        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal("publish", result.Items[0].Action);
        Assert.Equal("audit-pkg", result.Items[0].Package);
    }

    [Fact]
    public async Task GetAuditLog_FiltersByPackage()
    {
        var db = CreateTestDb();
        await db.AddAuditEntryAsync(new AuditEntry
        {
            Action = "publish", Package = "pkg-a", Version = "1.0.0",
            User = "alice", Timestamp = DateTime.UtcNow
        });
        await db.AddAuditEntryAsync(new AuditEntry
        {
            Action = "publish", Package = "pkg-b", Version = "1.0.0",
            User = "bob", Timestamp = DateTime.UtcNow
        });

        SearchResult<AuditEntry> result = await db.GetAuditLogAsync(1, 10, "pkg-a", null);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("pkg-a", result.Items[0].Package);
    }

    [Fact]
    public async Task GetAuditLog_FiltersByAction()
    {
        var db = CreateTestDb();
        await db.AddAuditEntryAsync(new AuditEntry
        {
            Action = "publish", Package = "pkg-c", Version = "1.0.0",
            User = "alice", Timestamp = DateTime.UtcNow
        });
        await db.AddAuditEntryAsync(new AuditEntry
        {
            Action = "unpublish", Package = "pkg-c", Version = "1.0.0",
            User = "alice", Timestamp = DateTime.UtcNow
        });

        SearchResult<AuditEntry> result = await db.GetAuditLogAsync(1, 10, null, "unpublish");

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("unpublish", result.Items[0].Action);
    }

    // ── Refresh token operations ──────────────────────────────────────────

    [Fact]
    public async Task CreateRefreshToken_PersistsRecord()
    {
        var db = CreateTestDb();
        await db.CreateUserAsync("alice", "hash", "user");

        await db.CreateRefreshTokenAsync(new RefreshTokenRecord
        {
            Id = "rt-1",
            Username = "alice",
            TokenHash = "hash-abc",
            AccessTokenId = "at-1",
            FamilyId = "family-1",
            MachineId = "machine-1",
            Scope = "publish",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(90),
            Consumed = false
        });

        var found = await db.GetRefreshTokenByHashAsync("hash-abc");
        Assert.NotNull(found);
        Assert.Equal("rt-1", found.Id);
        Assert.Equal("alice", found.Username);
        Assert.Equal("machine-1", found.MachineId);
        Assert.Equal("at-1", found.AccessTokenId);
        Assert.False(found.Consumed);
    }

    [Fact]
    public async Task GetRefreshTokenByHash_ReturnsNullWhenNotFound()
    {
        var db = CreateTestDb();
        var found = await db.GetRefreshTokenByHashAsync("nonexistent-hash");
        Assert.Null(found);
    }

    [Fact]
    public async Task ConsumeRefreshToken_SetsConsumedFlag()
    {
        var db = CreateTestDb();
        await db.CreateUserAsync("bob", "hash", "user");

        await db.CreateRefreshTokenAsync(new RefreshTokenRecord
        {
            Id = "rt-2",
            Username = "bob",
            TokenHash = "hash-def",
            AccessTokenId = "at-2",
            FamilyId = "family-2",
            MachineId = "machine-2",
            Scope = "publish",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(90),
            Consumed = false
        });

        bool result = await db.ConsumeRefreshTokenAsync("rt-2");

        Assert.True(result);
        var found = await db.GetRefreshTokenByHashAsync("hash-def");
        Assert.NotNull(found);
        Assert.True(found.Consumed);
    }

    [Fact]
    public async Task ConsumeRefreshToken_AlreadyConsumed_ReturnsFalse()
    {
        var db = CreateTestDb();
        await db.CreateUserAsync("henry", "hash", "user");

        await db.CreateRefreshTokenAsync(new RefreshTokenRecord
        {
            Id = "rt-ac",
            Username = "henry",
            TokenHash = "hash-ac",
            AccessTokenId = "at-ac",
            FamilyId = "family-ac",
            MachineId = "machine-ac",
            Scope = "publish",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(90),
            Consumed = false
        });

        bool first = await db.ConsumeRefreshTokenAsync("rt-ac");
        bool second = await db.ConsumeRefreshTokenAsync("rt-ac");

        Assert.True(first);
        Assert.False(second);
    }

    [Fact]
    public async Task ConsumeRefreshToken_NoOpWhenNotFound()
    {
        var db = CreateTestDb();
        bool result = await db.ConsumeRefreshTokenAsync("nonexistent-id");
        Assert.False(result);
    }

    [Fact]
    public async Task DeleteRefreshTokensByAccessToken_RemovesMatchingTokens()
    {
        var db = CreateTestDb();
        await db.CreateUserAsync("carol", "hash", "user");

        await db.CreateRefreshTokenAsync(new RefreshTokenRecord
        {
            Id = "rt-3",
            Username = "carol",
            TokenHash = "hash-111",
            AccessTokenId = "at-shared",
            FamilyId = "family-3",
            MachineId = "machine-3",
            Scope = "publish",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(90)
        });
        await db.CreateRefreshTokenAsync(new RefreshTokenRecord
        {
            Id = "rt-4",
            Username = "carol",
            TokenHash = "hash-222",
            AccessTokenId = "at-shared",
            FamilyId = "family-3",
            MachineId = "machine-3",
            Scope = "publish",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(90)
        });
        await db.CreateRefreshTokenAsync(new RefreshTokenRecord
        {
            Id = "rt-5",
            Username = "carol",
            TokenHash = "hash-333",
            AccessTokenId = "at-other",
            FamilyId = "family-3b",
            MachineId = "machine-3",
            Scope = "publish",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(90)
        });

        await db.DeleteRefreshTokensByAccessTokenAsync("at-shared");

        Assert.Null(await db.GetRefreshTokenByHashAsync("hash-111"));
        Assert.Null(await db.GetRefreshTokenByHashAsync("hash-222"));
        Assert.NotNull(await db.GetRefreshTokenByHashAsync("hash-333"));
    }

    [Fact]
    public async Task DeleteUserRefreshTokens_RemovesAllForUser()
    {
        var db = CreateTestDb();
        await db.CreateUserAsync("dave", "hash", "user");
        await db.CreateUserAsync("eve", "hash", "user");

        await db.CreateRefreshTokenAsync(new RefreshTokenRecord
        {
            Id = "rt-6",
            Username = "dave",
            TokenHash = "hash-aaa",
            AccessTokenId = "at-d1",
            FamilyId = "family-d1",
            MachineId = "machine-d",
            Scope = "publish",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(90)
        });
        await db.CreateRefreshTokenAsync(new RefreshTokenRecord
        {
            Id = "rt-7",
            Username = "eve",
            TokenHash = "hash-bbb",
            AccessTokenId = "at-e1",
            FamilyId = "family-e1",
            MachineId = "machine-e",
            Scope = "publish",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(90)
        });

        await db.DeleteUserRefreshTokensAsync("dave");

        Assert.Null(await db.GetRefreshTokenByHashAsync("hash-aaa"));
        Assert.NotNull(await db.GetRefreshTokenByHashAsync("hash-bbb"));
    }

    [Fact]
    public async Task CleanExpiredRefreshTokens_RemovesOnlyExpired()
    {
        var db = CreateTestDb();
        await db.CreateUserAsync("frank", "hash", "user");

        await db.CreateRefreshTokenAsync(new RefreshTokenRecord
        {
            Id = "rt-8",
            Username = "frank",
            TokenHash = "hash-expired",
            AccessTokenId = "at-f1",
            FamilyId = "family-f1",
            MachineId = "machine-f",
            Scope = "publish",
            CreatedAt = DateTime.UtcNow.AddDays(-100),
            ExpiresAt = DateTime.UtcNow.AddDays(-1) // expired
        });
        await db.CreateRefreshTokenAsync(new RefreshTokenRecord
        {
            Id = "rt-9",
            Username = "frank",
            TokenHash = "hash-valid",
            AccessTokenId = "at-f2",
            FamilyId = "family-f2",
            MachineId = "machine-f",
            Scope = "publish",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(90) // valid
        });

        await db.CleanExpiredRefreshTokensAsync();

        Assert.Null(await db.GetRefreshTokenByHashAsync("hash-expired"));
        Assert.NotNull(await db.GetRefreshTokenByHashAsync("hash-valid"));
    }

    [Fact]
    public async Task RefreshToken_CascadeDeleteOnUserRemoval()
    {
        var db = CreateTestDb();
        await db.CreateUserAsync("grace", "hash", "user");

        await db.CreateRefreshTokenAsync(new RefreshTokenRecord
        {
            Id = "rt-10",
            Username = "grace",
            TokenHash = "hash-grace",
            AccessTokenId = "at-g1",
            FamilyId = "family-g1",
            MachineId = "machine-g",
            Scope = "publish",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(90)
        });

        await db.DeleteUserAsync("grace");

        Assert.Null(await db.GetRefreshTokenByHashAsync("hash-grace"));
    }

    [Fact]
    public async Task GetRefreshTokensByFamily_ReturnsMatchingTokens()
    {
        var db = CreateTestDb();
        await db.CreateUserAsync("ivan", "hash", "user");

        await db.CreateRefreshTokenAsync(new RefreshTokenRecord
        {
            Id = "rt-f1",
            Username = "ivan",
            TokenHash = "hash-f1",
            AccessTokenId = "at-f1",
            FamilyId = "family-abc",
            MachineId = "machine-i",
            Scope = "publish",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(90)
        });
        await db.CreateRefreshTokenAsync(new RefreshTokenRecord
        {
            Id = "rt-f2",
            Username = "ivan",
            TokenHash = "hash-f2",
            AccessTokenId = "at-f2",
            FamilyId = "family-abc",
            MachineId = "machine-i",
            Scope = "publish",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(90)
        });
        await db.CreateRefreshTokenAsync(new RefreshTokenRecord
        {
            Id = "rt-f3",
            Username = "ivan",
            TokenHash = "hash-f3",
            AccessTokenId = "at-f3",
            FamilyId = "family-other",
            MachineId = "machine-i",
            Scope = "publish",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(90)
        });

        List<RefreshTokenRecord> results = await db.GetRefreshTokensByFamilyAsync("family-abc");

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal("family-abc", r.FamilyId));
        Assert.Contains(results, r => r.Id == "rt-f1");
        Assert.Contains(results, r => r.Id == "rt-f2");
    }

    [Fact]
    public async Task DeleteRefreshTokensByFamily_RemovesAllInFamily()
    {
        var db = CreateTestDb();
        await db.CreateUserAsync("julia", "hash", "user");

        await db.CreateRefreshTokenAsync(new RefreshTokenRecord
        {
            Id = "rt-d1",
            Username = "julia",
            TokenHash = "hash-d1",
            AccessTokenId = "at-d1",
            FamilyId = "family-del",
            MachineId = "machine-j",
            Scope = "publish",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(90)
        });
        await db.CreateRefreshTokenAsync(new RefreshTokenRecord
        {
            Id = "rt-d2",
            Username = "julia",
            TokenHash = "hash-d2",
            AccessTokenId = "at-d2",
            FamilyId = "family-del",
            MachineId = "machine-j",
            Scope = "publish",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(90)
        });
        await db.CreateRefreshTokenAsync(new RefreshTokenRecord
        {
            Id = "rt-d3",
            Username = "julia",
            TokenHash = "hash-d3",
            AccessTokenId = "at-d3",
            FamilyId = "family-keep",
            MachineId = "machine-j",
            Scope = "publish",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(90)
        });

        await db.DeleteRefreshTokensByFamilyAsync("family-del");

        Assert.Null(await db.GetRefreshTokenByHashAsync("hash-d1"));
        Assert.Null(await db.GetRefreshTokenByHashAsync("hash-d2"));
        Assert.NotNull(await db.GetRefreshTokenByHashAsync("hash-d3"));
    }

    // ── P4 bounded-domain DDL defaults (sqlite_master load-bearing gate) ─────

    /// <summary>
    /// After Initialize(), reads sqlite_master to verify the DDL column defaults for
    /// bounded-domain enum properties emit the correct lowercase wire strings.
    /// This is the load-bearing gate from P4 done_when — the EF model metadata can
    /// report the intended default while the emitted column default stores the wrong
    /// value (PascalCase enum name or int) due to HasConversion vs HasDefaultValue ordering.
    /// The sqlite_master SQL is the ground truth.
    /// </summary>
    [Fact]
    public void Initialize_BoundedDomainColumnDefaults_AreLowercaseWireStrings()
    {
        var options = new DbContextOptionsBuilder<RegistryDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        var context = new RegistryDbContext(options);
        context.Database.OpenConnection();
        var db = new StashRegistryDatabase(context);
        db.Initialize();

        // Read the raw DDL from sqlite_master so we can assert the emitted DEFAULT literals.
        var ddlMap = new System.Collections.Generic.Dictionary<string, string>();
        using (var conn = (SqliteConnection)context.Database.GetDbConnection())
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name, sql FROM sqlite_master WHERE type='table'";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string tableName = reader.GetString(0);
                string? sql = reader.IsDBNull(1) ? null : reader.GetString(1);
                if (sql != null)
                    ddlMap[tableName] = sql;
            }
        }

        // (table, column, expected-default-literal) — the DDL must contain "DEFAULT '<value>'"
        var assertions = new (string Table, string Column, string ExpectedDefault)[]
        {
            ("packages",     "visibility", "'public'"),
            ("users",        "role",       "'user'"),
            ("org_members",  "org_role",   "'member'"),
        };

        var violations = new System.Collections.Generic.List<string>();
        foreach (var (table, column, expected) in assertions)
        {
            if (!ddlMap.TryGetValue(table, out string? ddl))
            {
                violations.Add($"Table '{table}' not found in sqlite_master.");
                continue;
            }

            // SQLite can emit DEFAULT either as: DEFAULT 'value' or DEFAULT ('value')
            // Both are semantically equivalent and both prove the lowercase wire string is the default.
            string expectedBare = $"DEFAULT {expected}";
            string expectedParens = $"DEFAULT ({expected})";
            if (!ddl.Contains(expectedBare, StringComparison.OrdinalIgnoreCase) &&
                !ddl.Contains(expectedParens, StringComparison.OrdinalIgnoreCase))
            {
                violations.Add(
                    $"Table '{table}', column '{column}': expected DEFAULT {expected} (or DEFAULT ({expected.Trim('\'')}) form) in DDL " +
                    $"but it was not found. DDL snippet: {ddl[..Math.Min(ddl.Length, 800)]}");
            }
        }

        Assert.True(
            violations.Count == 0,
            $"{violations.Count} bounded-domain column default violation(s):\n" +
            string.Join("\n", violations.Select(v => $"  {v}")) +
            "\n\nThe EF value converter must emit lowercase wire strings as DEFAULT values in DDL. " +
            "Use .HasDefaultValueSql(\"'<wire-value>'\") to ensure the correct literal.");
    }

    /// <summary>
    /// Guards the INSERT-side correctness invariant: the CLR default value (value = 0) of each
    /// bounded-domain enum must round-trip to the wire string that matches the SQL DEFAULT literal
    /// in <c>HasDefaultValueSql("'X'")</c>. EF omits columns whose CLR value equals the type's CLR
    /// default (zero for enums), so the DB-level default fills in — that literal must match
    /// <c>default(T).ToWire()</c>. A future contributor reordering enum members silently corrupts
    /// every default-valued INSERT without this guard.
    /// </summary>
    [Fact]
    public void BoundedDomain_CLRDefault_MatchesDDLDefaultLiteral()
    {
        Assert.Equal("public", default(Visibilities).ToWire()); // packages.visibility DEFAULT 'public'
        Assert.Equal("user",   default(UserRoles).ToWire());    // users.role         DEFAULT 'user'
        Assert.Equal("member", default(OrgRoles).ToWire());     // org_members.org_role DEFAULT 'member'
    }
}
