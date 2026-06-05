using Microsoft.EntityFrameworkCore;
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
        _audit = new AuditService(_db);
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
}
