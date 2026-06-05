using System.Formats.Tar;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Stash.Registry.Configuration;
using Stash.Registry.Contracts;
using Stash.Registry.Database;
using Stash.Registry.Services;
using Stash.Registry.Storage;
using Stash.Registry.Database.Models;

namespace Stash.Tests.Registry;

public sealed class PackageServiceTests : IDisposable
{
    private readonly RegistryDbContext _context;
    private readonly string _storageDir;
    private readonly StashRegistryDatabase _db;
    private readonly FileSystemStorage _storage;
    private readonly RegistryConfig _config;
    private readonly PackageService _service;

    public PackageServiceTests()
    {
        _storageDir = Path.Combine(Path.GetTempPath(), $"stash-pkgsvc-storage-{Guid.NewGuid():N}");
        _config = new RegistryConfig();

        var options = new DbContextOptionsBuilder<RegistryDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _context = new RegistryDbContext(options);
        _context.Database.OpenConnection();
        _db = new StashRegistryDatabase(_context);
        _db.Initialize();

        _storage = new FileSystemStorage(_storageDir);
        _service = new PackageService(_db, _storage, _config);
    }

    public void Dispose()
    {
        _context.Dispose();
        try { Directory.Delete(_storageDir, true); } catch { }
    }

    private static byte[] CreateTestTarball(string name, string version, string? readme = null, Dictionary<string, string>? dependencies = null, bool? isPrivate = null)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        using (var tar = new TarWriter(gz, TarEntryFormat.Pax, leaveOpen: true))
        {
            var manifest = new
            {
                name = name,
                version = version,
                description = "Test package",
                license = "MIT",
                dependencies = dependencies,
                @private = isPrivate
            };
            string manifestJson = JsonSerializer.Serialize(manifest);
            byte[] manifestBytes = Encoding.UTF8.GetBytes(manifestJson);
            var manifestEntry = new PaxTarEntry(TarEntryType.RegularFile, "stash.json")
            {
                DataStream = new MemoryStream(manifestBytes)
            };
            tar.WriteEntry(manifestEntry);

            byte[] stashBytes = Encoding.UTF8.GetBytes("fn main() { io.println(\"hello\"); }");
            var stashEntry = new PaxTarEntry(TarEntryType.RegularFile, "main.stash")
            {
                DataStream = new MemoryStream(stashBytes)
            };
            tar.WriteEntry(stashEntry);

            if (readme != null)
            {
                byte[] readmeBytes = Encoding.UTF8.GetBytes(readme);
                var readmeEntry = new PaxTarEntry(TarEntryType.RegularFile, "README.md")
                {
                    DataStream = new MemoryStream(readmeBytes)
                };
                tar.WriteEntry(readmeEntry);
            }
        }
        return ms.ToArray();
    }

    private static byte[] CreateTarballWithoutManifest()
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        using (var tar = new TarWriter(gz, TarEntryFormat.Pax, leaveOpen: true))
        {
            byte[] stashBytes = Encoding.UTF8.GetBytes("fn main() {}");
            var entry = new PaxTarEntry(TarEntryType.RegularFile, "main.stash")
            {
                DataStream = new MemoryStream(stashBytes)
            };
            tar.WriteEntry(entry);
        }
        return ms.ToArray();
    }

    private static byte[] CreateTarballWithoutStashFile()
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        using (var tar = new TarWriter(gz, TarEntryFormat.Pax, leaveOpen: true))
        {
            var manifest = new { name = "@test/no-stash-pkg", version = "1.0.0", description = "test", license = "MIT" };
            byte[] manifestBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest));
            var entry = new PaxTarEntry(TarEntryType.RegularFile, "stash.json")
            {
                DataStream = new MemoryStream(manifestBytes)
            };
            tar.WriteEntry(entry);

            byte[] txtBytes = Encoding.UTF8.GetBytes("not a stash file");
            var txtEntry = new PaxTarEntry(TarEntryType.RegularFile, "readme.txt")
            {
                DataStream = new MemoryStream(txtBytes)
            };
            tar.WriteEntry(txtEntry);
        }
        return ms.ToArray();
    }

    private static string ComputeIntegrity(byte[] data)
    {
        byte[] hash = System.Security.Cryptography.SHA256.HashData(data);
        return "sha256-" + Convert.ToBase64String(hash);
    }

    // ── Publish ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Publish_ValidTarball_Succeeds()
    {
        byte[] tarball = CreateTestTarball("@test/test-pkg", "1.0.0");
        using var stream = new MemoryStream(tarball);

        VersionRecord result = await _service.Publish(stream, "alice", null);

        Assert.Equal("@test/test-pkg", result.PackageName);
        Assert.Equal("1.0.0", result.Version);
        Assert.Equal("alice", result.PublishedBy);
    }

    [Fact]
    public async Task Publish_StoresInDatabase()
    {
        byte[] tarball = CreateTestTarball("@test/db-pkg", "1.0.0");
        using var stream = new MemoryStream(tarball);
        await _service.Publish(stream, "alice", null);

        Assert.True(await _db.PackageExistsAsync("@test/db-pkg"));
        Assert.True(await _db.VersionExistsAsync("@test/db-pkg", "1.0.0"));
    }

    [Fact]
    public async Task Publish_StoresInStorage()
    {
        byte[] tarball = CreateTestTarball("@test/store-pkg", "1.0.0");
        using var stream = new MemoryStream(tarball);
        await _service.Publish(stream, "alice", null);

        Assert.True(await _storage.ExistsAsync("@test/store-pkg", "1.0.0"));
    }

    [Fact]
    public async Task Publish_ComputesIntegrity()
    {
        byte[] tarball = CreateTestTarball("@test/int-pkg", "1.0.0");
        string expectedIntegrity = ComputeIntegrity(tarball);
        using var stream = new MemoryStream(tarball);

        VersionRecord result = await _service.Publish(stream, "alice", null);

        Assert.Equal(expectedIntegrity, result.Integrity);
    }

    [Fact]
    public async Task Publish_ExtractsReadme()
    {
        byte[] tarball = CreateTestTarball("@test/readme-pkg", "1.0.0", readme: "# My Package");
        using var stream = new MemoryStream(tarball);
        await _service.Publish(stream, "alice", null);

        PackageRecord? pkg = await _db.GetPackageAsync("@test/readme-pkg");
        Assert.NotNull(pkg);
        Assert.Equal("# My Package", pkg.Readme);
    }

    [Fact]
    public async Task Publish_DuplicateVersion_ThrowsVersionConflict()
    {
        byte[] tarball1 = CreateTestTarball("@test/dup-pkg", "1.0.0");
        using (var s1 = new MemoryStream(tarball1))
        {
            await _service.Publish(s1, "alice", null);
        }

        byte[] tarball2 = CreateTestTarball("@test/dup-pkg", "1.0.0");
        using var s2 = new MemoryStream(tarball2);

        var ex = await Assert.ThrowsAsync<VersionConflictException>(async () => await _service.Publish(s2, "alice", null));
        Assert.Equal("@test/dup-pkg", ex.PackageName);
        Assert.Equal("1.0.0", ex.Version);
        // Verify the 409 controller path has a clear message (not a generic 400 string).
        Assert.Contains("1.0.0", ex.Message);
        Assert.Contains("@test/dup-pkg", ex.Message);
    }

    [Fact]
    public async Task Publish_NoManifest_Throws()
    {
        byte[] tarball = CreateTarballWithoutManifest();
        using var stream = new MemoryStream(tarball);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await _service.Publish(stream, "alice", null));
        Assert.Contains("stash.json", ex.Message);
    }

    [Fact]
    public async Task Publish_NoStashFile_Throws()
    {
        byte[] tarball = CreateTarballWithoutStashFile();
        using var stream = new MemoryStream(tarball);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await _service.Publish(stream, "alice", null));
        Assert.Contains(".stash", ex.Message);
    }

    [Fact]
    public async Task Publish_TooLarge_Throws()
    {
        _config.Security.MaxPackageSize = "1"; // 1 byte — any tarball exceeds this
        byte[] tarball = CreateTestTarball("@test/big-pkg", "1.0.0");
        using var stream = new MemoryStream(tarball);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await _service.Publish(stream, "alice", null));
    }

    [Fact]
    public async Task Publish_IntegrityMismatch_Throws()
    {
        byte[] tarball = CreateTestTarball("@test/mismatch-pkg", "1.0.0");
        using var stream = new MemoryStream(tarball);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _service.Publish(stream, "alice", "sha256-WRONG"));
        Assert.Contains("Integrity", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── Unpublish ───────────────────────────────────────────────────────

    [Fact]
    public async Task Unpublish_ValidRequest_Succeeds()
    {
        byte[] tarball = CreateTestTarball("@test/unpub-pkg", "1.0.0");
        using (var s = new MemoryStream(tarball))
        {
            await _service.Publish(s, "alice", null);
        }

        bool result = await _service.UnpublishAsync("@test/unpub-pkg", "1.0.0", "alice");

        Assert.True(result);
        Assert.False(await _db.VersionExistsAsync("@test/unpub-pkg", "1.0.0"));
        Assert.False(await _storage.ExistsAsync("@test/unpub-pkg", "1.0.0"));
    }

    [Fact]
    public async Task Unpublish_AnyUser_Succeeds_ServiceLayerNoLongerChecksOwner()
    {
        // P3: The owner check moved to the PDP / controller layer.
        // PackageService.UnpublishAsync no longer throws UnauthorizedAccessException.
        byte[] tarball = CreateTestTarball("@test/own-pkg", "1.0.0");
        using (var s = new MemoryStream(tarball))
        {
            await _service.Publish(s, "alice", null);
        }

        // bob can call UnpublishAsync directly at the service layer (controller handles authz)
        bool result = await _service.UnpublishAsync("@test/own-pkg", "1.0.0", "bob");
        Assert.True(result);
    }

    [Fact]
    public async Task Unpublish_ExpiredWindow_Throws()
    {
        // Set up the database directly with a version published 30 days ago
        // to deterministically exceed the default 72h unpublish window
        await _db.CreatePackageAsync(new PackageRecord
        {
            Name = "@test/exp-pkg",
            Description = "test",
            Latest = "1.0.0",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await _db.AssignPackageRoleAsync("@test/exp-pkg", "user", "alice", "owner");
        await _db.AddVersionAsync("@test/exp-pkg", new VersionRecord
        {
            PackageName = "@test/exp-pkg",
            Version = "1.0.0",
            Integrity = "sha256-test",
            PublishedAt = DateTime.UtcNow.AddDays(-30),
            PublishedBy = "alice"
        });

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _service.UnpublishAsync("@test/exp-pkg", "1.0.0", "alice"));
    }

    [Fact]
    public async Task Unpublish_NonExistent_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _service.UnpublishAsync("@test/ghost-pkg", "1.0.0", "alice"));
    }

    // ── Get operations ──────────────────────────────────────────────────

    [Fact]
    public async Task GetPackage_ReturnsPackage()
    {
        byte[] tarball = CreateTestTarball("@test/get-pkg", "1.0.0");
        using (var s = new MemoryStream(tarball))
        {
            await _service.Publish(s, "alice", null);
        }

        PackageRecord? result = await _service.GetPackageAsync("@test/get-pkg");

        Assert.NotNull(result);
        Assert.Equal("@test/get-pkg", result.Name);
    }

    [Fact]
    public async Task GetPackage_NonExistent_ReturnsNull()
    {
        PackageRecord? result = await _service.GetPackageAsync("nope");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetVersion_ReturnsVersion()
    {
        byte[] tarball = CreateTestTarball("@test/ver-pkg", "2.0.0");
        using (var s = new MemoryStream(tarball))
        {
            await _service.Publish(s, "alice", null);
        }

        VersionRecord? result = await _service.GetVersionAsync("@test/ver-pkg", "2.0.0");

        Assert.NotNull(result);
        Assert.Equal("2.0.0", result.Version);
    }

    [Fact]
    public async Task DownloadPackage_ReturnsStream()
    {
        byte[] tarball = CreateTestTarball("@test/dl-pkg", "1.0.0");
        using (var s = new MemoryStream(tarball))
        {
            await _service.Publish(s, "alice", null);
        }

        using Stream? result = await _service.DownloadPackageAsync("@test/dl-pkg", "1.0.0");

        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    // ── Latest version semver correctness ────────────────────────────────────────

    [Fact]
    public async Task Publish_LowerSemverAfterHigher_LatestRemainsHigher()
    {
        using (var s = new MemoryStream(CreateTestTarball("@test/semver-a", "2.0.0")))
            await _service.Publish(s, "alice", null);

        using (var s = new MemoryStream(CreateTestTarball("@test/semver-a", "1.5.0")))
            await _service.Publish(s, "alice", null);

        PackageRecord? pkg = await _db.GetPackageAsync("@test/semver-a");
        Assert.NotNull(pkg);
        Assert.Equal("2.0.0", pkg.Latest);
    }

    [Fact]
    public async Task Publish_HigherSemverAfterLower_LatestUpdates()
    {
        using (var s = new MemoryStream(CreateTestTarball("@test/semver-b", "2.0.0")))
            await _service.Publish(s, "alice", null);

        using (var s = new MemoryStream(CreateTestTarball("@test/semver-b", "2.1.0")))
            await _service.Publish(s, "alice", null);

        PackageRecord? pkg = await _db.GetPackageAsync("@test/semver-b");
        Assert.NotNull(pkg);
        Assert.Equal("2.1.0", pkg.Latest);
    }

    [Fact]
    public async Task Publish_PrereleaseOnly_LatestIsPrerelease()
    {
        using (var s = new MemoryStream(CreateTestTarball("@test/semver-c", "1.0.0-rc.1")))
            await _service.Publish(s, "alice", null);

        PackageRecord? pkg = await _db.GetPackageAsync("@test/semver-c");
        Assert.NotNull(pkg);
        Assert.Equal("1.0.0-rc.1", pkg.Latest);
    }

    [Fact]
    public async Task Publish_StableAfterPrerelease_LatestPromotesToStable()
    {
        using (var s = new MemoryStream(CreateTestTarball("@test/semver-d", "1.0.0-rc.1")))
            await _service.Publish(s, "alice", null);

        using (var s = new MemoryStream(CreateTestTarball("@test/semver-d", "1.0.0")))
            await _service.Publish(s, "alice", null);

        PackageRecord? pkg = await _db.GetPackageAsync("@test/semver-d");
        Assert.NotNull(pkg);
        Assert.Equal("1.0.0", pkg.Latest);
    }

    [Fact]
    public async Task Publish_PrereleaseAfterStable_LatestStaysStable()
    {
        using (var s = new MemoryStream(CreateTestTarball("@test/semver-e", "1.0.0")))
            await _service.Publish(s, "alice", null);

        using (var s = new MemoryStream(CreateTestTarball("@test/semver-e", "1.1.0-rc.1")))
            await _service.Publish(s, "alice", null);

        PackageRecord? pkg = await _db.GetPackageAsync("@test/semver-e");
        Assert.NotNull(pkg);
        Assert.Equal("1.0.0", pkg.Latest);
    }

    [Fact]
    public async Task Unpublish_LatestVersion_RecomputesToNextHighestStable()
    {
        foreach (var ver in new[] { "1.0.0", "2.0.0", "3.0.0" })
        {
            using var s = new MemoryStream(CreateTestTarball("@test/semver-f", ver));
            await _service.Publish(s, "alice", null);
        }

        await _service.UnpublishAsync("@test/semver-f", "3.0.0", "alice");

        PackageRecord? pkg = await _db.GetPackageAsync("@test/semver-f");
        Assert.NotNull(pkg);
        Assert.Equal("2.0.0", pkg.Latest);
    }

    [Fact]
    public async Task Unpublish_AllStable_PromotesPrereleaseToLatest()
    {
        using (var s = new MemoryStream(CreateTestTarball("@test/semver-g", "1.0.0")))
            await _service.Publish(s, "alice", null);

        using (var s = new MemoryStream(CreateTestTarball("@test/semver-g", "2.0.0-rc.1")))
            await _service.Publish(s, "alice", null);

        await _service.UnpublishAsync("@test/semver-g", "1.0.0", "alice");

        PackageRecord? pkg = await _db.GetPackageAsync("@test/semver-g");
        Assert.NotNull(pkg);
        Assert.Equal("2.0.0-rc.1", pkg.Latest);
    }

    // ── X-Integrity stored on publish ─────────────────────────────────────────
    //
    // Controller-level testing of the X-Integrity response header on DownloadVersion
    // requires an integration test with WebApplicationFactory<Program>, which is not
    // set up in this project. The service-level test below verifies that Publish
    // stores a non-empty Integrity field on the VersionRecord, which is the value
    // that PackagesController.DownloadVersion reads and emits as the X-Integrity header.

    [Fact]
    public async Task Publish_StoresIntegrityOnVersionRecord()
    {
        byte[] tarball = CreateTestTarball("@test/integrity-svc-pkg", "1.0.0");
        using (var s = new MemoryStream(tarball))
            await _service.Publish(s, "alice", null);

        VersionRecord? vr = await _db.GetPackageVersionAsync("@test/integrity-svc-pkg", "1.0.0");
        Assert.NotNull(vr);
        Assert.False(string.IsNullOrEmpty(vr.Integrity),
            "Publish must store a non-empty Integrity field so DownloadVersion can emit X-Integrity.");
        Assert.StartsWith("sha256-", vr.Integrity);
    }

    // ── Private manifest flag is ignored (D7 — visibility is server-side state) ─

    [Fact]
    public async Task Publish_PrivateTrueInManifest_Succeeds()
    {
        // D7: the manifest "private": true flag no longer blocks publishing.
        // Visibility is a server-side state; the manifest field is ignored.
        byte[] tarball = CreateTestTarball("@test/private-pkg", "1.0.0", isPrivate: true);
        using var stream = new MemoryStream(tarball);

        VersionRecord vr = await _service.Publish(stream, "alice", null);

        Assert.Equal("@test/private-pkg", vr.PackageName);
        Assert.Equal("1.0.0", vr.Version);
        // Package defaults to public visibility regardless of the manifest flag.
        PackageRecord? pkg = await _db.GetPackageAsync("@test/private-pkg");
        Assert.NotNull(pkg);
        Assert.Equal(Visibilities.Public, pkg.Visibility);
    }

    [Fact]
    public async Task Publish_PrivateFalse_Succeeds()
    {
        byte[] tarball = CreateTestTarball("@test/public-pkg", "1.0.0", isPrivate: false);
        using var stream = new MemoryStream(tarball);

        VersionRecord vr = await _service.Publish(stream, "alice", null);

        Assert.Equal("@test/public-pkg", vr.PackageName);
        Assert.Equal("1.0.0", vr.Version);
    }

    // ── M2: storage_bytes written at publish time (D10) ──────────────────

    /// <summary>
    /// <c>PackageService.PublishAsync</c> writes <c>StorageBytes</c> on the
    /// <see cref="VersionRecord"/> from the in-memory tarball size (D10 — persisted
    /// column, never a runtime filesystem stat).
    /// </summary>
    [Fact]
    public async Task Publish_SetsStorageBytes_FromTarballSize()
    {
        byte[] tarball = CreateTestTarball("@test/storybytes-pkg", "1.0.0");
        using var stream = new MemoryStream(tarball);

        VersionRecord vr = await _service.Publish(stream, "alice", null);

        Assert.True(vr.StorageBytes > 0,
            "StorageBytes must be populated with the tarball length at publish time.");
        Assert.Equal(tarball.LongLength, vr.StorageBytes);
    }

    /// <summary>
    /// After publishing a package, the persisted <see cref="VersionRecord.StorageBytes"/>
    /// equals the original tarball length (round-trip through the database).
    /// </summary>
    [Fact]
    public async Task Publish_StorageBytes_PersistedInDatabase()
    {
        byte[] tarball = CreateTestTarball("@test/storybytes-persist-pkg", "1.0.0");
        using var stream = new MemoryStream(tarball);
        await _service.Publish(stream, "alice", null);

        VersionRecord? persisted = await _db.GetPackageVersionAsync("@test/storybytes-persist-pkg", "1.0.0");

        Assert.NotNull(persisted);
        Assert.Equal(tarball.LongLength, persisted.StorageBytes);
    }
}
