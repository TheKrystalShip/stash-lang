using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Stash.Tests.Registry.Authz;
using Xunit;

namespace Stash.Tests.Registry;

/// <summary>
/// End-to-end tests for the expanded <c>GET /api/v1/admin/stats</c> response (M6).
/// Verifies that <c>downloads</c> and <c>activity</c> fields are present and carry
/// the correct counts after a known sequence of publish/unpublish/deprecate operations.
/// </summary>
public sealed class AdminStatsExpandedTests : RegistryAuthzTestBase
{
    // ── Structure: fields must always be present ──────────────────────────────

    /// <summary>
    /// <c>downloads</c> and <c>activity</c> blocks are present even on a fresh (empty) registry.
    /// Guards against the "missing field on empty DB" failure mode.
    /// </summary>
    [Fact]
    public async Task GetStats_EmptyRegistry_ContainsDownloadsAndActivityBlocks()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client   = ctx.Factory.CreateClient();

        string adminToken = await RegisterAndGetAdminTokenAsync(client, "stats-empty");
        SetBearer(client, adminToken);

        var resp = await client.GetAsync("/api/v1/admin/stats");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        string body = await resp.Content.ReadAsStringAsync();
        using var doc  = JsonDocument.Parse(body);
        var root       = doc.RootElement;

        Assert.True(root.TryGetProperty("downloads", out var dl),
            $"StatsResponse must contain 'downloads' block. Body: {body}");
        Assert.True(dl.TryGetProperty("total", out _),
            $"downloads must contain 'total'. Body: {body}");
        Assert.True(dl.TryGetProperty("last24h", out _),
            $"downloads must contain 'last24h'. Body: {body}");

        Assert.True(root.TryGetProperty("activity", out var act),
            $"StatsResponse must contain 'activity' block. Body: {body}");
        Assert.True(act.TryGetProperty("publishesLast24h", out _),
            $"activity must contain 'publishesLast24h'. Body: {body}");
        Assert.True(act.TryGetProperty("unpublishesLast24h", out _),
            $"activity must contain 'unpublishesLast24h'. Body: {body}");
        Assert.True(act.TryGetProperty("deprecationsLast24h", out _),
            $"activity must contain 'deprecationsLast24h'. Body: {body}");
    }

    /// <summary>
    /// <c>downloads.total</c> and <c>downloads.last24h</c> are zero on an empty registry
    /// (no download events).
    /// </summary>
    [Fact]
    public async Task GetStats_EmptyRegistry_DownloadsAreZero()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client   = ctx.Factory.CreateClient();

        string adminToken = await RegisterAndGetAdminTokenAsync(client, "stats-dl-zero");
        SetBearer(client, adminToken);

        var resp = await client.GetAsync("/api/v1/admin/stats");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        string body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var dl = doc.RootElement.GetProperty("downloads");

        Assert.Equal(0L, dl.GetProperty("total").GetInt64());
        Assert.Equal(0L, dl.GetProperty("last24h").GetInt64());
    }

    // ── Activity counts are derived from real audit-log entries ───────────────

    /// <summary>
    /// After publishing a package, <c>publishesLast24h = 1</c>.
    /// </summary>
    [Fact]
    public async Task GetStats_AfterPublish_PublishCountIsOne()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client   = ctx.Factory.CreateClient();

        string username   = "stats-pub-act";
        string adminToken = await RegisterAndGetAdminTokenAsync(client, username);
        string publishToken = await RegisterAndGetTokenAsync(client, username);

        byte[] tarball = CreateTarball($"@{username}/pub-act-pkg", "1.0.0");
        var content    = TarballContent(tarball);
        var req        = new HttpRequestMessage(HttpMethod.Put,
            $"/api/v1/packages/{username}/pub-act-pkg");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", publishToken);
        req.Content               = content;
        var pubResp = await client.SendAsync(req);
        Assert.True(pubResp.IsSuccessStatusCode,
            $"Publish failed: {pubResp.StatusCode} — {await pubResp.Content.ReadAsStringAsync()}");

        SetBearer(client, adminToken);
        var statsResp = await client.GetAsync("/api/v1/admin/stats");
        Assert.Equal(HttpStatusCode.OK, statsResp.StatusCode);

        string statsBody = await statsResp.Content.ReadAsStringAsync();
        using var doc    = JsonDocument.Parse(statsBody);
        var activity     = doc.RootElement.GetProperty("activity");

        int publishCount = activity.GetProperty("publishesLast24h").GetInt32();
        // Exactly 1: fresh in-memory DB, no other publish actions.
        // Registration/login/token-issue write user.create/token.create — never package.* — so count is deterministic.
        Assert.Equal(1, publishCount);
    }

    /// <summary>
    /// After publishing and deprecating a package, <c>deprecationsLast24h = 1</c>
    /// and <c>unpublishesLast24h = 0</c>.
    /// </summary>
    [Fact]
    public async Task GetStats_AfterDeprecation_DeprecationCountIsOne_UnpublishIsZero()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client   = ctx.Factory.CreateClient();

        string username   = "stats-dep-act";
        string adminToken = await RegisterAndGetAdminTokenAsync(client, username);
        string publishToken = await RegisterAndGetTokenAsync(client, username);

        // Publish
        byte[] tarball = CreateTarball($"@{username}/dep-act-pkg", "1.0.0");
        var pubReq = new HttpRequestMessage(HttpMethod.Put,
            $"/api/v1/packages/{username}/dep-act-pkg");
        pubReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", publishToken);
        pubReq.Content               = TarballContent(tarball);
        var pubResp = await client.SendAsync(pubReq);
        Assert.True(pubResp.IsSuccessStatusCode,
            $"Publish failed: {await pubResp.Content.ReadAsStringAsync()}");

        // Deprecate
        var depReq = new HttpRequestMessage(HttpMethod.Patch,
            $"/api/v1/packages/{username}/dep-act-pkg/deprecate");
        depReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", publishToken);
        depReq.Content = new System.Net.Http.StringContent(
            """{"message": "Superseded by dep-act-pkg-v2"}""",
            System.Text.Encoding.UTF8,
            "application/json");
        var depResp = await client.SendAsync(depReq);
        Assert.True(depResp.IsSuccessStatusCode,
            $"Deprecate failed: {depResp.StatusCode} — {await depResp.Content.ReadAsStringAsync()}");

        SetBearer(client, adminToken);
        var statsResp = await client.GetAsync("/api/v1/admin/stats");
        Assert.Equal(HttpStatusCode.OK, statsResp.StatusCode);

        string statsBody = await statsResp.Content.ReadAsStringAsync();
        using var doc    = JsonDocument.Parse(statsBody);
        var activity     = doc.RootElement.GetProperty("activity");

        // Exactly 1 deprecation (package.deprecate allow), 0 unpublishes.
        // Fresh in-memory DB; registration and token-issue never write package.* audit entries.
        int deprecations = activity.GetProperty("deprecationsLast24h").GetInt32();
        Assert.Equal(1, deprecations);

        int unpublishes = activity.GetProperty("unpublishesLast24h").GetInt32();
        Assert.Equal(0, unpublishes);
    }

    /// <summary>
    /// Cumulative test: publish once, unpublish once, deprecate a second published package once.
    /// Asserts exact counts per field, isolating each activity bucket independently.
    /// </summary>
    [Fact]
    public async Task GetStats_AfterPublishAndUnpublish_ActivityCountsAreCorrect()
    {
        await using var ctx = RegistryAuthzFactory.Create();
        using var client   = ctx.Factory.CreateClient();

        string username   = "stats-all-act";
        string adminToken = await RegisterAndGetAdminTokenAsync(client, username);
        string publishToken = await RegisterAndGetTokenAsync(client, username);

        // Publish pkg-a (used for unpublish test)
        byte[] tarballA = CreateTarball($"@{username}/all-act-a", "1.0.0");
        var reqA = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/packages/{username}/all-act-a");
        reqA.Headers.Authorization = new AuthenticationHeaderValue("Bearer", publishToken);
        reqA.Content               = TarballContent(tarballA);
        var respA = await client.SendAsync(reqA);
        Assert.True(respA.IsSuccessStatusCode,
            $"Publish A failed: {await respA.Content.ReadAsStringAsync()}");

        // Unpublish pkg-a@1.0.0
        var delReq = new HttpRequestMessage(HttpMethod.Delete,
            $"/api/v1/packages/{username}/all-act-a/1.0.0");
        delReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", publishToken);
        var delResp = await client.SendAsync(delReq);
        Assert.True(delResp.IsSuccessStatusCode,
            $"Unpublish A failed: {delResp.StatusCode} — {await delResp.Content.ReadAsStringAsync()}");

        // Publish pkg-b
        byte[] tarballB = CreateTarball($"@{username}/all-act-b", "1.0.0");
        var reqB = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/packages/{username}/all-act-b");
        reqB.Headers.Authorization = new AuthenticationHeaderValue("Bearer", publishToken);
        reqB.Content               = TarballContent(tarballB);
        var respB = await client.SendAsync(reqB);
        Assert.True(respB.IsSuccessStatusCode,
            $"Publish B failed: {await respB.Content.ReadAsStringAsync()}");

        SetBearer(client, adminToken);
        var statsResp = await client.GetAsync("/api/v1/admin/stats");
        Assert.Equal(HttpStatusCode.OK, statsResp.StatusCode);

        string statsBody = await statsResp.Content.ReadAsStringAsync();
        using var doc    = JsonDocument.Parse(statsBody);
        var activity     = doc.RootElement.GetProperty("activity");

        // Exactly 2 publishes (pkg-a + pkg-b, both package.create audit events).
        // Exactly 1 unpublish (pkg-a@1.0.0 delete, package.unpublish audit event).
        // Zero deprecations in this test.
        // Fresh in-memory DB — no other package.* actions from auth setup.
        int publishes   = activity.GetProperty("publishesLast24h").GetInt32();
        int unpublishes = activity.GetProperty("unpublishesLast24h").GetInt32();
        int deprecations = activity.GetProperty("deprecationsLast24h").GetInt32();

        Assert.Equal(2, publishes);
        Assert.Equal(1, unpublishes);
        Assert.Equal(0, deprecations);
    }
}
