using System.Formats.Tar;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Stash.Registry.Configuration;
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

    private static byte[] CreateTestTarball(string name, string version, string? readme = null, Dictionary<string, string>? dependencies = null)
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
                dependencies = dependencies
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
            var manifest = new { name = "no-stash-pkg", version = "1.0.0", description = "test", license = "MIT" };
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
        byte[] tarball = CreateTestTarball("test-pkg", "1.0.0");
        using var stream = new MemoryStream(tarball);

        VersionRecord result = await _service.Publish(stream, "alice", null);

        Assert.Equal("test-pkg", result.PackageName);
        Assert.Equal("1.0.0", result.Version);
        Assert.Equal("alice", result.PublishedBy);
    }

    [Fact]
    public async Task Publish_StoresInDatabase()
    {
        byte[] tarball = CreateTestTarball("db-pkg", "1.0.0");
        using var stream = new MemoryStream(tarball);
        await _service.Publish(stream, "alice", null);

        Assert.True(await _db.PackageExistsAsync("db-pkg"));
        Assert.True(await _db.VersionExistsAsync("db-pkg", "1.0.0"));
    }

    [Fact]
    public async Task Publish_StoresInStorage()
    {
        byte[] tarball = CreateTestTarball("store-pkg", "1.0.0");
        using var stream = new MemoryStream(tarball);
        await _service.Publish(stream, "alice", null);

        Assert.True(await _storage.ExistsAsync("store-pkg", "1.0.0"));
    }

    [Fact]
    public async Task Publish_ComputesIntegrity()
    {
        byte[] tarball = CreateTestTarball("int-pkg", "1.0.0");
        string expectedIntegrity = ComputeIntegrity(tarball);
        using var stream = new MemoryStream(tarball);

        VersionRecord result = await _service.Publish(stream, "alice", null);

        Assert.Equal(expectedIntegrity, result.Integrity);
    }

    [Fact]
    public async Task Publish_ExtractsReadme()
    {
        byte[] tarball = CreateTestTarball("readme-pkg", "1.0.0", readme: "# My Package");
        using var stream = new MemoryStream(tarball);
        await _service.Publish(stream, "alice", null);

        PackageRecord? pkg = await _db.GetPackageAsync("readme-pkg");
        Assert.NotNull(pkg);
        Assert.Equal("# My Package", pkg.Readme);
    }

    [Fact]
    public async Task Publish_DuplicateVersion_Throws()
    {
        byte[] tarball1 = CreateTestTarball("dup-pkg", "1.0.0");
        using (var s1 = new MemoryStream(tarball1))
        {
            await _service.Publish(s1, "alice", null);
        }

        byte[] tarball2 = CreateTestTarball("dup-pkg", "1.0.0");
        using var s2 = new MemoryStream(tarball2);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await _service.Publish(s2, "alice", null));
        Assert.Contains("immutable", ex.Message, StringComparison.OrdinalIgnoreCase);
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
        byte[] tarball = CreateTestTarball("big-pkg", "1.0.0");
        using var stream = new MemoryStream(tarball);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await _service.Publish(stream, "alice", null));
    }

    [Fact]
    public async Task Publish_IntegrityMismatch_Throws()
    {
        byte[] tarball = CreateTestTarball("mismatch-pkg", "1.0.0");
        using var stream = new MemoryStream(tarball);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _service.Publish(stream, "alice", "sha256-WRONG"));
        Assert.Contains("Integrity", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── Unpublish ───────────────────────────────────────────────────────

    [Fact]
    public async Task Unpublish_ValidRequest_Succeeds()
    {
        byte[] tarball = CreateTestTarball("unpub-pkg", "1.0.0");
        using (var s = new MemoryStream(tarball))
        {
            await _service.Publish(s, "alice", null);
        }

        bool result = await _service.UnpublishAsync("unpub-pkg", "1.0.0", "alice");

        Assert.True(result);
        Assert.False(await _db.VersionExistsAsync("unpub-pkg", "1.0.0"));
        Assert.False(await _storage.ExistsAsync("unpub-pkg", "1.0.0"));
    }

    [Fact]
    public async Task Unpublish_NotOwner_Throws()
    {
        byte[] tarball = CreateTestTarball("own-pkg", "1.0.0");
        using (var s = new MemoryStream(tarball))
        {
            await _service.Publish(s, "alice", null);
        }

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            async () => await _service.UnpublishAsync("own-pkg", "1.0.0", "bob"));
    }

    [Fact]
    public async Task Unpublish_ExpiredWindow_Throws()
    {
        // Set up the database directly with a version published 30 days ago
        // to deterministically exceed the default 72h unpublish window
        await _db.CreatePackageAsync(new PackageRecord
        {
            Name = "exp-pkg",
            Description = "test",
            Latest = "1.0.0",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await _db.AddOwnerAsync("exp-pkg", "alice");
        await _db.AddVersionAsync("exp-pkg", new VersionRecord
        {
            PackageName = "exp-pkg",
            Version = "1.0.0",
            Integrity = "sha256-test",
            PublishedAt = DateTime.UtcNow.AddDays(-30),
            PublishedBy = "alice"
        });

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _service.UnpublishAsync("exp-pkg", "1.0.0", "alice"));
    }

    [Fact]
    public async Task Unpublish_NonExistent_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _service.UnpublishAsync("ghost-pkg", "1.0.0", "alice"));
    }

    // ── Get operations ──────────────────────────────────────────────────

    [Fact]
    public async Task GetPackage_ReturnsPackage()
    {
        byte[] tarball = CreateTestTarball("get-pkg", "1.0.0");
        using (var s = new MemoryStream(tarball))
        {
            await _service.Publish(s, "alice", null);
        }

        PackageRecord? result = await _service.GetPackageAsync("get-pkg");

        Assert.NotNull(result);
        Assert.Equal("get-pkg", result.Name);
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
        byte[] tarball = CreateTestTarball("ver-pkg", "2.0.0");
        using (var s = new MemoryStream(tarball))
        {
            await _service.Publish(s, "alice", null);
        }

        VersionRecord? result = await _service.GetVersionAsync("ver-pkg", "2.0.0");

        Assert.NotNull(result);
        Assert.Equal("2.0.0", result.Version);
    }

    [Fact]
    public async Task DownloadPackage_ReturnsStream()
    {
        byte[] tarball = CreateTestTarball("dl-pkg", "1.0.0");
        using (var s = new MemoryStream(tarball))
        {
            await _service.Publish(s, "alice", null);
        }

        using Stream? result = await _service.DownloadPackageAsync("dl-pkg", "1.0.0");

        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }
}
