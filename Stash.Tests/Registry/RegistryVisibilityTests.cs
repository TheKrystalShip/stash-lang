using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Stash.Registry.Configuration;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;
using Stash.Registry.Services;
using Stash.Registry.Storage;

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

    private static PackageRecord MakePackage(string name, string visibility = "public") => new()
    {
        Name = name,
        Latest = "1.0.0",
        Visibility = visibility,
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
        Assert.Equal("public", pkg.Visibility);
    }

    // ── SetPackageVisibilityAsync ────────────────────────────────────────────────

    [Fact]
    public async Task SetPackageVisibility_ChangesToPrivate()
    {
        await _db.CreatePackageAsync(MakePackage("set-vis-pkg", "public"));

        await _db.SetPackageVisibilityAsync("set-vis-pkg", "private");

        PackageRecord? pkg = await _db.GetPackageAsync("set-vis-pkg");
        Assert.NotNull(pkg);
        Assert.Equal("private", pkg.Visibility);
    }

    [Fact]
    public async Task SetPackageVisibility_ChangesToInternal()
    {
        await _db.CreatePackageAsync(MakePackage("int-vis-pkg", "public"));

        await _db.SetPackageVisibilityAsync("int-vis-pkg", "internal");

        PackageRecord? pkg = await _db.GetPackageAsync("int-vis-pkg");
        Assert.NotNull(pkg);
        Assert.Equal("internal", pkg.Visibility);
    }

    [Fact]
    public async Task SetPackageVisibility_ChangesBackToPublic()
    {
        await _db.CreatePackageAsync(MakePackage("back-to-pub", "private"));

        await _db.SetPackageVisibilityAsync("back-to-pub", "public");

        PackageRecord? pkg = await _db.GetPackageAsync("back-to-pub");
        Assert.NotNull(pkg);
        Assert.Equal("public", pkg.Visibility);
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
        Assert.Equal("search-pub", result.Packages[0].Name);
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
        Assert.Equal("priv-mine", result.Packages[0].Name);
    }

    [Fact]
    public async Task Search_AuthenticatedUser_SeesPublicAndOwnPrivate()
    {
        await _db.CreatePackageAsync(MakePackage("vis-pub-x", "public"));
        await _db.CreatePackageAsync(MakePackage("vis-priv-x", "private"));
        await _db.AssignPackageRoleAsync("vis-priv-x", "user", "alice", "owner");

        SearchResult result = await _db.SearchPackagesAsync("vis-", 1, 20, "alice");

        Assert.Equal(2, result.TotalCount);
        var names = result.Packages.ConvertAll(p => p.Name);
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
        Assert.Equal("internal-accessible", result.Packages[0].Name);
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
        Assert.Equal("public", fetched.Visibility);
    }
}
