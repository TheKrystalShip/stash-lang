using System;
using System.Collections.Generic;
using System.IO;
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
using Stash.Registry.Configuration;
using Stash.Registry.Contracts;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;
using Stash.Registry.Services;
using System.Linq;
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

    // ── A4: export endpoint tests ─────────────────────────────────────────────

    /// <summary>
    /// Helper: seeds N audit entries and returns their action values for comparison.
    /// </summary>
    private static async Task SeedExportEntriesAsync(
        WebApplicationFactory<Stash.Registry.Program> factory,
        int count, string actionPrefix = "export.test")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RegistryDbContext>();
        for (int i = 0; i < count; i++)
        {
            db.AuditLog.Add(new AuditEntry
            {
                Action    = $"{actionPrefix}.{i}",
                User      = "export-user",
                Package   = "@test/pkg",
                Decision  = "allow",
                Timestamp = DateTime.UtcNow.AddSeconds(-i),
            });
        }
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// GET /api/v1/admin/audit-log/export?format=jsonl returns 200 with
    /// Content-Type: application/x-ndjson and each line parses as AuditEntryResponse.
    /// </summary>
    [Fact]
    public async Task ExportAuditLog_FormatJsonl_Returns200WithNdJson()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        string adminToken = await RegisterAndGetAdminTokenAsync(client, "admin-exp-jsonl");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", adminToken);

        await SeedExportEntriesAsync(factory, 3);

        var response = await client.GetAsync("/api/v1/admin/audit-log/export?format=jsonl");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        Assert.Equal("application/x-ndjson", contentType);

        string body = await response.Content.ReadAsStringAsync();
        var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length >= 3, $"Expected at least 3 JSONL lines, got {lines.Length}.");

        // Each line must parse as an AuditEntryResponse with a non-empty action
        foreach (string line in lines)
        {
            using var doc = JsonDocument.Parse(line);
            Assert.True(doc.RootElement.TryGetProperty("action", out var actionProp));
            Assert.False(string.IsNullOrEmpty(actionProp.GetString()), "Every JSONL line must have a non-empty 'action' field.");
        }
    }

    /// <summary>
    /// GET /api/v1/admin/audit-log/export?format=csv returns 200 with
    /// Content-Type: text/csv, a fixed-column header row, and data rows that equal
    /// the filtered list.
    /// </summary>
    [Fact]
    public async Task ExportAuditLog_FormatCsv_Returns200WithCsvHeaderAndRows()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        string adminToken = await RegisterAndGetAdminTokenAsync(client, "admin-exp-csv");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", adminToken);

        await SeedExportEntriesAsync(factory, 2, "csv.test");

        var response = await client.GetAsync(
            "/api/v1/admin/audit-log/export?format=csv&user=export-user&action=csv.test.0");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        Assert.Equal("text/csv", contentType);

        string body = await response.Content.ReadAsStringAsync();
        var lines = body.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);

        // First line must be the fixed header (A6 adds previousHash,entryHash).
        Assert.True(lines.Length >= 1, "CSV must have at least a header row.");
        Assert.Equal("action,package,version,user,target,ip,timestamp,decision,denyReason,previousHash,entryHash", lines[0]);

        // Data rows: filter matched exactly 1 entry (csv.test.0) — at least 1 data row
        Assert.True(lines.Length >= 2, "Expected at least one data row.");

        // The first data row must have the right action in the first column
        string[] firstDataCols = lines[1].Split(',');
        Assert.Equal("csv.test.0", firstDataCols[0]);
    }

    /// <summary>
    /// GET /api/v1/admin/audit-log/export?format=xml returns 400 (unknown format).
    /// The bounded <see cref="AuditExportFormat"/> enum rejects unknown values at model-binding.
    /// </summary>
    [Fact]
    public async Task ExportAuditLog_FormatXml_Returns400()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        string adminToken = await RegisterAndGetAdminTokenAsync(client, "admin-exp-xml");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", adminToken);

        var response = await client.GetAsync("/api/v1/admin/audit-log/export?format=xml");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// GET /api/v1/admin/audit-log/export with no ?format parameter returns 400
    /// (<c>[Required]</c> on <see cref="AuditExportQuery.format"/> forces model-binding to reject it).
    /// </summary>
    [Fact]
    public async Task ExportAuditLog_MissingFormat_Returns400()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        string adminToken = await RegisterAndGetAdminTokenAsync(client, "admin-exp-nofmt");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", adminToken);

        var response = await client.GetAsync("/api/v1/admin/audit-log/export");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// JSONL export honours filters: <c>?format=jsonl&amp;user=export-user</c> returns only
    /// entries for that user; the set equals the same-filtered list response.
    /// </summary>
    [Fact]
    public async Task ExportAuditLog_JsonlWithUserFilter_ReturnsFilteredEntries()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        string adminToken = await RegisterAndGetAdminTokenAsync(client, "admin-exp-filter");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", adminToken);

        // Seed 2 entries for "alice" and 2 for "bob"
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RegistryDbContext>();
            db.AuditLog.Add(new AuditEntry { Action = "filter.test", User = "alice-exp", Timestamp = DateTime.UtcNow.AddSeconds(-4) });
            db.AuditLog.Add(new AuditEntry { Action = "filter.test", User = "bob-exp",   Timestamp = DateTime.UtcNow.AddSeconds(-3) });
            db.AuditLog.Add(new AuditEntry { Action = "filter.test", User = "alice-exp", Timestamp = DateTime.UtcNow.AddSeconds(-2) });
            db.AuditLog.Add(new AuditEntry { Action = "filter.test", User = "bob-exp",   Timestamp = DateTime.UtcNow.AddSeconds(-1) });
            await db.SaveChangesAsync();
        }

        var exportResp = await client.GetAsync(
            "/api/v1/admin/audit-log/export?format=jsonl&user=alice-exp");
        Assert.Equal(HttpStatusCode.OK, exportResp.StatusCode);

        string exportBody = await exportResp.Content.ReadAsStringAsync();
        var exportLines = exportBody.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // All returned lines must be for alice-exp
        foreach (string line in exportLines)
        {
            using var doc = JsonDocument.Parse(line);
            Assert.True(doc.RootElement.TryGetProperty("user", out var u));
            Assert.Equal("alice-exp", u.GetString());
        }

        // Export count must match the list endpoint count
        var listResp = await client.GetAsync(
            "/api/v1/admin/audit-log?user=alice-exp&pageSize=50");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        string listBody = await listResp.Content.ReadAsStringAsync();
        using var listDoc = JsonDocument.Parse(listBody);
        int listCount = listDoc.RootElement.GetProperty("totalCount").GetInt32();

        Assert.Equal(listCount, exportLines.Length);
    }

    /// <summary>
    /// CSV export contains a quoted field when the value contains a comma,
    /// verifying RFC-4180 quoting.
    /// </summary>
    [Fact]
    public async Task ExportAuditLog_Csv_QuotesFieldsWithCommas()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        string adminToken = await RegisterAndGetAdminTokenAsync(client, "admin-exp-quote");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", adminToken);

        // Seed an entry whose Package contains a comma
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RegistryDbContext>();
            db.AuditLog.Add(new AuditEntry
            {
                Action    = "quote.test",
                Package   = "@test/has,comma",
                User      = "quote-user",
                Decision  = "allow",
                Timestamp = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync(
            "/api/v1/admin/audit-log/export?format=csv&action=quote.test&user=quote-user");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        // The package field contains a comma, so it must appear quoted in the output
        Assert.Contains("\"@test/has,comma\"", body);
    }

    // ── A6: verify endpoint (HTTP-level) ─────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="WebApplicationFactory{TEntryPoint}"/> with tamper-evidence enabled,
    /// wiring a fresh <see cref="AuditChainHasher"/> (SHA-256, no secret) as a singleton.
    /// </summary>
    private static WebApplicationFactory<Stash.Registry.Program> CreateFactoryWithTamperEvidence(SqliteConnection conn)
    {
        return new WebApplicationFactory<Stash.Registry.Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSolutionRelativeContentRoot("Stash.Registry");
                builder.UseSetting("environment", "Development");
                builder.ConfigureTestServices(services =>
                {
                    // Replace the DbContext with the in-memory connection.
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

                    // Replace the AuditChainHasher singleton with an enabled instance.
                    var existing = services.SingleOrDefault(d => d.ServiceType == typeof(AuditChainHasher));
                    if (existing != null)
                        services.Remove(existing);
                    services.AddSingleton(new AuditChainHasher(
                        new AuditTamperEvidenceConfig { Enabled = true, HashSecret = null }));
                });
            });
    }

    /// <summary>
    /// GET /api/v1/admin/audit-log/verify with tamper-evidence on returns
    /// <c>enabled=true, valid=true, checkedCount&gt;=3</c> after seeding 3 publish entries.
    /// Registration/login events are also hashed (factory enables tamper-evidence globally), so
    /// checkedCount includes more than the 3 seeded entries.
    /// This is the end-to-end HTTP-level proof that the controller calls <see cref="AuditChainHasher.WalkChainAsync"/>
    /// (the same walker exercised by the unit tests in <c>AuditTamperEvidenceTests</c>).
    /// </summary>
    [Fact]
    public async Task VerifyAuditLog_TamperEvidenceEnabled_ValidChain_ReturnsValidTrue()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactoryWithTamperEvidence(conn);
        var client = factory.CreateClient();

        string adminToken = await RegisterAndGetAdminTokenAsync(client, "admin-verify-valid");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", adminToken);

        // Seed 3 hashed entries using the AuditService (goes through AuditChainHasher).
        using (var scope = factory.Services.CreateScope())
        {
            var db     = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
            var hasher = scope.ServiceProvider.GetRequiredService<AuditChainHasher>();
            var audit  = new AuditService(db, new IpHasher(IpHandlingMode.Raw), hasher);
            await audit.LogPublishAsync("@a/foo", "1.0.0", "alice", null);
            await audit.LogPublishAsync("@a/foo", "1.0.1", "alice", null);
            await audit.LogPublishAsync("@a/foo", "1.0.2", "alice", null);
        }

        var response = await client.GetAsync("/api/v1/admin/audit-log/verify");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("enabled").GetBoolean(),   "enabled must be true");
        Assert.True(root.GetProperty("valid").GetBoolean(),     "valid must be true for untampered chain");
        // checkedCount includes audit events from registration/login (factory has tamper-evidence
        // enabled globally), so there will be more than 3 hashed entries total.
        Assert.True(root.GetProperty("checkedCount").GetInt32() >= 3,
            "checkedCount must include at least the 3 seeded publish entries");
        Assert.Equal(JsonValueKind.Null, root.GetProperty("firstBrokenId").ValueKind);
    }

    /// <summary>
    /// GET /api/v1/admin/audit-log/verify after directly mutating one entry's <c>User</c>
    /// column returns <c>valid=false, firstBrokenId=&lt;that entry's id&gt;</c>.
    /// </summary>
    [Fact]
    public async Task VerifyAuditLog_TamperedEntry_ReturnsValidFalseWithBrokenId()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactoryWithTamperEvidence(conn);
        var client = factory.CreateClient();

        string adminToken = await RegisterAndGetAdminTokenAsync(client, "admin-verify-tamper");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", adminToken);

        // Seed 3 hashed entries.
        using (var scope = factory.Services.CreateScope())
        {
            var db     = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
            var hasher = scope.ServiceProvider.GetRequiredService<AuditChainHasher>();
            var audit  = new AuditService(db, new IpHasher(IpHandlingMode.Raw), hasher);
            await audit.LogPublishAsync("@a/foo", "1.0.0", "alice", null);
            await audit.LogPublishAsync("@a/foo", "1.0.1", "alice", null);
            await audit.LogPublishAsync("@a/foo", "1.0.2", "alice", null);
        }

        // Mutate the 2nd entry's User field directly (DB-level tamper).
        int tamperedId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RegistryDbContext>();
            var entries = db.AuditLog.OrderBy(e => e.Id).ToList();
            var target  = entries[1]; // 2nd entry
            tamperedId  = target.Id;
            target.User = "mallory";
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync("/api/v1/admin/audit-log/verify");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("enabled").GetBoolean(),    "enabled must be true");
        Assert.False(root.GetProperty("valid").GetBoolean(),     "valid must be false after tamper");
        Assert.Equal(tamperedId, root.GetProperty("firstBrokenId").GetInt32());
    }

    /// <summary>
    /// GET /api/v1/admin/audit-log/verify with tamper-evidence disabled returns
    /// <c>enabled=false, valid=true, checkedCount=0</c>.
    /// </summary>
    [Fact]
    public async Task VerifyAuditLog_TamperEvidenceDisabled_ReturnsEnabledFalse()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn); // default factory — tamper-evidence off
        var client = factory.CreateClient();

        string adminToken = await RegisterAndGetAdminTokenAsync(client, "admin-verify-off");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", adminToken);

        var response = await client.GetAsync("/api/v1/admin/audit-log/verify");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("enabled").GetBoolean(), "enabled must be false when tamper-evidence is off");
        Assert.True(root.GetProperty("valid").GetBoolean(),    "valid must be true when disabled (trivially)");
        Assert.Equal(0, root.GetProperty("checkedCount").GetInt32());
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
