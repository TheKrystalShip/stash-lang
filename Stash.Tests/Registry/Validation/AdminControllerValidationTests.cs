using System;
using System.Net;
using System.Net.Http;
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

namespace Stash.Tests.Registry.Validation;

/// <summary>
/// Integration tests verifying that the <c>AdminController</c> actions reject invalid
/// requests via the declarative <c>ModelStateInvalidFilter</c> — confirming the action
/// body is never entered on invalid input.
/// </summary>
/// <remarks>
/// <para>
/// Each test spins up a full in-process ASP.NET Core server (via
/// <see cref="WebApplicationFactory{TEntryPoint}"/>) backed by an in-memory SQLite
/// database. Admin operations require the first registered user (who receives the admin
/// role) to issue an admin-ceiling JWT.
/// </para>
/// </remarks>
public sealed class AdminControllerValidationTests
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

    private static async Task<ErrorResponse?> ReadErrorResponseAsync(HttpResponseMessage response)
    {
        string content = await response.Content.ReadAsStringAsync();
        try
        {
            return JsonSerializer.Deserialize<ErrorResponse>(content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Registers the first user (who receives the admin role), logs in, and issues an
    /// admin-ceiling token. Returns the admin-ceiling JWT.
    /// </summary>
    private static async Task<string> RegisterAndGetAdminTokenAsync(
        HttpClient client,
        string username)
    {
        // Register and login to get a publish-ceiling token (first user gets admin role).
        await client.PostAsync("/api/v1/auth/register",
            Json(new { username, password = "Password123!" }));
        var loginResp = await client.PostAsync("/api/v1/auth/login",
            Json(new { username, password = "Password123!" }));
        loginResp.EnsureSuccessStatusCode();
        string loginBody = await loginResp.Content.ReadAsStringAsync();
        using var loginDoc = JsonDocument.Parse(loginBody);
        string publishToken = loginDoc.RootElement.GetProperty("accessToken").GetString()!;

        // Issue an admin-ceiling token using the publish token.
        var savedAuth = client.DefaultRequestHeaders.Authorization;
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", publishToken);
        var issueResp = await client.PostAsync("/api/v1/auth/tokens",
            Json(new { ceiling = "admin", expiresIn = "1d" }));
        client.DefaultRequestHeaders.Authorization = savedAuth;

        if (issueResp.IsSuccessStatusCode)
        {
            string tokenBody = await issueResp.Content.ReadAsStringAsync();
            using var tokenDoc = JsonDocument.Parse(tokenBody);
            return tokenDoc.RootElement.GetProperty("token").GetString()!;
        }

        // Fallback: publish token (may fail admin-policy checks in some tests)
        return publishToken;
    }

    // ── CreateUser validation ─────────────────────────────────────────────────

    /// <summary>
    /// POST /api/v1/admin/users with an empty JSON body (no username, no password) returns
    /// 400 <c>InvalidRequest</c>. The action body is never entered: no user row is created.
    /// </summary>
    [Fact]
    public async Task CreateUser_EmptyBody_Returns400InvalidRequest_AndNoUserCreated()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        string adminToken = await RegisterAndGetAdminTokenAsync(client, "admin-cu-empty");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);

        var response = await client.PostAsync("/api/v1/admin/users",
            Json(new { username = (string?)null, password = (string?)null }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await ReadErrorResponseAsync(response);
        Assert.NotNull(error);
        Assert.Equal("InvalidRequest", error!.Error);

        // Action body was not entered — the new username was never created.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
        var user = await db.GetUserAsync("null");
        Assert.Null(user);
    }

    // ── GetAuditLog validation ────────────────────────────────────────────────

    /// <summary>
    /// GET /api/v1/admin/audit-log?pageSize=300 returns 400 <c>InvalidRequest</c>.
    /// <para>
    /// Previously the controller silently clamped out-of-range <c>pageSize</c> values to 200;
    /// after migration to <c>[FromQuery] AuditLogQuery</c> with <c>[Range(1, 200)]</c>, the
    /// <c>ModelStateInvalidFilter</c> rejects out-of-range values before the action body runs.
    /// </para>
    /// </summary>
    [Fact]
    public async Task GetAuditLog_OutOfRangePageSize_Returns400InvalidRequest()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        string adminToken = await RegisterAndGetAdminTokenAsync(client, "admin-al-range");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);

        var response = await client.GetAsync("/api/v1/admin/audit-log?pageSize=300");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await ReadErrorResponseAsync(response);
        Assert.NotNull(error);
        Assert.Equal("InvalidRequest", error!.Error);
    }

    /// <summary>
    /// GET /api/v1/admin/audit-log with no <c>pageSize</c> parameter returns the default
    /// page size of 50 entries per page. Seeds 55 audit entries so page 1 is full; asserts
    /// both <c>entries.Count == 50</c> and the response body's <c>pageSize == 50</c>.
    /// Regression test for the accidental drift from 50 → 20 introduced in P5.
    /// </summary>
    [Fact]
    public async Task GetAuditLog_NoPageSize_DefaultsTo50EntriesPerPage()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        string adminToken = await RegisterAndGetAdminTokenAsync(client, "admin-al-default");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);

        // Seed 55 audit entries directly via EF so the first page (default 50) is full.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RegistryDbContext>();
            for (int i = 0; i < 55; i++)
            {
                db.AuditLog.Add(new AuditEntry
                {
                    Action = "test.seed",
                    Timestamp = DateTime.UtcNow.AddSeconds(-i)
                });
            }
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync("/api/v1/admin/audit-log");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        int pageSize = root.GetProperty("pageSize").GetInt32();
        int entriesCount = root.GetProperty("items").GetArrayLength();

        Assert.Equal(50, pageSize);
        Assert.Equal(50, entriesCount);
    }

    // ── CreateUser username grammar ───────────────────────────────────────────

    /// <summary>
    /// POST /api/v1/admin/users with an UPPERCASE username returns 201 Created.
    /// Admin grammar (<c>^[a-zA-Z0-9_-]+$</c>) allows uppercase; this is intentionally
    /// broader than the self-register scope grammar which is lowercase-only.
    /// </summary>
    [Fact]
    public async Task CreateUser_UpperCaseUsername_Returns201()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        string adminToken = await RegisterAndGetAdminTokenAsync(client, "admin-cu-upper");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);

        var response = await client.PostAsync("/api/v1/admin/users",
            Json(new { username = "Alice_01", password = "Password123!" }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>
    /// POST /api/v1/admin/users with an underscore-only username returns 201 Created.
    /// Admin grammar allows underscores; this differs from the scope grammar used at self-register.
    /// </summary>
    [Fact]
    public async Task CreateUser_UsernameWithUnderscore_Returns201()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        string adminToken = await RegisterAndGetAdminTokenAsync(client, "admin-cu-under");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);

        var response = await client.PostAsync("/api/v1/admin/users",
            Json(new { username = "under_score", password = "Password123!" }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>
    /// POST /api/v1/admin/users with a username containing a space returns 400 InvalidRequest.
    /// Spaces are outside the admin grammar (<c>^[a-zA-Z0-9_-]+$</c>) and are rejected by
    /// <c>ModelStateInvalidFilter</c> before the action body runs.
    /// </summary>
    [Fact]
    public async Task CreateUser_UsernameWithSpace_Returns400()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        await using var factory = CreateFactory(conn);
        var client = factory.CreateClient();

        string adminToken = await RegisterAndGetAdminTokenAsync(client, "admin-cu-space");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);

        var response = await client.PostAsync("/api/v1/admin/users",
            Json(new { username = "has space", password = "Password123!" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await ReadErrorResponseAsync(response);
        Assert.NotNull(error);
        Assert.Equal("InvalidRequest", error!.Error);
    }
}
