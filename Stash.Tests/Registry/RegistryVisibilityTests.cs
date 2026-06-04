using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
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
using Stash.Registry.Configuration;
using Stash.Registry.Contracts;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;
using Stash.Registry.Services;
using Stash.Registry.Storage;
using Xunit;

namespace Stash.Tests.Registry;

/// <summary>
/// Tests for P4: package visibility enforcement, RequireReadScope policy registration,
/// publish-time private-flag removal, search visibility filtering, and
/// <see cref="StashRegistryDatabase.SetPackageVisibilityAsync"/>.
/// </summary>
public sealed class RegistryVisibilityTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly RegistryDbContext _context;
    private readonly StashRegistryDatabase _db;
    private readonly string _storageDir;
    private readonly FileSystemStorage _storage;
    private readonly RegistryConfig _config;
    private readonly PackageService _service;

    public RegistryVisibilityTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var options = new DbContextOptionsBuilder<RegistryDbContext>()
            .UseSqlite(_conn)
            .Options;
        _context = new RegistryDbContext(options);
        _db = new StashRegistryDatabase(_context);
        _db.Initialize();

        _storageDir = Path.Combine(Path.GetTempPath(), $"stash-vis-{Guid.NewGuid():N}");
        _config = new RegistryConfig();
        _storage = new FileSystemStorage(_storageDir);
        _service = new PackageService(_db, _storage, _config);
    }

    public void Dispose()
    {
        _context.Dispose();
        _conn.Dispose();
        try { Directory.Delete(_storageDir, true); } catch { }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static PackageRecord MakePackage(string name, string visibilityWire = "public") => new()
    {
        Name = name,
        Latest = "1.0.0",
        Visibility = visibilityWire.ToVisibility(),
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static byte[] CreateTestTarball(string name, string version, bool? isPrivate = null)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        using (var tar = new TarWriter(gz, TarEntryFormat.Pax, leaveOpen: true))
        {
            var manifest = new { name, version, description = "Test", license = "MIT", @private = isPrivate };
            byte[] manifestBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest));
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "stash.json")
            {
                DataStream = new MemoryStream(manifestBytes)
            });
            byte[] stashBytes = Encoding.UTF8.GetBytes("fn main() {}");
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "main.stash")
            {
                DataStream = new MemoryStream(stashBytes)
            });
        }
        return ms.ToArray();
    }

    // ── D7: manifest private flag is no longer a publish barrier ────────────────

    [Fact]
    public async Task Publish_ManifestPrivateTrue_Succeeds()
    {
        // D7: "private": true in the manifest must no longer block publishing.
        byte[] tarball = CreateTestTarball("@test/vis-private-ok", "1.0.0", isPrivate: true);
        using var stream = new MemoryStream(tarball);

        VersionRecord vr = await _service.Publish(stream, "alice", null);

        Assert.Equal("@test/vis-private-ok", vr.PackageName);
    }

    [Fact]
    public async Task Publish_ManifestPrivateTrue_DefaultsVisibilityToPublic()
    {
        // The server default for new packages is public; manifest private flag does not change it.
        byte[] tarball = CreateTestTarball("@test/vis-default-pub", "1.0.0", isPrivate: true);
        using var stream = new MemoryStream(tarball);
        await _service.Publish(stream, "alice", null);

        PackageRecord? pkg = await _db.GetPackageAsync("@test/vis-default-pub");
        Assert.NotNull(pkg);
        Assert.Equal(Visibilities.Public, pkg.Visibility);
    }

    // ── SetPackageVisibilityAsync ────────────────────────────────────────────────

    [Fact]
    public async Task SetPackageVisibility_ChangesToPrivate()
    {
        await _db.CreatePackageAsync(MakePackage("set-vis-pkg", "public"));

        await _db.SetPackageVisibilityAsync("set-vis-pkg", "private");

        PackageRecord? pkg = await _db.GetPackageAsync("set-vis-pkg");
        Assert.NotNull(pkg);
        Assert.Equal(Visibilities.Private, pkg.Visibility);
    }

    [Fact]
    public async Task SetPackageVisibility_ChangesToInternal()
    {
        await _db.CreatePackageAsync(MakePackage("int-vis-pkg", "public"));

        await _db.SetPackageVisibilityAsync("int-vis-pkg", "internal");

        PackageRecord? pkg = await _db.GetPackageAsync("int-vis-pkg");
        Assert.NotNull(pkg);
        Assert.Equal(Visibilities.Internal, pkg.Visibility);
    }

    [Fact]
    public async Task SetPackageVisibility_ChangesBackToPublic()
    {
        await _db.CreatePackageAsync(MakePackage("back-to-pub", "private"));

        await _db.SetPackageVisibilityAsync("back-to-pub", "public");

        PackageRecord? pkg = await _db.GetPackageAsync("back-to-pub");
        Assert.NotNull(pkg);
        Assert.Equal(Visibilities.Public, pkg.Visibility);
    }

    [Fact]
    public async Task SetPackageVisibility_UpdatesTimestamp()
    {
        DateTime before = DateTime.UtcNow.AddSeconds(-1);
        await _db.CreatePackageAsync(MakePackage("ts-vis-pkg", "public"));

        await _db.SetPackageVisibilityAsync("ts-vis-pkg", "private");

        PackageRecord? pkg = await _db.GetPackageAsync("ts-vis-pkg");
        Assert.NotNull(pkg);
        Assert.True(pkg.UpdatedAt > before);
    }

    // ── SearchPackagesAsync visibility filtering ─────────────────────────────────

    [Fact]
    public async Task Search_Unauthenticated_ReturnsOnlyPublicPackages()
    {
        await _db.CreatePackageAsync(MakePackage("search-pub", "public"));
        await _db.CreatePackageAsync(MakePackage("search-priv", "private"));
        await _db.CreatePackageAsync(MakePackage("search-int", "internal"));

        // callerUsername = null -> unauthenticated
        SearchResult result = await _db.SearchPackagesAsync("search-", 1, 20, null);

        Assert.Single(result.Packages);
        Assert.Equal("search-pub", result.Packages[0].Package.Name);
    }

    [Fact]
    public async Task Search_AuthenticatedWithReaderRole_SeesOwnPrivatePackages()
    {
        await _db.CreatePackageAsync(MakePackage("priv-mine", "private"));
        await _db.AssignPackageRoleAsync("priv-mine", "user", "alice", "reader");
        await _db.CreatePackageAsync(MakePackage("priv-other", "private"));
        await _db.AssignPackageRoleAsync("priv-other", "user", "bob", "owner");

        // alice is authenticated and has reader on priv-mine but not priv-other
        SearchResult result = await _db.SearchPackagesAsync("priv-", 1, 20, "alice");

        Assert.Single(result.Packages);
        Assert.Equal("priv-mine", result.Packages[0].Package.Name);
    }

    [Fact]
    public async Task Search_AuthenticatedUser_SeesPublicAndOwnPrivate()
    {
        await _db.CreatePackageAsync(MakePackage("vis-pub-x", "public"));
        await _db.CreatePackageAsync(MakePackage("vis-priv-x", "private"));
        await _db.AssignPackageRoleAsync("vis-priv-x", "user", "alice", "owner");

        SearchResult result = await _db.SearchPackagesAsync("vis-", 1, 20, "alice");

        Assert.Equal(2, result.TotalCount);
        var names = result.Packages.ConvertAll(p => p.Package.Name);
        Assert.Contains("vis-pub-x", names);
        Assert.Contains("vis-priv-x", names);
    }

    [Fact]
    public async Task Search_AuthenticatedUser_DoesNotSeePrivatePackagesWithoutRole()
    {
        await _db.CreatePackageAsync(MakePackage("no-role-priv", "private"));
        await _db.AssignPackageRoleAsync("no-role-priv", "user", "bob", "owner");

        // alice has no role on this package
        SearchResult result = await _db.SearchPackagesAsync("no-role-priv", 1, 20, "alice");

        Assert.Empty(result.Packages);
    }

    [Fact]
    public async Task Search_AuthenticatedUser_SeesInternalPackagesWithRole()
    {
        await _db.CreatePackageAsync(MakePackage("internal-accessible", "internal"));
        await _db.AssignPackageRoleAsync("internal-accessible", "user", "alice", "reader");

        SearchResult result = await _db.SearchPackagesAsync("internal-accessible", 1, 20, "alice");

        Assert.Single(result.Packages);
        Assert.Equal("internal-accessible", result.Packages[0].Package.Name);
    }

    // ── F04: internal visibility — search branch (b): user-owned scope, no package_roles row ──

    [Fact]
    public async Task Search_Internal_UserOwnedScope_ScopeOwnerSees_WithoutPackageRole()
    {
        // Arrange: alice has a user-owned scope (auto-provisioned) and an internal package in it.
        // alice has NO package_roles row — access should be granted via the user-owned-scope branch.
        await _db.CreateUserWithScopeAsync("alice", "hash");
        await _db.CreatePackageAsync(MakePackage("@alice/secret-lib", "internal"));

        // alice searches — should see the package even without a package_roles entry
        SearchResult result = await _db.SearchPackagesAsync("secret-lib", 1, 20, "alice");

        Assert.Single(result.Packages);
        Assert.Equal("@alice/secret-lib", result.Packages[0].Package.Name);
    }

    [Fact]
    public async Task Search_Internal_UserOwnedScope_OtherUserDoesNotSee_WithoutPackageRole()
    {
        // bob has no package_roles row and is not the scope owner → should not see alice's internal pkg
        await _db.CreateUserWithScopeAsync("alice", "hash");
        await _db.CreateUserWithScopeAsync("bob", "hash");
        await _db.CreatePackageAsync(MakePackage("@alice/hidden-lib", "internal"));

        SearchResult result = await _db.SearchPackagesAsync("hidden-lib", 1, 20, "bob");

        Assert.Empty(result.Packages);
    }

    // ── PackageRecord default visibility ─────────────────────────────────────────

    [Fact]
    public async Task CreatePackage_DefaultVisibility_IsPublic()
    {
        var pkg = new PackageRecord
        {
            Name = "default-vis-test",
            Latest = "1.0.0",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
            // Visibility not set — should default to "public"
        };
        await _db.CreatePackageAsync(pkg);

        PackageRecord? fetched = await _db.GetPackageAsync("default-vis-test");
        Assert.NotNull(fetched);
        Assert.Equal(Visibilities.Public, fetched.Visibility);
    }
}

/// <summary>
/// F04: HTTP-level integration tests for the explicit <c>internal</c> visibility branches
/// in <c>CanReadPackageAsync</c> (brief lines 197-201).
/// </summary>
public sealed class RegistryInternalVisibilityEndpointTests : IDisposable
{
    private SqliteConnection? _connection;

    public void Dispose() => _connection?.Dispose();

    private WebApplicationFactory<Stash.Registry.Program> CreateFactory(out SqliteConnection conn)
    {
        _connection?.Dispose();
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        conn = _connection;
        var capturedConn = conn;

        return new WebApplicationFactory<Stash.Registry.Program>()
            .WithWebHostBuilder(builder =>
            {
                // Pin the content root to the registry project relative to the solution
                // so WebApplicationFactory does not guess it from the current working
                // directory (which throws DirectoryNotFoundException in full-suite runs).
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
    }

    private static StringContent Json(object body) =>
        new StringContent(System.Text.Json.JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json");

    /// <summary>
    /// Registers a user, logs in, and returns a publish-ceiling Bearer token.
    /// Login now issues a read-ceiling token (P5 least-privilege default); tests that
    /// create orgs, add members, or publish need an explicit publish-ceiling upgrade.
    /// </summary>
    private static async Task<string> RegisterAndLoginAsync(HttpClient client, string username, string password = "Password123!")
    {
        await client.PostAsync("/api/v1/auth/register", Json(new { username, password }));
        var loginResp = await client.PostAsync("/api/v1/auth/login", Json(new { username, password }));
        loginResp.EnsureSuccessStatusCode();
        string loginJson = await loginResp.Content.ReadAsStringAsync();
        using var loginDoc = System.Text.Json.JsonDocument.Parse(loginJson);
        string loginToken = loginDoc.RootElement.GetProperty("accessToken").GetString()!;

        // Login issues a read-ceiling token; upgrade to publish-ceiling for write tests.
        var savedAuth = client.DefaultRequestHeaders.Authorization;
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginToken);
        var tokenResp = await client.PostAsync("/api/v1/auth/tokens",
            Json(new { ceiling = "publish", expiresIn = "1d" }));
        client.DefaultRequestHeaders.Authorization = savedAuth;

        tokenResp.EnsureSuccessStatusCode();
        string tokenJson = await tokenResp.Content.ReadAsStringAsync();
        using var tokenDoc = System.Text.Json.JsonDocument.Parse(tokenJson);
        return tokenDoc.RootElement.GetProperty("token").GetString()!;
    }

    private static PackageRecord MakePackage(string name, string visibilityWire) => new()
    {
        Name = name,
        Latest = "1.0.0",
        Visibility = visibilityWire.ToVisibility(),
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    // ── Branch (a): org-owned scope, caller is org member, no direct package_roles row ──

    [Fact]
    public async Task CanRead_Internal_OrgOwnedScope_OrgMemberNoDirectRole_Returns200()
    {
        await using var factory = CreateFactory(out _);
        using var client = factory.CreateClient();

        // alice creates the org (becomes owner), bob joins as member
        string aliceToken = await RegisterAndLoginAsync(client,"alice");
        string bobToken = await RegisterAndLoginAsync(client,"bob");

        // alice creates the org "acme" via the API
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", aliceToken);
        await client.PostAsync("/api/v1/orgs", Json(new { name = "acme", displayName = "Acme Corp" }));

        // alice adds bob as org member
        var orgMembersResp = await client.PostAsync("/api/v1/orgs/acme/members",
            Json(new { username = "bob", role = "member" }));
        Assert.True(orgMembersResp.IsSuccessStatusCode, $"Add member failed: {await orgMembersResp.Content.ReadAsStringAsync()}");

        // Seed: internal package in the @acme scope, NO package_roles entry for bob
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
        await db.CreatePackageAsync(MakePackage("@acme/internal-tool", "internal"));

        // bob reads the package — should be 200 (branch a: org member)
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bobToken);
        var response = await client.GetAsync("/api/v1/packages/acme/internal-tool");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    // ── Branch (b): user-owned scope, caller is the scope owner, no direct package_roles row ──

    [Fact]
    public async Task CanRead_Internal_UserOwnedScope_ScopeOwnerNoDirectRole_Returns200()
    {
        await using var factory = CreateFactory(out _);
        using var client = factory.CreateClient();

        string aliceToken = await RegisterAndLoginAsync(client,"alice");

        // Seed: internal package in alice's user-owned @alice scope, NO package_roles row
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
        await db.CreatePackageAsync(MakePackage("@alice/private-utils", "internal"));

        // alice reads — should be 200 (branch b: scope owner)
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", aliceToken);
        var response = await client.GetAsync("/api/v1/packages/alice/private-utils");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    // ── Unrelated caller: no membership, no role → 404 ──

    [Fact]
    public async Task CanRead_Internal_UnrelatedCaller_Returns404()
    {
        await using var factory = CreateFactory(out _);
        using var client = factory.CreateClient();

        await RegisterAndLoginAsync(client,"alice");
        string bobToken = await RegisterAndLoginAsync(client,"bob");

        // Seed: internal package in alice's scope; bob has no role, no membership
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IRegistryDatabase>();
        await db.CreatePackageAsync(MakePackage("@alice/secret-thing", "internal"));

        // bob reads — should be 404
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bobToken);
        var response = await client.GetAsync("/api/v1/packages/alice/secret-thing");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }
}
