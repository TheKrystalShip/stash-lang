using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
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
using Stash.Registry;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;

namespace Stash.Tests.Registry.Authz;

/// <summary>
/// A disposable wrapper combining a <see cref="WebApplicationFactory{TEntryPoint}"/> with its
/// backing in-memory SQLite connection, so each test can use a fresh factory+connection pair
/// with <c>await using var ctx = RegistryAuthzFactory.Create()</c>.
/// </summary>
public sealed class RegistryTestContext : IAsyncDisposable
{
    public WebApplicationFactory<Stash.Registry.Program> Factory { get; }
    private readonly SqliteConnection _conn;

    public RegistryTestContext(WebApplicationFactory<Stash.Registry.Program> factory, SqliteConnection conn)
    {
        Factory = factory;
        _conn = conn;
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        _conn.Dispose();
    }
}

/// <summary>
/// Helper factory for P3 authorization integration tests.
/// Each test creates a fresh context via <c>await using var ctx = RegistryAuthzFactory.Create()</c>.
/// </summary>
public static class RegistryAuthzFactory
{
    public static RegistryTestContext Create()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var capturedConn = conn;

        var factory = new WebApplicationFactory<Stash.Registry.Program>()
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
                        options.UseSqlite(capturedConn));

                    var sp = services.BuildServiceProvider();
                    using var scope = sp.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
                    db.Initialize();
                });
            });

        return new RegistryTestContext(factory, conn);
    }

    /// <summary>
    /// Like <see cref="Create"/> but backs the registry with a <b>shared-cache</b> in-memory
    /// SQLite database (unique per call) instead of a single shared connection. Each request
    /// opens its OWN connection to the same in-memory DB, so tests that fire many concurrent
    /// requests do not race on one ADO.NET connection — concurrent command execution on a
    /// single connection is not supported and surfaces as spurious 500s. <c>Default Timeout</c>
    /// makes a blocked writer wait for the SQLite write lock (busy-wait) instead of throwing
    /// <c>SQLITE_BUSY</c> under legitimate multi-connection contention.
    ///
    /// A keepalive connection is held open for the context's lifetime because an in-memory
    /// SQLite database is dropped when its last connection closes. Use this for the concurrency
    /// suites only; sequential tests should use <see cref="Create"/>.
    /// </summary>
    /// <param name="configure">
    /// Optional hook to replace the registry's <c>RegistryConfig</c> (e.g. to set
    /// <c>ScopeOwnershipPolicy = Open</c>). When null, the app's appsettings config is used.
    /// </param>
    public static RegistryTestContext CreateConcurrent(
        Action<Stash.Registry.Configuration.RegistryConfig>? configure = null)
    {
        string dbName = $"reg-concurrent-{Guid.NewGuid():N}";
        string connString =
            $"Data Source=file:{dbName}?mode=memory&cache=shared;Default Timeout=30";

        var keepAlive = new SqliteConnection(connString);
        keepAlive.Open();

        var factory = new WebApplicationFactory<Stash.Registry.Program>()
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

                    // Pass the connection STRING (not a single connection) so every scoped
                    // DbContext opens its own connection to the shared-cache in-memory DB.
                    services.AddDbContext<RegistryDbContext>(options =>
                        options.UseSqlite(connString));

                    if (configure != null)
                    {
                        var cfgDescriptor = services.SingleOrDefault(d =>
                            d.ServiceType == typeof(Stash.Registry.Configuration.RegistryConfig));
                        if (cfgDescriptor != null)
                            services.Remove(cfgDescriptor);
                        var cfg = new Stash.Registry.Configuration.RegistryConfig();
                        configure(cfg);
                        services.AddSingleton(cfg);
                    }

                    var sp = services.BuildServiceProvider();
                    using var scope = sp.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
                    db.Initialize();
                });
            });

        return new RegistryTestContext(factory, keepAlive);
    }
}

/// <summary>
/// Base class providing static helper methods for P3 authorization integration tests.
/// </summary>
public abstract class RegistryAuthzTestBase
{
    protected static StringContent Json(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    protected static void SetBearer(HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }

    protected static async Task<string> RegisterAndGetTokenAsync(
        HttpClient client,
        string username,
        string password = "Password123!")
    {
        await client.PostAsync("/api/v1/auth/register", Json(new { username, password }));
        var loginResp = await client.PostAsync("/api/v1/auth/login", Json(new { username, password }));
        loginResp.EnsureSuccessStatusCode();
        var loginBody = await loginResp.Content.ReadAsStringAsync();
        using var loginDoc = JsonDocument.Parse(loginBody);
        string loginToken = loginDoc.RootElement.GetProperty("accessToken").GetString()!;

        // Login now issues a read-ceiling token (least-privilege default, P5).
        // Callers that need to publish (all P3/P4 write tests) need a publish-ceiling token.
        // Issue one explicitly so existing tests continue to work unchanged.
        var savedAuth = client.DefaultRequestHeaders.Authorization;
        SetBearer(client, loginToken);
        var tokenResp = await client.PostAsync("/api/v1/auth/tokens",
            Json(new { ceiling = "publish", expiresIn = "1d" }));
        client.DefaultRequestHeaders.Authorization = savedAuth;

        if (tokenResp.IsSuccessStatusCode)
        {
            var tokenBody = await tokenResp.Content.ReadAsStringAsync();
            using var tokenDoc = JsonDocument.Parse(tokenBody);
            if (tokenDoc.RootElement.TryGetProperty("token", out var tok) && tok.GetString() != null)
                return tok.GetString()!;
        }

        // Fallback: return the login (read-ceiling) token — only for tests that do not write.
        return loginToken;
    }

    /// <summary>
    /// Registers the first user (who gets admin role) and issues an admin-scoped token.
    /// Only works for the FIRST registered user (who gets admin role automatically).
    /// </summary>
    protected static async Task<string> RegisterAndGetAdminTokenAsync(
        HttpClient client,
        string username,
        string password = "Password123!")
    {
        // Register + login to get a publish-scoped token
        string publishToken = await RegisterAndGetTokenAsync(client, username, password);

        // Use the publish token to issue an admin-scoped token
        var savedAuth = client.DefaultRequestHeaders.Authorization;
        SetBearer(client, publishToken);
        var issueResp = await client.PostAsync("/api/v1/auth/tokens",
            Json(new { scope = "admin", expiresIn = "1d" }));

        if (issueResp.IsSuccessStatusCode)
        {
            var body = await issueResp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            // The response uses "token" (lowercase) per TokenCreateResponse
            if (doc.RootElement.TryGetProperty("token", out var tok) && tok.GetString() != null)
            {
                client.DefaultRequestHeaders.Authorization = savedAuth;
                return tok.GetString()!;
            }
        }

        client.DefaultRequestHeaders.Authorization = savedAuth;
        // Fallback: return publish token (may fail admin-policy check)
        return publishToken;
    }

    protected static async Task SeedScopeAsync(
        WebApplicationFactory<Stash.Registry.Program> factory,
        string scopeName,
        string ownerUsername)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
        if (!await db.ScopeExistsAsync(scopeName))
        {
            await db.CreateScopeAsync(new ScopeRecord
            {
                Name = scopeName,
                OwnerType = "user",
                OwnerUsername = ownerUsername
            });
        }
    }

    protected static async Task SeedPackageAsync(
        WebApplicationFactory<Stash.Registry.Program> factory,
        string packageName,
        string creatorUsername,
        string visibility = "public",
        string version = "1.0.0")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();

        if (!await db.PackageExistsAsync(packageName))
        {
            await db.CreatePackageAsync(new PackageRecord
            {
                Name = packageName,
                Latest = version,
                Visibility = visibility,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        var roles = await db.GetPackageRolesAsync(packageName);
        bool creatorIsOwner = roles.Exists(r => r.PrincipalId == creatorUsername && r.Role == "owner");
        if (!creatorIsOwner)
            await db.AssignPackageRoleAsync(packageName, "user", creatorUsername, "owner");
    }

    protected static async Task SeedPackageRoleAsync(
        WebApplicationFactory<Stash.Registry.Program> factory,
        string packageName,
        string principalType,
        string principalId,
        string role)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
        await db.AssignPackageRoleAsync(packageName, principalType, principalId, role);
    }

    protected static async Task<List<AuditEntry>> GetAuditEntriesAsync(
        WebApplicationFactory<Stash.Registry.Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
        var result = await db.GetAuditLogAsync(1, 1000, null, null);
        return result.Items;
    }

    protected static byte[] CreateTarball(string name, string version)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        using (var tar = new TarWriter(gz, TarEntryFormat.Pax, leaveOpen: true))
        {
            var manifest = new { name, version, description = "Test package", license = "MIT" };
            byte[] manifestBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest));
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "stash.json")
            {
                DataStream = new MemoryStream(manifestBytes)
            });
            byte[] stashBytes = Encoding.UTF8.GetBytes("fn main() { }");
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "main.stash")
            {
                DataStream = new MemoryStream(stashBytes)
            });
        }
        return ms.ToArray();
    }

    protected static ByteArrayContent TarballContent(byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return content;
    }

    protected static async Task<bool> PackageExistsAsync(WebApplicationFactory<Stash.Registry.Program> factory, string name)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
        return await db.PackageExistsAsync(name);
    }

    protected static async Task<List<PackageRoleEntry>> GetPackageRolesAsync(
        WebApplicationFactory<Stash.Registry.Program> factory, string name)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
        return await db.GetPackageRolesAsync(name);
    }
}
