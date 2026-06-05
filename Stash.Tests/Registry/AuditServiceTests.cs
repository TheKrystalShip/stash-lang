using System.Net;
using Microsoft.EntityFrameworkCore;
using Stash.Registry.Configuration;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;
using Stash.Registry.Services;

namespace Stash.Tests.Registry;

public sealed class AuditServiceTests : IDisposable
{
    private readonly RegistryDbContext _context;
    private readonly StashRegistryDatabase _db;
    private readonly AuditService _audit;

    public AuditServiceTests()
    {
        var options = new DbContextOptionsBuilder<RegistryDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _context = new RegistryDbContext(options);
        _context.Database.OpenConnection();
        _db = new StashRegistryDatabase(_context);
        _db.Initialize();
        // Use IpMode=Raw for existing tests so stored IP == supplied raw IP.
        var ipHasher = new IpHasher(IpHandlingMode.Raw);
        _audit = new AuditService(_db, ipHasher);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    [Fact]
    public async Task LogPublish_CreatesEntry()
    {
        await _audit.LogPublishAsync("my-pkg", "1.0.0", "alice", "127.0.0.1");

        SearchResult<AuditEntry> log = await _db.GetAuditLogAsync(1, 10, null, null);

        Assert.Equal(1, log.TotalCount);
        Assert.Equal("publish", log.Items[0].Action);
        Assert.Equal("my-pkg", log.Items[0].Package);
        Assert.Equal("1.0.0", log.Items[0].Version);
        Assert.Equal("alice", log.Items[0].User);
        Assert.Equal("127.0.0.1", log.Items[0].Ip);
    }

    [Fact]
    public async Task LogUnpublish_CreatesEntry()
    {
        await _audit.LogUnpublishAsync("my-pkg", "1.0.0", "alice", "10.0.0.1");

        SearchResult<AuditEntry> log = await _db.GetAuditLogAsync(1, 10, null, "unpublish");

        Assert.Equal(1, log.TotalCount);
        Assert.Equal("unpublish", log.Items[0].Action);
    }

    [Fact]
    public async Task LogRoleAssign_CreatesEntry()
    {
        await _audit.LogRoleAssignAsync("my-pkg", "alice", "bob", "127.0.0.1");

        SearchResult<AuditEntry> log = await _db.GetAuditLogAsync(1, 10, null, "role.assign");

        Assert.Equal(1, log.TotalCount);
        Assert.Equal("role.assign", log.Items[0].Action);
        Assert.Equal("bob", log.Items[0].Target);
    }

    [Fact]
    public async Task LogTokenCreate_CreatesEntry()
    {
        await _audit.LogTokenCreateAsync("alice", "127.0.0.1");

        SearchResult<AuditEntry> log = await _db.GetAuditLogAsync(1, 10, null, "token.create");

        Assert.Equal(1, log.TotalCount);
        Assert.Equal("token.create", log.Items[0].Action);
        Assert.Equal("alice", log.Items[0].User);
    }

    // ── A2: auth event helpers ────────────────────────────────────────────────

    [Fact]
    public async Task LogAuthLoginSuccess_CreatesAllowEntry()
    {
        await _audit.LogAuthLoginSuccessAsync("alice", "127.0.0.1");

        SearchResult<AuditEntry> log = await _db.GetAuditLogAsync(1, 10, null, "auth.login.success");

        Assert.Equal(1, log.TotalCount);
        Assert.Equal(AuditActions.AuthLoginSuccess, log.Items[0].Action);
        Assert.Equal("alice", log.Items[0].User);
        Assert.Equal("allow", log.Items[0].Decision);
    }

    [Fact]
    public async Task LogAuthLoginFailure_CreatesDenyEntry()
    {
        await _audit.LogAuthLoginFailureAsync("bob", "10.0.0.1");

        SearchResult<AuditEntry> log = await _db.GetAuditLogAsync(1, 10, null, "auth.login.failure");

        Assert.Equal(1, log.TotalCount);
        Assert.Equal(AuditActions.AuthLoginFailure, log.Items[0].Action);
        Assert.Equal("bob", log.Items[0].User);
        Assert.Equal("deny", log.Items[0].Decision);
    }

    [Fact]
    public async Task LogAuthRefreshFailure_CreatesDenyEntry_NullUserAllowed()
    {
        await _audit.LogAuthRefreshFailureAsync(null, null);

        SearchResult<AuditEntry> log = await _db.GetAuditLogAsync(1, 10, null, "auth.refresh.failure");

        Assert.Equal(1, log.TotalCount);
        Assert.Equal(AuditActions.AuthRefreshFailure, log.Items[0].Action);
        Assert.Null(log.Items[0].User);
        Assert.Equal("deny", log.Items[0].Decision);
    }

    [Fact]
    public async Task LogAuthRegister_CreatesAllowEntry()
    {
        await _audit.LogAuthRegisterAsync("newuser", "192.168.1.1");

        SearchResult<AuditEntry> log = await _db.GetAuditLogAsync(1, 10, null, "auth.register");

        Assert.Equal(1, log.TotalCount);
        Assert.Equal(AuditActions.AuthRegister, log.Items[0].Action);
        Assert.Equal("newuser", log.Items[0].User);
        Assert.Equal("allow", log.Items[0].Decision);
    }

    [Fact]
    public async Task GetAuditLog_ReturnsPaginated()
    {
        for (int i = 0; i < 5; i++)
        {
            await _audit.LogPublishAsync($"pkg-{i}", "1.0.0", "alice", null);
        }

        SearchResult<AuditEntry> page1 = await _audit.GetAuditLogAsync(1, 2);
        SearchResult<AuditEntry> page2 = await _audit.GetAuditLogAsync(2, 2);
        SearchResult<AuditEntry> page3 = await _audit.GetAuditLogAsync(3, 2);

        Assert.Equal(5, page1.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(2, page2.Items.Count);
        Assert.Single(page3.Items);
    }

    // ── A3: write-time IP transform ───────────────────────────────────────────

    /// <summary>
    /// With IpMode=Hashed the stored Ip column holds the HMAC hash, not the raw IP.
    /// Asserts D11 write-time transform: AuditService applies IIpHasher.Apply once before
    /// constructing every AuditEntry.
    /// </summary>
    [Fact]
    public async Task LogPublish_HashedMode_StoresHashedIpNotRaw()
    {
        byte[] key = new byte[32];
        var hashedHasher = new IpHasher(IpHandlingMode.Hashed, key);
        var auditHashed = new AuditService(_db, hashedHasher);

        const string rawIp = "203.0.113.7";
        await auditHashed.LogPublishAsync("my-pkg", "1.0.0", "alice", rawIp);

        SearchResult<AuditEntry> log = await _db.GetAuditLogAsync(1, 10, null, null);

        Assert.Equal(1, log.TotalCount);
        string? storedIp = log.Items[0].Ip;
        Assert.NotNull(storedIp);
        Assert.NotEqual(rawIp, storedIp); // must NOT store the raw IP
        Assert.Equal(32, storedIp.Length); // HMAC-SHA256 truncated to 32 hex chars
    }

    /// <summary>
    /// With IpMode=Off the stored Ip column is null for every audit entry.
    /// </summary>
    [Fact]
    public async Task LogPublish_OffMode_StoresNullIp()
    {
        var offHasher = new IpHasher(IpHandlingMode.Off);
        var auditOff = new AuditService(_db, offHasher);

        await auditOff.LogPublishAsync("my-pkg", "1.0.0", "alice", "10.0.0.1");

        SearchResult<AuditEntry> log = await _db.GetAuditLogAsync(1, 10, null, null);

        Assert.Equal(1, log.TotalCount);
        Assert.Null(log.Items[0].Ip);
    }

    // ── A3: filter widening ───────────────────────────────────────────────────

    /// <summary>
    /// GetAuditLog filtered by user returns only that user's entries.
    /// </summary>
    [Fact]
    public async Task GetAuditLog_FilterByUser_ReturnsOnlyThatUser()
    {
        await _audit.LogPublishAsync("pkg-a", "1.0.0", "alice", null);
        await _audit.LogPublishAsync("pkg-b", "1.0.0", "bob", null);
        await _audit.LogPublishAsync("pkg-c", "1.0.0", "alice", null);

        SearchResult<AuditEntry> result = await _audit.GetAuditLogAsync(1, 10, user: "alice");

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, e => Assert.Equal("alice", e.User));
    }

    /// <summary>
    /// GetAuditLog filtered by from+to (inclusive) returns only entries within the window.
    /// </summary>
    [Fact]
    public async Task GetAuditLog_FilterByFromTo_ReturnsEntriesWithinWindow()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var t1 = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

        // Seed directly to control timestamps precisely
        await _db.AddAuditEntryAsync(new AuditEntry { Action = "publish", User = "alice", Timestamp = t0 });
        await _db.AddAuditEntryAsync(new AuditEntry { Action = "publish", User = "alice", Timestamp = t1 });
        await _db.AddAuditEntryAsync(new AuditEntry { Action = "publish", User = "alice", Timestamp = t2 });

        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);

        SearchResult<AuditEntry> result = await _audit.GetAuditLogAsync(
            1, 10, from: from, to: to);

        // t0 and t1 are within [from, to]; t2 is outside
        Assert.Equal(2, result.TotalCount);
    }

    /// <summary>
    /// The ip filter transforms the operator-supplied raw IP through IIpHasher and matches
    /// the stored (already-transformed) value.  With IpMode=Hashed, the filter finds exactly
    /// the entries written from the source IP.
    /// </summary>
    [Fact]
    public async Task GetAuditLog_FilterByIp_HashedMode_MatchesStoredHash()
    {
        byte[] key = new byte[32];
        var hashedHasher = new IpHasher(IpHandlingMode.Hashed, key);
        var auditHashed = new AuditService(_db, hashedHasher);

        const string sourceIp = "203.0.113.7";
        const string otherIp = "10.0.0.1";
        await auditHashed.LogPublishAsync("pkg-a", "1.0.0", "alice", sourceIp);
        await auditHashed.LogPublishAsync("pkg-b", "1.0.0", "alice", sourceIp);
        await auditHashed.LogPublishAsync("pkg-c", "1.0.0", "alice", otherIp);

        // The filter hashes the raw IP the same way as the write path → matches stored hashes.
        SearchResult<AuditEntry> result = await auditHashed.GetAuditLogAsync(1, 10, ip: sourceIp);

        Assert.Equal(2, result.TotalCount);

        // The stored Ip must be the hash, not the raw IP.
        Assert.All(result.Items, e =>
        {
            Assert.NotNull(e.Ip);
            Assert.NotEqual(sourceIp, e.Ip);
        });
    }

    /// <summary>
    /// With IpMode=Off, the ip filter returns an empty result set (stored column is null;
    /// WHERE Ip == null would match all entries, which is wrong — AuditService short-circuits).
    /// </summary>
    [Fact]
    public async Task GetAuditLog_FilterByIp_OffMode_ReturnsEmpty()
    {
        var offHasher = new IpHasher(IpHandlingMode.Off);
        var auditOff = new AuditService(_db, offHasher);

        await auditOff.LogPublishAsync("pkg-a", "1.0.0", "alice", "10.0.0.1");
        await auditOff.LogPublishAsync("pkg-b", "1.0.0", "alice", "10.0.0.1");

        // Operator supplies a raw IP but mode=off → AuditService short-circuits → empty
        SearchResult<AuditEntry> result = await auditOff.GetAuditLogAsync(1, 10, ip: "10.0.0.1");

        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
    }
}
