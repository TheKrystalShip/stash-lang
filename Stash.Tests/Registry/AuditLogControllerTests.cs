using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stash.Registry.Contracts;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;
using Xunit;

namespace Stash.Tests.Registry;

/// <summary>
/// Integration tests verifying that <c>AdminController.GetAuditLog</c> returns a
/// <see cref="PagedResponse{T}"/> of <see cref="AuditEntryResponse"/> with the
/// <c>"items"</c> wire key (not the former <c>"entries"</c> key that existed before
/// the P1 envelope migration).
/// </summary>
public sealed class AuditLogControllerTests
{
    // ── Factory ───────────────────────────────────────────────────────────────

    private static WebApplicationFactory<Stash.Registry.Program> CreateFactory(SqliteConnection conn)
    {
        return new WebApplicationFactory<Stash.Registry.Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSolutionRelativeContentRoot("Stash.Registry");
                builder.UseSetting("environment", "Development");
                builder.ConfigureTestServices(services =>
                {
                    var descriptor = services.SingleOrDefault(d =>
                        d.ServiceType == typeof(DbContextOptions<RegistryDbContext>));
                    if (descriptor != null)
                        services.Remove(descriptor);

                    services.AddDbContext<RegistryDbContext>(options =>
                        options.UseSqlite(conn));

                    var sp = services.BuildServiceProvider();
                    using var scope = sp.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
                    db.Initialize();
                });
            });
    }

    private static StringContent Json(object body)
        => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    /// <summary>
    /// Registers the first user (who receives the admin role by bootstrap), logs in to get
    /// a publish-ceiling token, then issues an admin-ceiling token. Returns the admin JWT.
    /// </summary>
    private static async Task<string> RegisterAndGetAdminTokenAsync(
        HttpClient client, string username)
    {
        // Register — first user gets admin role automatically via bootstrap
        await client.PostAsync("/api/v1/auth/register",
            Json(new { username, password = "Password123!" }));

        // Login → publish-ceiling token
        var loginResp = await client.PostAsync("/api/v1/auth/login",
            Json(new { username, password = "Password123!" }));
        loginResp.EnsureSuccessStatusCode();
        string loginBody = await loginResp.Content.ReadAsStringAsync();
        using var loginDoc = JsonDocument.Parse(loginBody);
        string publishToken = loginDoc.RootElement.GetProperty("accessToken").GetString()!;

        // Issue admin-ceiling token
        var savedAuth = client.DefaultRequestHeaders.Authorization;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", publishToken);
        var issueResp = await client.PostAsync("/api/v1/auth/tokens",
            Json(new { ceiling = "admin", expiresIn = "1d" }));
        client.DefaultRequestHeaders.Authorization = savedAuth;

        if (issueResp.IsSuccessStatusCode)
        {
            string tokenBody = await issueResp.Content.ReadAsStringAsync();
            using var tokenDoc = JsonDocument.Parse(tokenBody);
            return tokenDoc.RootElement.GetProperty("token").GetString()!;
        }

        // Fallback: use publish token (may not satisfy admin-policy in all tests)
        return publishToken;
    }

    // ── Wire shape: items key ────────────────────────────────────────────────

    /// <summary>
    /// GET /api/v1/admin/audit-log returns a JSON object with the key <c>"items"</c>
    /// (not <c>"entries"</c>). This is the P1 breaking wire change for the audit endpoint.
    /// </summary>
    [Fact]
    public async Task GetAuditLog_ReturnsItemsKey_NotEntriesKey()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        // The first registered user becomes admin
        string adminToken = await RegisterAndGetAdminTokenAsync(client, "admin-al-wire");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", adminToken);

        var response = await client.GetAsync("/api/v1/admin/audit-log");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("items", out _),
            "Audit-log response must use the key 'items' (PagedResponse<T> envelope).");
        Assert.False(root.TryGetProperty("entries", out _),
            "Audit-log response must NOT use the old key 'entries'.");
    }

    /// <summary>
    /// GET /api/v1/admin/audit-log returns all five pagination metadata fields:
    /// <c>items</c>, <c>totalCount</c>, <c>page</c>, <c>pageSize</c>, <c>totalPages</c>.
    /// </summary>
    [Fact]
    public async Task GetAuditLog_ResponseContainsAllPaginationFields()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        string adminToken = await RegisterAndGetAdminTokenAsync(client, "admin-al-fields");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", adminToken);

        var response = await client.GetAsync("/api/v1/admin/audit-log?page=1&pageSize=50");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("items", out _), "Expected 'items' field.");
        Assert.True(root.TryGetProperty("totalCount", out _), "Expected 'totalCount' field.");
        Assert.True(root.TryGetProperty("page", out var pg), "Expected 'page' field.");
        Assert.True(root.TryGetProperty("pageSize", out var ps), "Expected 'pageSize' field.");
        Assert.True(root.TryGetProperty("totalPages", out _), "Expected 'totalPages' field.");

        Assert.Equal(1, pg.GetInt32());
        Assert.Equal(50, ps.GetInt32());
    }

    // ── Per-endpoint caps remain on query DTO ─────────────────────────────────

    /// <summary>
    /// GET /api/v1/admin/audit-log?pageSize=201 returns 400 InvalidRequest (cap = 200 on AuditLogQuery).
    /// Verifies the per-endpoint cap is still enforced via the query DTO, not the envelope.
    /// </summary>
    [Fact]
    public async Task GetAuditLog_PageSizeAboveCap_Returns400()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        string adminToken = await RegisterAndGetAdminTokenAsync(client, "admin-al-cap");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", adminToken);

        var response = await client.GetAsync("/api/v1/admin/audit-log?pageSize=201");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// GET /api/v1/admin/audit-log?pageSize=200 (at the cap) returns 200 OK.
    /// The per-endpoint cap on AuditLogQuery is inclusive at 200.
    /// </summary>
    [Fact]
    public async Task GetAuditLog_PageSizeAtCap200_Returns200()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        string adminToken = await RegisterAndGetAdminTokenAsync(client, "admin-al-cap200");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", adminToken);

        var response = await client.GetAsync("/api/v1/admin/audit-log?pageSize=200");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// GET /api/v1/admin/audit-log with seeded entries returns them under the <c>"items"</c> key
    /// and the count matches. Regression guard for the P1 envelope migration.
    /// </summary>
    [Fact]
    public async Task GetAuditLog_WithEntries_ReturnsItemsArray()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        string adminToken = await RegisterAndGetAdminTokenAsync(client, "admin-al-seed");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", adminToken);

        // Seed 3 audit entries directly
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RegistryDbContext>();
            for (int i = 0; i < 3; i++)
            {
                db.AuditLog.Add(new AuditEntry
                {
                    Action = "test.seed",
                    Timestamp = DateTime.UtcNow.AddSeconds(-i)
                });
            }
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync("/api/v1/admin/audit-log?pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("items", out var items));
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        // The 3 seeded entries plus any entries created by registration/login
        Assert.True(items.GetArrayLength() >= 3,
            "Expected at least 3 items in the 'items' array.");
    }

    // ── A3: widened filter tests (user, ip, from/to) ──────────────────────────

    /// <summary>
    /// GET /api/v1/admin/audit-log?user=alice returns only alice's entries.
    /// Seeds three entries: two for alice, one for bob; verifies the filter.
    /// </summary>
    [Fact]
    public async Task GetAuditLog_FilterByUser_ReturnsOnlyThatUsersEntries()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        string adminToken = await RegisterAndGetAdminTokenAsync(client, "admin-user-filter");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", adminToken);

        // Seed entries for specific users
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RegistryDbContext>();
            db.AuditLog.Add(new AuditEntry { Action = "publish", User = "alice", Timestamp = DateTime.UtcNow.AddSeconds(-3) });
            db.AuditLog.Add(new AuditEntry { Action = "publish", User = "bob", Timestamp = DateTime.UtcNow.AddSeconds(-2) });
            db.AuditLog.Add(new AuditEntry { Action = "unpublish", User = "alice", Timestamp = DateTime.UtcNow.AddSeconds(-1) });
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync("/api/v1/admin/audit-log?user=alice&pageSize=50");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("items", out var items));
        // All returned items must be alice's
        foreach (var item in items.EnumerateArray())
        {
            Assert.True(item.TryGetProperty("user", out var u));
            Assert.Equal("alice", u.GetString());
        }
        // At least the 2 seeded entries for alice
        Assert.True(items.GetArrayLength() >= 2,
            "Expected at least 2 items for user=alice.");
    }

    /// <summary>
    /// GET /api/v1/admin/audit-log?action=X&amp;from=t0&amp;to=t1 returns only X within [t0,t1].
    /// </summary>
    [Fact]
    public async Task GetAuditLog_FilterByActionAndTimeWindow_ReturnsMatchingEntries()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        string adminToken = await RegisterAndGetAdminTokenAsync(client, "admin-action-time");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", adminToken);

        var t0 = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var t1 = new DateTime(2025, 6, 3, 0, 0, 0, DateTimeKind.Utc);
        var tOutside = new DateTime(2025, 6, 10, 0, 0, 0, DateTimeKind.Utc);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RegistryDbContext>();
            // 2 entries of "special.action" inside the window
            db.AuditLog.Add(new AuditEntry { Action = "special.action", User = "alice", Timestamp = t0 });
            db.AuditLog.Add(new AuditEntry { Action = "special.action", User = "alice", Timestamp = t1 });
            // 1 entry of "special.action" OUTSIDE the window
            db.AuditLog.Add(new AuditEntry { Action = "special.action", User = "alice", Timestamp = tOutside });
            // 1 entry of different action inside window
            db.AuditLog.Add(new AuditEntry { Action = "other.action", User = "alice", Timestamp = t0 });
            await db.SaveChangesAsync();
        }

        string from = Uri.EscapeDataString(t0.ToString("o"));
        string to = Uri.EscapeDataString(t1.ToString("o"));
        var response = await client.GetAsync(
            $"/api/v1/admin/audit-log?action=special.action&from={from}&to={to}&pageSize=50");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("totalCount", out var tc));
        Assert.Equal(2, tc.GetInt32());

        Assert.True(root.TryGetProperty("items", out var items));
        foreach (var item in items.EnumerateArray())
        {
            Assert.True(item.TryGetProperty("action", out var a));
            Assert.Equal("special.action", a.GetString());
        }
    }
}
