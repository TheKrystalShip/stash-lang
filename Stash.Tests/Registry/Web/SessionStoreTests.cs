using System;
using System.Threading.Tasks;
using Stash.Registry.Web.Auth;
using Xunit;

namespace Stash.Tests.Registry.Web;

/// <summary>
/// Unit tests for <see cref="InMemorySessionStore"/>.
/// </summary>
public sealed class SessionStoreTests
{
    private static BffSession MakeSession(string username = "alice") =>
        new BffSession
        {
            Username = username,
            PublishTokenJwt = $"jwt-for-{username}",
            PublishTokenId = $"token-id-{username}",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(8),
        };

    // ── GetAsync ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_UnknownSessionId_ReturnsNull()
    {
        var store = new InMemorySessionStore();

        var result = await store.GetAsync("no-such-session");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_AfterSet_ReturnsSameSession()
    {
        var store = new InMemorySessionStore();
        var session = MakeSession("alice");
        var expiresAt = DateTimeOffset.UtcNow.AddHours(8);

        await store.SetAsync("sid1", session, expiresAt);

        var result = await store.GetAsync("sid1");

        Assert.NotNull(result);
        Assert.Equal("alice", result!.Username);
        Assert.Equal(session.PublishTokenId, result.PublishTokenId);
    }

    [Fact]
    public async Task GetAsync_AfterRemove_ReturnsNull()
    {
        var store = new InMemorySessionStore();
        var session = MakeSession("bob");
        var expiresAt = DateTimeOffset.UtcNow.AddHours(8);

        await store.SetAsync("sid2", session, expiresAt);
        await store.RemoveAsync("sid2");

        var result = await store.GetAsync("sid2");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_ExpiredSession_ReturnsNull()
    {
        var store = new InMemorySessionStore();
        var session = MakeSession("charlie");
        // Set expiry in the past.
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);

        await store.SetAsync("sid3", session, expiresAt);

        var result = await store.GetAsync("sid3");

        Assert.Null(result);
    }

    // ── SetAsync ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetAsync_OverwritesExistingSession()
    {
        var store = new InMemorySessionStore();
        var sessionV1 = MakeSession("dave-v1");
        var sessionV2 = MakeSession("dave-v2");
        var expiresAt = DateTimeOffset.UtcNow.AddHours(8);

        await store.SetAsync("sid4", sessionV1, expiresAt);
        await store.SetAsync("sid4", sessionV2, expiresAt);

        var result = await store.GetAsync("sid4");

        Assert.NotNull(result);
        Assert.Equal("dave-v2", result!.Username);
    }

    // ── RemoveAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveAsync_NonExistentSession_IsNoOp()
    {
        var store = new InMemorySessionStore();

        // Should not throw.
        await store.RemoveAsync("never-existed");
    }

    // ── Multiple sessions ─────────────────────────────────────────────────────

    [Fact]
    public async Task MultipleSessionIds_AreIndependent()
    {
        var store = new InMemorySessionStore();
        var sessionA = MakeSession("user-a");
        var sessionB = MakeSession("user-b");
        var expiresAt = DateTimeOffset.UtcNow.AddHours(8);

        await store.SetAsync("sid-a", sessionA, expiresAt);
        await store.SetAsync("sid-b", sessionB, expiresAt);

        var resultA = await store.GetAsync("sid-a");
        var resultB = await store.GetAsync("sid-b");

        Assert.NotNull(resultA);
        Assert.NotNull(resultB);
        Assert.Equal("user-a", resultA!.Username);
        Assert.Equal("user-b", resultB!.Username);
    }

    // ── BffSession record ─────────────────────────────────────────────────────

    [Fact]
    public void BffSession_DoesNotCarryRoleField()
    {
        // Enforce the spec: BffSession MUST NOT have a Role field.
        var sessionType = typeof(BffSession);
        var roleProperty = sessionType.GetProperty("Role");

        Assert.Null(roleProperty);
    }

    [Fact]
    public void BffSession_HasExpectedShape()
    {
        // Confirm the required properties exist so test helpers don't silently bind to null.
        var sessionType = typeof(BffSession);
        Assert.NotNull(sessionType.GetProperty("Username"));
        Assert.NotNull(sessionType.GetProperty("PublishTokenJwt"));
        Assert.NotNull(sessionType.GetProperty("PublishTokenId"));
        Assert.NotNull(sessionType.GetProperty("ExpiresAt"));
    }
}
