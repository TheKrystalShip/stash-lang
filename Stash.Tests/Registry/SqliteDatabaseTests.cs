using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;
using System.Threading.Tasks;
using Xunit;

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

        SearchResult result = await db.SearchPackagesAsync("alpha", 1, 10);

        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Packages);
        Assert.Equal("search-alpha", result.Packages[0].Name);
    }

    [Fact]
    public async Task SearchPackages_MatchesByDescription()
    {
        var db = CreateTestDb();
        var pkg = MakePackage("pkg-x");
        pkg.Description = "A unique-description here";
        await db.CreatePackageAsync(pkg);

        SearchResult result = await db.SearchPackagesAsync("unique-description", 1, 10);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("pkg-x", result.Packages[0].Name);
    }

    [Fact]
    public async Task SearchPackages_NoMatch_ReturnsEmpty()
    {
        var db = CreateTestDb();
        await db.CreatePackageAsync(MakePackage("no-match-pkg"));

        SearchResult result = await db.SearchPackagesAsync("zzzzz-nonexistent", 1, 10);

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

        SearchResult page1 = await db.SearchPackagesAsync("page-pkg", 1, 2);
        SearchResult page2 = await db.SearchPackagesAsync("page-pkg", 2, 2);
        SearchResult page3 = await db.SearchPackagesAsync("page-pkg", 3, 2);

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
        Assert.Equal("user", result.Role);
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

    // ── Ownership operations ────────────────────────────────────────────

    [Fact]
    public async Task AddOwner_GetOwners_Works()
    {
        var db = CreateTestDb();
        await db.CreatePackageAsync(MakePackage("own-pkg"));
        await db.AddOwnerAsync("own-pkg", "alice");
        await db.AddOwnerAsync("own-pkg", "bob");

        List<string> owners = await db.GetOwnersAsync("own-pkg");

        Assert.Equal(2, owners.Count);
        Assert.Contains("alice", owners);
        Assert.Contains("bob", owners);
    }

    [Fact]
    public async Task RemoveOwner_RemovesOwner()
    {
        var db = CreateTestDb();
        await db.CreatePackageAsync(MakePackage("rmown-pkg"));
        await db.AddOwnerAsync("rmown-pkg", "alice");
        await db.AddOwnerAsync("rmown-pkg", "bob");

        await db.RemoveOwnerAsync("rmown-pkg", "alice");

        List<string> owners = await db.GetOwnersAsync("rmown-pkg");
        Assert.Single(owners);
        Assert.Equal("bob", owners[0]);
    }

    [Fact]
    public async Task IsOwner_ReturnsCorrectly()
    {
        var db = CreateTestDb();
        await db.CreatePackageAsync(MakePackage("isown-pkg"));
        await db.AddOwnerAsync("isown-pkg", "alice");

        Assert.True(await db.IsOwnerAsync("isown-pkg", "alice"));
        Assert.False(await db.IsOwnerAsync("isown-pkg", "bob"));
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
}
