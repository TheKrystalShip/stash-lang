using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Stash.Cli.PackageManager;
using Stash.Common;
using Stash.Registry.Configuration;
using Stash.Registry.Database;
using Stash.Registry.Database.Models;
using Stash.Registry.Services;
using Stash.Registry.Storage;
using Xunit;

namespace Stash.Tests.Registry;

/// <summary>
/// Tests for P6: scoped route migration.
/// Verifies that all package routes use the two-segment <c>{scope}/{name}</c> form
/// and that <see cref="PackageManifest.SplitScopedName"/> correctly splits scoped names.
/// </summary>
public sealed class RegistryRoutesTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly RegistryDbContext _context;
    private readonly StashRegistryDatabase _db;
    private readonly string _storageDir;
    private readonly FileSystemStorage _storage;
    private readonly RegistryConfig _config;
    private readonly PackageService _service;

    public RegistryRoutesTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var options = new DbContextOptionsBuilder<RegistryDbContext>()
            .UseSqlite(_conn)
            .Options;
        _context = new RegistryDbContext(options);
        _db = new StashRegistryDatabase(_context);
        _db.Initialize();

        _storageDir = Path.Combine(Path.GetTempPath(), $"stash-routes-{Guid.NewGuid():N}");
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

    // ── SplitScopedName ──────────────────────────────────────────────────────────

    [Fact]
    public void SplitScopedName_ValidInput_ReturnsBareScope()
    {
        var (scope, localName) = PackageManifest.SplitScopedName("@alice/widget");

        Assert.Equal("alice", scope);
        Assert.Equal("widget", localName);
    }

    [Fact]
    public void SplitScopedName_StashScope_ReturnsBareStash()
    {
        var (scope, localName) = PackageManifest.SplitScopedName("@stash/oci");

        Assert.Equal("stash", scope);
        Assert.Equal("oci", localName);
    }

    [Fact]
    public void SplitScopedName_HyphenatedSegments_SplitsCorrectly()
    {
        var (scope, localName) = PackageManifest.SplitScopedName("@my-org/my-package");

        Assert.Equal("my-org", scope);
        Assert.Equal("my-package", localName);
    }

    [Theory]
    [InlineData("widget")]          // flat name, no scope
    [InlineData("@")]               // just '@'
    [InlineData("@alice/")]         // empty local name
    [InlineData("@/widget")]        // empty scope
    [InlineData("@Alice/widget")]   // uppercase in scope
    public void SplitScopedName_InvalidInput_ThrowsArgumentException(string invalidName)
    {
        Assert.Throws<ArgumentException>(() => PackageManifest.SplitScopedName(invalidName));
    }

    // ── Publish + download round-trip (DB-level) ──────────────────────────────────

    private static byte[] CreateTestTarball(string name, string version)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        using (var tar = new TarWriter(gz, TarEntryFormat.Pax, leaveOpen: true))
        {
            var manifest = new { name, version, description = "Test", license = "MIT" };
            byte[] manifestBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest));
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "stash.json")
            {
                DataStream = new MemoryStream(manifestBytes)
            });
            byte[] codeBytes = Encoding.UTF8.GetBytes("// code");
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "index.stash")
            {
                DataStream = new MemoryStream(codeBytes)
            });
        }
        return ms.ToArray();
    }

    [Fact]
    public async Task ScopedPublish_AndRetrieve_RoundTripsByPackageName()
    {
        string packageName = "@alice/widget";
        string version = "1.0.0";
        byte[] tarball = CreateTestTarball(packageName, version);

        using var stream = new MemoryStream(tarball);
        VersionRecord vr = await _service.Publish(stream, "alice", null);

        Assert.Equal(packageName, vr.PackageName);
        Assert.Equal(version, vr.Version);

        // The DB stores the full @scope/name form
        bool exists = await _db.PackageExistsAsync(packageName);
        Assert.True(exists);

        PackageRecord? pkg = await _db.GetPackageAsync(packageName);
        Assert.NotNull(pkg);
        Assert.Equal(packageName, pkg.Name);

        // We can retrieve the tarball from storage
        Stream? retrieved = await _storage.RetrieveAsync(packageName, version);
        Assert.NotNull(retrieved);
    }

    // ── RegistryClient URL construction ─────────────────────────────────────────

    /// <summary>
    /// A capturing HTTP handler that records the last request URI.
    /// </summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }
        public string ResponseBody { get; set; } = "{}";
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            var response = new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    [Fact]
    public void RegistryClient_GetAvailableVersions_UsesScopedPath()
    {
        var handler = new CapturingHandler
        {
            ResponseBody = """{"versions":{}}"""
        };
        var http = new HttpClient(handler);
        var client = new RegistryClient("https://registry.example.com", http, token: "tok");

        client.GetAvailableVersions("@alice/widget");

        Assert.NotNull(handler.LastRequestUri);
        string path = handler.LastRequestUri!.AbsolutePath;
        // Must be /api/v1/packages/alice/widget (two segments, no @)
        Assert.EndsWith("/packages/alice/widget", path);
        // Must NOT contain %2F (old single-segment encoding)
        Assert.DoesNotContain("%2F", path);
        Assert.DoesNotContain("%40", path);
    }

    [Fact]
    public void RegistryClient_GetManifest_UsesScopedPath()
    {
        var handler = new CapturingHandler
        {
            ResponseBody = """{"version":"1.0.0","dependencies":{}}"""
        };
        var http = new HttpClient(handler);
        var client = new RegistryClient("https://registry.example.com", http, token: "tok");

        client.GetManifest("@alice/widget", SemVer.Parse("1.0.0")!);

        Assert.NotNull(handler.LastRequestUri);
        string path = handler.LastRequestUri!.AbsolutePath;
        Assert.Contains("/packages/alice/widget/", path);
        Assert.DoesNotContain("%2F", path);
    }

    [Fact]
    public void RegistryClient_GetResolvedUrl_UsesScopedPath()
    {
        var http = new HttpClient(new CapturingHandler());
        var client = new RegistryClient("https://registry.example.com", http);

        string url = client.GetResolvedUrl("@stash/oci", SemVer.Parse("1.0.0")!);

        // Expected: https://registry.example.com/api/v1/packages/stash/oci/1.0.0/download
        Assert.Contains("/packages/stash/oci/", url);
        Assert.EndsWith("/download", url);
        Assert.DoesNotContain("%2F", url);
    }

    [Fact]
    public void RegistryClient_Unpublish_UsesScopedPath()
    {
        var handler = new CapturingHandler();
        var http = new HttpClient(handler);
        var client = new RegistryClient("https://registry.example.com", http, token: "tok");

        client.Unpublish("@alice/widget", "1.0.0");

        Assert.NotNull(handler.LastRequestUri);
        string path = handler.LastRequestUri!.AbsolutePath;
        Assert.Contains("/packages/alice/widget/1.0.0", path);
        Assert.DoesNotContain("%2F", path);
    }

    [Fact]
    public void RegistryClient_DeprecatePackage_UsesScopedPath()
    {
        var handler = new CapturingHandler();
        var http = new HttpClient(handler);
        var client = new RegistryClient("https://registry.example.com", http, token: "tok");

        client.DeprecatePackage("@alice/widget", "deprecated", null);

        Assert.NotNull(handler.LastRequestUri);
        string path = handler.LastRequestUri!.AbsolutePath;
        Assert.Contains("/packages/alice/widget/deprecate", path);
        Assert.DoesNotContain("%2F", path);
    }

    [Fact]
    public void RegistryClient_UndeprecatePackage_UsesScopedPath()
    {
        var handler = new CapturingHandler();
        var http = new HttpClient(handler);
        var client = new RegistryClient("https://registry.example.com", http, token: "tok");

        client.UndeprecatePackage("@alice/widget");

        Assert.NotNull(handler.LastRequestUri);
        string path = handler.LastRequestUri!.AbsolutePath;
        Assert.Contains("/packages/alice/widget/deprecate", path);
        Assert.DoesNotContain("%2F", path);
    }
}
