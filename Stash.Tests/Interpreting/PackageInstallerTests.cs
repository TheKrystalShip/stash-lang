using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using Stash.Common;
using Stash.Cli.PackageManager;

namespace Stash.Tests.Interpreting;

// ── TarballTests ──────────────────────────────────────────────────────────────

public class TarballTests : IDisposable
{
    private readonly string _tempDir;

    public TarballTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void Tarball_PackAndExtract_RoundTrips()
    {
        string srcDir = Path.Combine(_tempDir, "src");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "main.stash"), "print(\"hello\")");
        File.WriteAllText(Path.Combine(srcDir, "README.md"), "# Test");

        string tarball = Path.Combine(_tempDir, "out.tar.gz");
        var included = Tarball.Pack(srcDir, tarball);

        string extractDir = Path.Combine(_tempDir, "extracted");
        Tarball.Extract(tarball, extractDir);

        Assert.Contains("main.stash", included);
        Assert.Contains("README.md", included);
        Assert.True(File.Exists(Path.Combine(extractDir, "main.stash")));
        Assert.Equal("print(\"hello\")", File.ReadAllText(Path.Combine(extractDir, "main.stash")));
        Assert.True(File.Exists(Path.Combine(extractDir, "README.md")));
    }

    [Fact]
    public void Tarball_Pack_RespectsStashIgnore()
    {
        string srcDir = Path.Combine(_tempDir, "pkg");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, ".stashignore"), "*.log");
        File.WriteAllText(Path.Combine(srcDir, "main.stash"), "// code");
        File.WriteAllText(Path.Combine(srcDir, "debug.log"), "log data");

        string tarball = Path.Combine(_tempDir, "pkg.tar.gz");
        var included = Tarball.Pack(srcDir, tarball);

        Assert.Contains("main.stash", included);
        Assert.DoesNotContain("debug.log", included);
    }

    [Fact]
    public void Tarball_Pack_DefaultIgnoresGitDir()
    {
        string srcDir = Path.Combine(_tempDir, "repo");
        Directory.CreateDirectory(srcDir);
        Directory.CreateDirectory(Path.Combine(srcDir, ".git"));
        File.WriteAllText(Path.Combine(srcDir, ".git", "HEAD"), "ref: refs/heads/main");
        File.WriteAllText(Path.Combine(srcDir, "main.stash"), "// code");

        string tarball = Path.Combine(_tempDir, "repo.tar.gz");
        var included = Tarball.Pack(srcDir, tarball);

        Assert.Contains("main.stash", included);
        Assert.DoesNotContain(".git/HEAD", included);
    }

    [Fact]
    public void Tarball_Extract_PathTraversal_Throws()
    {
        string maliciousArchive = Path.Combine(_tempDir, "malicious.tar.gz");
        string targetDir = Path.Combine(_tempDir, "target");

        using (var fileStream = new FileStream(maliciousArchive, FileMode.Create))
        using (var gzipStream = new GZipStream(fileStream, CompressionLevel.Optimal))
        using (var tarWriter = new TarWriter(gzipStream, TarEntryFormat.Pax, leaveOpen: false))
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, "../escape.txt");
            using var ms = new MemoryStream("evil"u8.ToArray());
            entry.DataStream = ms;
            tarWriter.WriteEntry(entry);
        }

        Assert.Throws<InvalidOperationException>(() => Tarball.Extract(maliciousArchive, targetDir));
    }

    [Fact]
    public void Tarball_Pack_EmptyDirectory_ReturnsEmpty()
    {
        string emptyDir = Path.Combine(_tempDir, "empty");
        Directory.CreateDirectory(emptyDir);
        string tarball = Path.Combine(_tempDir, "empty.tar.gz");

        var included = Tarball.Pack(emptyDir, tarball);

        Assert.Empty(included);
    }
}

// ── PackageCacheTests ─────────────────────────────────────────────────────────

public class PackageCacheTests : IDisposable
{
    private readonly string _pkgName;
    private readonly string _tempDir;

    public PackageCacheTests()
    {
        _pkgName = $"@test/pkg-{Guid.NewGuid():N}";
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        PackageCache.ClearPackage(_pkgName);
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void PackageCache_GetCachePath_ReturnsExpectedPath()
    {
        string path = PackageCache.GetCachePath("@mylib/lib", "1.2.3");

        Assert.Contains("mylib-lib", path);
        Assert.EndsWith(Path.Combine("mylib-lib", "1.2.3.tar.gz"), path);
    }

    [Fact]
    public void PackageCache_StoreAndRetrieve_Works()
    {
        string srcFile = Path.Combine(_tempDir, "dummy.tar.gz");
        File.WriteAllBytes(srcFile, [0x1f, 0x8b]); // gzip magic bytes

        PackageCache.Store(_pkgName, "1.0.0", srcFile);

        Assert.True(PackageCache.IsCached(_pkgName, "1.0.0"));
        string? cachedPath = PackageCache.GetCachedTarball(_pkgName, "1.0.0");
        Assert.NotNull(cachedPath);
        Assert.True(File.Exists(cachedPath));
    }

    [Fact]
    public void PackageCache_ClearPackage_RemovesPackageDir()
    {
        string srcFile = Path.Combine(_tempDir, "dummy.tar.gz");
        File.WriteAllBytes(srcFile, [0x1f, 0x8b]);
        PackageCache.Store(_pkgName, "2.0.0", srcFile);
        Assert.True(PackageCache.IsCached(_pkgName, "2.0.0"));

        PackageCache.ClearPackage(_pkgName);

        Assert.False(PackageCache.IsCached(_pkgName, "2.0.0"));
    }

    [Fact]
    public void PackageCache_VerifyCache_CorrectIntegrity_ReturnsTrue()
    {
        string srcDir = Path.Combine(_tempDir, "pkg");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "main.stash"), "// code");

        string tarball = Path.Combine(_tempDir, "pkg.tar.gz");
        Tarball.Pack(srcDir, tarball);
        PackageCache.Store(_pkgName, "1.0.0", tarball);

        string integrity = LockFile.ComputeIntegrity(PackageCache.GetCachePath(_pkgName, "1.0.0"));

        Assert.True(PackageCache.VerifyCache(_pkgName, "1.0.0", integrity));
        Assert.False(PackageCache.VerifyCache(_pkgName, "1.0.0", "sha256-invalid"));
    }
}

// ── GitSourceTests ────────────────────────────────────────────────────────────

public class GitSourceTests
{
    [Fact]
    public void GitSource_IsGitSource_ValidPrefix_ReturnsTrue()
    {
        Assert.True(GitSource.IsGitSource("git:https://github.com/user/repo.git"));
    }

    [Fact]
    public void GitSource_IsGitSource_NoPrefix_ReturnsFalse()
    {
        Assert.False(GitSource.IsGitSource("^1.0.0"));
    }

    [Fact]
    public void GitSource_IsGitSource_UppercasePrefix_ReturnsFalse()
    {
        // IsGitSource uses Ordinal (case-sensitive) comparison
        Assert.False(GitSource.IsGitSource("Git:https://github.com/user/repo.git"));
    }

    [Fact]
    public void GitSource_ParseGitSource_WithRef_ReturnsUrlAndRef()
    {
        var (url, gitRef) = GitSource.ParseGitSource("git:https://github.com/user/repo.git#v1.0.0");

        Assert.Equal("https://github.com/user/repo.git", url);
        Assert.Equal("v1.0.0", gitRef);
    }

    [Fact]
    public void GitSource_ParseGitSource_WithoutRef_ReturnsUrlAndNull()
    {
        var (url, gitRef) = GitSource.ParseGitSource("git:https://github.com/user/repo.git");

        Assert.Equal("https://github.com/user/repo.git", url);
        Assert.Null(gitRef);
    }

    [Fact]
    public void GitSource_ParseGitSource_WithCommitHash_ReturnsUrlAndRef()
    {
        var (url, gitRef) = GitSource.ParseGitSource("git:https://github.com/user/repo.git#abc1234");

        Assert.Equal("https://github.com/user/repo.git", url);
        Assert.Equal("abc1234", gitRef);
    }
}

// ── PackageInstallerTests ─────────────────────────────────────────────────────

public class PackageInstallerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _pkgName;

    private class MockPackageSource : IPackageSource
    {
        private readonly Dictionary<string, List<(SemVer version, PackageManifest manifest)>> _packages = new();
        private readonly Dictionary<string, (bool Deprecated, string? Message)> _deprecations = new(StringComparer.Ordinal);

        public void AddPackage(string name, string version, Dictionary<string, string>? deps = null,
            bool deprecated = false, string? deprecationMessage = null)
        {
            if (!_packages.TryGetValue(name, out var list))
            {
                list = new List<(SemVer, PackageManifest)>();
                _packages[name] = list;
            }
            list.Add((SemVer.Parse(version), new PackageManifest { Name = name, Version = version, Dependencies = deps }));
            if (deprecated)
            {
                _deprecations[$"{name}@{version}"] = (true, deprecationMessage);
            }
        }

        public List<SemVer> GetAvailableVersions(string name)
            => _packages.TryGetValue(name, out var list)
                ? list.Select(p => p.version).ToList()
                : new List<SemVer>();

        public PackageManifest? GetManifest(string name, SemVer version)
            => _packages.TryGetValue(name, out var list)
                ? list.FirstOrDefault(p => p.version.Equals(version)).manifest
                : null;

        public string GetResolvedUrl(string name, SemVer version)
            => $"https://registry.example.com/{name}/{version}.tar.gz";

        public string? GetIntegrity(string name, SemVer version)
            => null;

        public (bool Deprecated, string? Message) GetDeprecation(string name, SemVer version)
            => _deprecations.TryGetValue($"{name}@{version}", out var info) ? info : (false, null);
    }

    public PackageInstallerTests()
    {
        _pkgName = $"@test/pkg-{Guid.NewGuid():N}";
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }

        PackageCache.ClearPackage(_pkgName);
    }

    private static void WriteManifest(string projectDir, Dictionary<string, string>? deps = null, string name = "test-project")
    {
        string depsSection = deps == null || deps.Count == 0
            ? ""
            : ",\n  \"dependencies\": {" + string.Join(", ", deps.Select(kv => $"\"{kv.Key}\": \"{kv.Value}\"")) + "}";
        File.WriteAllText(
            Path.Combine(projectDir, "stash.json"),
            $"{{\n  \"name\": \"{name}\",\n  \"version\": \"1.0.0\"{depsSection}\n}}\n");
    }

    private string CreateAndCachePackage(string pkgName, string version)
    {
        string safeName = pkgName.TrimStart('@').Replace('/', '-');
        string pkgSrcDir = Path.Combine(_tempDir, $"pkg-src-{safeName}-{version}");
        Directory.CreateDirectory(pkgSrcDir);
        File.WriteAllText(Path.Combine(pkgSrcDir, "main.stash"), $"// {pkgName} v{version}");

        string tarball = Path.Combine(_tempDir, $"{safeName}-{version}.tar.gz");
        Tarball.Pack(pkgSrcDir, tarball);
        PackageCache.Store(pkgName, version, tarball);
        return PackageCache.GetCachePath(pkgName, version);
    }

    [Fact]
    public void PackageInstaller_Install_CreatesLockFileAndStashesDir()
    {
        string projectDir = Path.Combine(_tempDir, "project");
        Directory.CreateDirectory(projectDir);
        WriteManifest(projectDir, new Dictionary<string, string> { [_pkgName] = "^1.0.0" });

        CreateAndCachePackage(_pkgName, "1.0.0");

        var source = new MockPackageSource();
        source.AddPackage(_pkgName, "1.0.0");

        PackageInstaller.Install(projectDir, source);

        Assert.True(File.Exists(Path.Combine(projectDir, "stash-lock.json")));
        Assert.True(Directory.Exists(Path.Combine(projectDir, "stashes")));
        string versionMarker = Path.Combine(projectDir, "stashes", _pkgName, ".stash-version");
        Assert.True(File.Exists(versionMarker));
        Assert.Equal("1.0.0", File.ReadAllText(versionMarker).Trim());
    }

    [Fact]
    public void PackageInstaller_InstallFromLockFile_SkipsAlreadyInstalled()
    {
        string projectDir = Path.Combine(_tempDir, "project2");
        Directory.CreateDirectory(projectDir);

        string stashesDir = Path.Combine(projectDir, "stashes", _pkgName);
        Directory.CreateDirectory(stashesDir);
        File.WriteAllText(Path.Combine(stashesDir, ".stash-version"), "1.0.0");
        File.WriteAllText(Path.Combine(stashesDir, "sentinel.txt"), "do-not-delete");

        var lockFile = new LockFile
        {
            LockVersion = 1,
            Resolved = new Dictionary<string, LockFileEntry>
            {
                [_pkgName] = new LockFileEntry { Version = "1.0.0", Resolved = "https://example.com/pkg.tar.gz" }
            }
        };

        PackageInstaller.InstallFromLockFile(projectDir, lockFile);

        // Package was already installed at the correct version — sentinel should survive
        Assert.True(File.Exists(Path.Combine(stashesDir, "sentinel.txt")));
    }

    [Fact]
    public void PackageInstaller_UninstallPackage_RemovesFromManifestAndDisk()
    {
        string projectDir = Path.Combine(_tempDir, "project3");
        Directory.CreateDirectory(projectDir);
        WriteManifest(projectDir, new Dictionary<string, string> { [_pkgName] = "^1.0.0" });

        CreateAndCachePackage(_pkgName, "1.0.0");

        var source = new MockPackageSource();
        source.AddPackage(_pkgName, "1.0.0");

        PackageInstaller.Install(projectDir, source);
        Assert.True(Directory.Exists(Path.Combine(projectDir, "stashes", _pkgName)));

        PackageInstaller.UninstallPackage(projectDir, _pkgName);

        Assert.False(Directory.Exists(Path.Combine(projectDir, "stashes", _pkgName)));
        var manifest = PackageManifest.Load(projectDir)!;
        Assert.False(manifest.Dependencies?.ContainsKey(_pkgName) ?? false);
    }

    [Fact]
    public void PackageInstaller_Install_FreshLockFile_SkipsResolution()
    {
        string projectDir = Path.Combine(_tempDir, "project4");
        Directory.CreateDirectory(projectDir);
        WriteManifest(projectDir, new Dictionary<string, string> { [_pkgName] = "^1.0.0" });

        CreateAndCachePackage(_pkgName, "1.0.0");

        // Pre-write a lock file that already resolves the dep
        var lockFile = new LockFile
        {
            LockVersion = 1,
            Resolved = new Dictionary<string, LockFileEntry>
            {
                [_pkgName] = new LockFileEntry { Version = "1.0.0", Resolved = "https://example.com/pkg.tar.gz" }
            }
        };
        lockFile.Save(projectDir);

        // Passing null source: if resolution were attempted, Install would throw.
        // Succeeding confirms the fresh lock file path was taken.
        PackageInstaller.Install(projectDir);

        Assert.True(File.Exists(Path.Combine(projectDir, "stashes", _pkgName, ".stash-version")));
    }

    [Fact]
    public void PackageInstaller_Update_FullUpdate_DeletesLockAndReInstalls()
    {
        string projectDir = Path.Combine(_tempDir, "project5");
        Directory.CreateDirectory(projectDir);
        WriteManifest(projectDir, new Dictionary<string, string> { [_pkgName] = "^1.0.0" });

        CreateAndCachePackage(_pkgName, "1.0.0");

        var source = new MockPackageSource();
        source.AddPackage(_pkgName, "1.0.0");

        PackageInstaller.Install(projectDir, source);
        Assert.True(File.Exists(Path.Combine(projectDir, "stash-lock.json")));

        PackageInstaller.Update(projectDir, null, source);

        Assert.True(File.Exists(Path.Combine(projectDir, "stash-lock.json")));
        Assert.True(File.Exists(Path.Combine(projectDir, "stashes", _pkgName, ".stash-version")));
    }

    [Fact]
    public void PackageInstaller_InstallPackage_AddsDependencyToManifest()
    {
        string projectDir = Path.Combine(_tempDir, "project6");
        Directory.CreateDirectory(projectDir);
        WriteManifest(projectDir); // no deps initially

        CreateAndCachePackage(_pkgName, "1.0.0");

        var source = new MockPackageSource();
        source.AddPackage(_pkgName, "1.0.0");

        PackageInstaller.InstallPackage(projectDir, _pkgName, "^1.0.0", source);

        var manifest = PackageManifest.Load(projectDir)!;
        Assert.True(manifest.Dependencies?.ContainsKey(_pkgName) ?? false);
        Assert.Equal("^1.0.0", manifest.Dependencies![_pkgName]);
    }
}

// ── IntegrityVerificationTests ────────────────────────────────────────────────

/// <summary>
/// Tests for lock-file integrity checking (cache path) and X-Integrity header
/// verification (HTTP download path).
///
/// HTTP download tests use System.Net.HttpListener on a randomly chosen port.
/// Cache-only tests exercise InstallEntry via InstallFromLockFile.
/// </summary>
public class IntegrityVerificationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _pkgName;

    public IntegrityVerificationTests()
    {
        _pkgName = $"@integrity/pkg-{Guid.NewGuid():N}";
        _tempDir = Path.Combine(Path.GetTempPath(), $"stash-integrity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        PackageInstaller.SetAllowMissingIntegrity(false);
        PackageCache.ClearPackage(_pkgName);
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static void WriteManifest(string projectDir, string pkgName)
    {
        File.WriteAllText(
            Path.Combine(projectDir, "stash.json"),
            $"{{\"name\":\"test\",\"version\":\"1.0.0\",\"dependencies\":{{\"{pkgName}\":\"^1.0.0\"}}}}");
    }

    private string BuildTarball()
    {
        string srcDir = Path.Combine(_tempDir, "pkg-src");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "main.stash"), "// integrity test package");

        string tarball = Path.Combine(_tempDir, "pkg.tar.gz");
        Tarball.Pack(srcDir, tarball);
        return tarball;
    }

    /// <summary>
    /// Starts a minimal HTTP server that responds to any GET with
    /// <paramref name="tarballBytes"/> and an optional X-Integrity header.
    /// Returns the base URL and an IDisposable that stops the listener.
    /// </summary>
    private static (string baseUrl, IDisposable server) StartTestServer(byte[] tarballBytes, string? integrityHeader)
    {
        // HttpListener cannot bind port 0, so we discover a free port and bind to it.
        // The naive approach (bind a TcpListener to 0, read the port, Stop(), then
        // Start() an HttpListener on it) has a TOCTOU race: the port can be reclaimed
        // in the Stop→Start gap, producing intermittent "address already in use".
        // Eliminate the gap with a retry-on-conflict loop: probe a free port, try to
        // bind the HttpListener, and on the (rare) conflict pick another port.
        HttpListener listener;
        string prefix;
        while (true)
        {
            int port;
            var tempListener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            tempListener.Start();
            port = ((System.Net.IPEndPoint)tempListener.LocalEndpoint).Port;
            tempListener.Stop();

            prefix = $"http://127.0.0.1:{port}/";
            listener = new HttpListener();
            listener.Prefixes.Add(prefix);
            try
            {
                listener.Start();
                break;
            }
            catch (HttpListenerException)
            {
                try { listener.Close(); } catch { }
                // Port was reclaimed between probe and bind — pick another.
            }
        }

        var cts = new System.Threading.CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); }
                catch (ObjectDisposedException) { break; }
                catch (HttpListenerException) { break; }

                ctx.Response.ContentType = "application/gzip";
                ctx.Response.ContentLength64 = tarballBytes.Length;
                if (integrityHeader != null)
                {
                    ctx.Response.Headers["X-Integrity"] = integrityHeader;
                }
                await ctx.Response.OutputStream.WriteAsync(tarballBytes);
                ctx.Response.Close();
            }
        }, cts.Token);

        var disposable = new ServerDisposable(listener, cts);
        return (prefix.TrimEnd('/'), disposable);
    }

    private sealed class ServerDisposable : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly System.Threading.CancellationTokenSource _cts;

        public ServerDisposable(HttpListener listener, System.Threading.CancellationTokenSource cts)
        {
            _listener = listener;
            _cts = cts;
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            _listener.Close();
        }
    }

    private static string ComputeIntegrityFromBytes(byte[] bytes)
    {
        byte[] hash = SHA256.HashData(bytes);
        return "sha256-" + Convert.ToBase64String(hash);
    }

    // ── cache integrity tests (no HTTP) ───────────────────────────────────────

    [Fact]
    public void InstallEntry_CachedTarballMatchesLockIntegrity_Extracts()
    {
        string tarball = BuildTarball();
        PackageCache.Store(_pkgName, "1.0.0", tarball);
        string integrity = LockFile.ComputeIntegrity(PackageCache.GetCachePath(_pkgName, "1.0.0"));

        string projectDir = Path.Combine(_tempDir, "project-match");
        Directory.CreateDirectory(projectDir);
        WriteManifest(projectDir, _pkgName);

        var lockFile = new LockFile
        {
            LockVersion = 1,
            Resolved = new Dictionary<string, LockFileEntry>
            {
                [_pkgName] = new LockFileEntry
                {
                    Version = "1.0.0",
                    Resolved = "https://example.com/pkg.tar.gz",
                    Integrity = integrity
                }
            }
        };

        PackageInstaller.InstallFromLockFile(projectDir, lockFile);

        Assert.True(File.Exists(Path.Combine(projectDir, "stashes", _pkgName, ".stash-version")));
    }

    [Fact]
    public void InstallEntry_CachedTarballMismatchesLockIntegrity_ThrowsAndDeletes()
    {
        string tarball = BuildTarball();
        PackageCache.Store(_pkgName, "1.0.0", tarball);
        string cachePath = PackageCache.GetCachePath(_pkgName, "1.0.0");
        // Use a deliberately wrong integrity value.
        string wrongIntegrity = "sha256-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

        string projectDir = Path.Combine(_tempDir, "project-mismatch");
        Directory.CreateDirectory(projectDir);
        WriteManifest(projectDir, _pkgName);

        var lockFile = new LockFile
        {
            LockVersion = 1,
            Resolved = new Dictionary<string, LockFileEntry>
            {
                [_pkgName] = new LockFileEntry
                {
                    Version = "1.0.0",
                    Resolved = "https://example.com/pkg.tar.gz",
                    Integrity = wrongIntegrity
                }
            }
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PackageInstaller.InstallFromLockFile(projectDir, lockFile));
        Assert.Contains("Integrity check failed", ex.Message);
        Assert.Contains("Refusing to install", ex.Message);
        // Cache file must be deleted after a mismatch.
        Assert.False(File.Exists(cachePath));
    }

    [Fact]
    public void InstallEntry_CachedTarballEmptyLockIntegrity_SkipsVerification()
    {
        string tarball = BuildTarball();
        PackageCache.Store(_pkgName, "1.0.0", tarball);

        string projectDir = Path.Combine(_tempDir, "project-nointegrity");
        Directory.CreateDirectory(projectDir);
        WriteManifest(projectDir, _pkgName);

        var lockFile = new LockFile
        {
            LockVersion = 1,
            Resolved = new Dictionary<string, LockFileEntry>
            {
                [_pkgName] = new LockFileEntry
                {
                    Version = "1.0.0",
                    Resolved = "https://example.com/pkg.tar.gz",
                    Integrity = null  // legacy — no check should occur
                }
            }
        };

        // Must not throw — verification is skipped when Integrity is null.
        PackageInstaller.InstallFromLockFile(projectDir, lockFile);
        Assert.True(File.Exists(Path.Combine(projectDir, "stashes", _pkgName, ".stash-version")));
    }

    // ── HTTP download integrity tests ─────────────────────────────────────────

    [Fact]
    public void DownloadAndCache_HeaderMatches_Succeeds()
    {
        string tarball = BuildTarball();
        byte[] tarballBytes = File.ReadAllBytes(tarball);
        string correctIntegrity = ComputeIntegrityFromBytes(tarballBytes);

        var (baseUrl, server) = StartTestServer(tarballBytes, correctIntegrity);
        using (server)
        {
            string projectDir = Path.Combine(_tempDir, "project-http-ok");
            Directory.CreateDirectory(projectDir);
            WriteManifest(projectDir, _pkgName);

            var lockFile = new LockFile
            {
                LockVersion = 1,
                Resolved = new Dictionary<string, LockFileEntry>
                {
                    [_pkgName] = new LockFileEntry
                    {
                        Version = "1.0.0",
                        Resolved = $"{baseUrl}/pkg.tar.gz"
                    }
                }
            };

            PackageInstaller.InstallFromLockFile(projectDir, lockFile);
            Assert.True(File.Exists(PackageCache.GetCachePath(_pkgName, "1.0.0")));
        }
    }

    [Fact]
    public void DownloadAndCache_HeaderMismatch_ThrowsAndDeletesCache()
    {
        string tarball = BuildTarball();
        byte[] tarballBytes = File.ReadAllBytes(tarball);
        string wrongIntegrity = "sha256-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

        var (baseUrl, server) = StartTestServer(tarballBytes, wrongIntegrity);
        using (server)
        {
            string projectDir = Path.Combine(_tempDir, "project-http-mismatch");
            Directory.CreateDirectory(projectDir);
            WriteManifest(projectDir, _pkgName);

            var lockFile = new LockFile
            {
                LockVersion = 1,
                Resolved = new Dictionary<string, LockFileEntry>
                {
                    [_pkgName] = new LockFileEntry
                    {
                        Version = "1.0.0",
                        Resolved = $"{baseUrl}/pkg.tar.gz"
                    }
                }
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                PackageInstaller.InstallFromLockFile(projectDir, lockFile));
            Assert.Contains("Integrity check failed", ex.Message);
            Assert.Contains("Refusing to install", ex.Message);
            Assert.False(File.Exists(PackageCache.GetCachePath(_pkgName, "1.0.0")));
        }
    }

    [Fact]
    public void DownloadAndCache_MissingHeader_ThrowsByDefault()
    {
        string tarball = BuildTarball();
        byte[] tarballBytes = File.ReadAllBytes(tarball);

        var (baseUrl, server) = StartTestServer(tarballBytes, integrityHeader: null);
        using (server)
        {
            string projectDir = Path.Combine(_tempDir, "project-http-noheader");
            Directory.CreateDirectory(projectDir);
            WriteManifest(projectDir, _pkgName);

            var lockFile = new LockFile
            {
                LockVersion = 1,
                Resolved = new Dictionary<string, LockFileEntry>
                {
                    [_pkgName] = new LockFileEntry
                    {
                        Version = "1.0.0",
                        Resolved = $"{baseUrl}/pkg.tar.gz"
                    }
                }
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                PackageInstaller.InstallFromLockFile(projectDir, lockFile));
            Assert.Contains("Integrity check failed", ex.Message);
            Assert.Contains("X-Integrity", ex.Message);
            Assert.False(File.Exists(PackageCache.GetCachePath(_pkgName, "1.0.0")));
        }
    }

    [Fact]
    public void DownloadAndCache_MissingHeaderWithAllowFlag_Succeeds()
    {
        string tarball = BuildTarball();
        byte[] tarballBytes = File.ReadAllBytes(tarball);

        var (baseUrl, server) = StartTestServer(tarballBytes, integrityHeader: null);
        using (server)
        {
            string projectDir = Path.Combine(_tempDir, "project-http-allow");
            Directory.CreateDirectory(projectDir);
            WriteManifest(projectDir, _pkgName);

            var lockFile = new LockFile
            {
                LockVersion = 1,
                Resolved = new Dictionary<string, LockFileEntry>
                {
                    [_pkgName] = new LockFileEntry
                    {
                        Version = "1.0.0",
                        Resolved = $"{baseUrl}/pkg.tar.gz"
                    }
                }
            };

            PackageInstaller.SetAllowMissingIntegrity(true);
            try
            {
                PackageInstaller.InstallFromLockFile(projectDir, lockFile);
            }
            finally
            {
                PackageInstaller.SetAllowMissingIntegrity(false);
            }

            Assert.True(File.Exists(PackageCache.GetCachePath(_pkgName, "1.0.0")));
        }
    }
}

// ── DeprecationWarningTests ───────────────────────────────────────────────────

/// <summary>
/// Tests that <c>PackageInstaller.Install</c> prints a warning to stderr
/// for each deprecated dependency discovered during resolution.
/// </summary>
public class DeprecationWarningTests : IDisposable
{
    private readonly string _tempDir;

    public DeprecationWarningTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"stash-depwarn-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    // MockPackageSource with deprecation support (local to this test class)
    private sealed class MockSource : IPackageSource
    {
        private readonly Dictionary<string, List<SemVer>> _versions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, PackageManifest> _manifests = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (bool Deprecated, string? Message)> _deprecations = new(StringComparer.Ordinal);

        public void Add(string name, string version, bool deprecated = false, string? deprecationMessage = null,
            Dictionary<string, string>? deps = null)
        {
            var sv = SemVer.Parse(version);
            if (!_versions.TryGetValue(name, out var list))
            {
                list = new List<SemVer>();
                _versions[name] = list;
            }
            list.Add(sv);
            string key = $"{name}@{version}";
            _manifests[key] = new PackageManifest { Name = name, Version = version, Dependencies = deps };
            _deprecations[key] = (deprecated, deprecationMessage);
        }

        public List<SemVer> GetAvailableVersions(string n) =>
            _versions.TryGetValue(n, out var l) ? l : new List<SemVer>();

        public PackageManifest? GetManifest(string n, SemVer v) =>
            _manifests.TryGetValue($"{n}@{v}", out var m) ? m : null;

        public string GetResolvedUrl(string n, SemVer v) =>
            $"https://registry.example.com/{n}/{v}.tar.gz";

        public string? GetIntegrity(string n, SemVer v) => null;

        public (bool Deprecated, string? Message) GetDeprecation(string n, SemVer v) =>
            _deprecations.TryGetValue($"{n}@{v}", out var d) ? d : (false, null);
    }

    private static string CaptureStdErr(Action action)
    {
        var orig = Console.Error;
        using var sw = new StringWriter();
        Console.SetError(sw);
        try { action(); }
        finally { Console.SetError(orig); }
        return sw.ToString();
    }

    private void WriteManifest(string projectDir, Dictionary<string, string> deps)
    {
        string depsJson = string.Join(", ", deps.Select(kv => $"\"{kv.Key}\": \"{kv.Value}\""));
        File.WriteAllText(
            Path.Combine(projectDir, "stash.json"),
            $"{{\"name\":\"test\",\"version\":\"1.0.0\",\"dependencies\":{{{depsJson}}}}}");
    }

    private string CreateAndCachePackage(string pkgName, string version)
    {
        string safeName = pkgName.TrimStart('@').Replace('/', '-');
        string srcDir = Path.Combine(_tempDir, $"src-{safeName}-{version}");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "main.stash"), $"// {pkgName}");
        string tarball = Path.Combine(_tempDir, $"{safeName}-{version}.tar.gz");
        Tarball.Pack(srcDir, tarball);
        PackageCache.Store(pkgName, version, tarball);
        return tarball;
    }

    [Fact]
    public void Install_OneDeprecatedDep_PrintsExactlyOneWarning()
    {
        string projectDir = Path.Combine(_tempDir, "proj-one");
        Directory.CreateDirectory(projectDir);
        WriteManifest(projectDir, new Dictionary<string, string> { ["@libs/old-lib"] = "^1.0.0" });
        CreateAndCachePackage("@libs/old-lib", "1.0.0");

        var source = new MockSource();
        source.Add("@libs/old-lib", "1.0.0", deprecated: true, deprecationMessage: "use new-lib instead");

        string stderr = CaptureStdErr(() => PackageInstaller.Install(projectDir, source));

        var warningLines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.StartsWith("warning:", StringComparison.Ordinal))
            .ToList();
        Assert.Single(warningLines);
        Assert.Equal("warning: @libs/old-lib@1.0.0 is deprecated: use new-lib instead", warningLines[0]);
    }

    [Fact]
    public void Install_MultipleDeprecatedDeps_NoDuplicateWarnings()
    {
        string projectDir = Path.Combine(_tempDir, "proj-multi");
        Directory.CreateDirectory(projectDir);
        WriteManifest(projectDir, new Dictionary<string, string>
        {
            ["@suite/alpha"] = "^1.0.0",
            ["@suite/beta"] = "^2.0.0"
        });
        CreateAndCachePackage("@suite/alpha", "1.0.0");
        CreateAndCachePackage("@suite/beta", "2.0.0");

        var source = new MockSource();
        source.Add("@suite/alpha", "1.0.0", deprecated: true, deprecationMessage: "alpha is old");
        source.Add("@suite/beta", "2.0.0", deprecated: true, deprecationMessage: "beta is retired");

        string stderr = CaptureStdErr(() => PackageInstaller.Install(projectDir, source));

        var warningLines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.StartsWith("warning:", StringComparison.Ordinal))
            .ToList();
        // Exactly two warnings, one per package (no duplicates).
        Assert.Equal(2, warningLines.Count);
        Assert.Contains("warning: @suite/alpha@1.0.0 is deprecated: alpha is old", warningLines);
        Assert.Contains("warning: @suite/beta@2.0.0 is deprecated: beta is retired", warningLines);
    }

    [Fact]
    public void Install_NoDeprecatedDeps_NoWarningOutput()
    {
        string projectDir = Path.Combine(_tempDir, "proj-clean");
        Directory.CreateDirectory(projectDir);
        WriteManifest(projectDir, new Dictionary<string, string> { ["@suite/clean-lib"] = "^3.0.0" });
        CreateAndCachePackage("@suite/clean-lib", "3.0.0");

        var source = new MockSource();
        source.Add("@suite/clean-lib", "3.0.0", deprecated: false);

        string stderr = CaptureStdErr(() => PackageInstaller.Install(projectDir, source));

        Assert.DoesNotContain("warning:", stderr);
    }
}

// ── InstallAtomicityTests ─────────────────────────────────────────────────────

/// <summary>
/// Tests that <see cref="PackageInstaller.Install(string, IPackageSource, PackageManifest)"/>
/// never mutates <c>stash.json</c> on disk — the caller (InstallCommand) is responsible for
/// writing the manifest only after a successful install.
/// </summary>
public class InstallAtomicityTests : IDisposable
{
    private readonly string _tempDir;

    public InstallAtomicityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"stash-atomicity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private static void WriteManifest(string projectDir, Dictionary<string, string>? deps = null)
    {
        string depsSection = deps == null || deps.Count == 0
            ? ""
            : ",\n  \"dependencies\": {" + string.Join(", ", deps.Select(kv => $"\"{kv.Key}\": \"{kv.Value}\"")) + "}";
        File.WriteAllText(
            Path.Combine(projectDir, "stash.json"),
            $"{{\n  \"name\": \"test-project\",\n  \"version\": \"1.0.0\"{depsSection}\n}}\n");
    }

    private string CreateAndCachePackage(string pkgName, string version)
    {
        string safeName = pkgName.TrimStart('@').Replace('/', '-');
        string srcDir = Path.Combine(_tempDir, $"src-{safeName}-{version}");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "main.stash"), $"// {pkgName} v{version}");
        string tarball = Path.Combine(_tempDir, $"{safeName}-{version}.tar.gz");
        Tarball.Pack(srcDir, tarball);
        PackageCache.Store(pkgName, version, tarball);
        return tarball;
    }

    // Package source that throws InvalidOperationException on GetAvailableVersions.
    private sealed class ThrowingPackageSource : IPackageSource
    {
        public List<SemVer> GetAvailableVersions(string packageName) =>
            throw new InvalidOperationException($"Network failure resolving '{packageName}'");

        public PackageManifest? GetManifest(string packageName, SemVer version) => null;
        public string GetResolvedUrl(string packageName, SemVer version) => "";
        public string? GetIntegrity(string packageName, SemVer version) => null;
        public (bool Deprecated, string? Message) GetDeprecation(string packageName, SemVer version) => (false, null);
    }

    // Package source that returns an empty version list (simulates "package not found").
    private sealed class NotFoundPackageSource : IPackageSource
    {
        public List<SemVer> GetAvailableVersions(string packageName) => new List<SemVer>();
        public PackageManifest? GetManifest(string packageName, SemVer version) => null;
        public string GetResolvedUrl(string packageName, SemVer version) => "";
        public string? GetIntegrity(string packageName, SemVer version) => null;
        public (bool Deprecated, string? Message) GetDeprecation(string packageName, SemVer version) => (false, null);
    }

    // Minimal working mock source.
    private sealed class MockSource : IPackageSource
    {
        private readonly Dictionary<string, List<SemVer>> _versions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, PackageManifest> _manifests = new(StringComparer.Ordinal);

        public void Add(string name, string version)
        {
            var sv = SemVer.Parse(version);
            if (!_versions.TryGetValue(name, out var list))
            {
                list = new List<SemVer>();
                _versions[name] = list;
            }
            list.Add(sv);
            _manifests[$"{name}@{version}"] = new PackageManifest { Name = name, Version = version };
        }

        public List<SemVer> GetAvailableVersions(string n) =>
            _versions.TryGetValue(n, out var l) ? l : new List<SemVer>();

        public PackageManifest? GetManifest(string n, SemVer v) =>
            _manifests.TryGetValue($"{n}@{v}", out var m) ? m : null;

        public string GetResolvedUrl(string n, SemVer v) =>
            $"https://registry.example.com/{n}/{v}.tar.gz";

        public string? GetIntegrity(string n, SemVer v) => null;
        public (bool Deprecated, string? Message) GetDeprecation(string n, SemVer v) => (false, null);
    }

    [Fact]
    public void InstallNew_NetworkFailure_ManifestUnchanged()
    {
        string projectDir = Path.Combine(_tempDir, "atomicity-netfail");
        Directory.CreateDirectory(projectDir);
        WriteManifest(projectDir); // no deps

        string manifestBefore = File.ReadAllText(Path.Combine(projectDir, "stash.json"));

        // Build in-memory manifest with new dep (simulating what InstallCommand would do).
        var manifest = PackageManifest.Load(projectDir)!;
        manifest.Dependencies = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["new-dep"] = "^1.0.0"
        };

        Assert.Throws<InvalidOperationException>(() =>
            PackageInstaller.Install(projectDir, new ThrowingPackageSource(), manifest));

        string manifestAfter = File.ReadAllText(Path.Combine(projectDir, "stash.json"));
        Assert.Equal(manifestBefore, manifestAfter);
    }

    [Fact]
    public void InstallNew_PackageNotFound_ManifestUnchanged()
    {
        string projectDir = Path.Combine(_tempDir, "atomicity-notfound");
        Directory.CreateDirectory(projectDir);
        WriteManifest(projectDir);

        string manifestBefore = File.ReadAllText(Path.Combine(projectDir, "stash.json"));

        var manifest = PackageManifest.Load(projectDir)!;
        manifest.Dependencies = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ghost-pkg"] = "^1.0.0"
        };

        // NotFoundPackageSource returns empty version lists → resolver throws.
        Assert.Throws<InvalidOperationException>(() =>
            PackageInstaller.Install(projectDir, new NotFoundPackageSource(), manifest));

        string manifestAfter = File.ReadAllText(Path.Combine(projectDir, "stash.json"));
        Assert.Equal(manifestBefore, manifestAfter);
    }

    [Fact]
    public void InstallNew_Success_ManifestNotWrittenByInstaller()
    {
        // The installer itself must NOT write stash.json — that is InstallCommand's job.
        string pkgName = $"@atomic/pkg-{Guid.NewGuid():N}";
        string projectDir = Path.Combine(_tempDir, "atomicity-success");
        Directory.CreateDirectory(projectDir);
        WriteManifest(projectDir); // no deps on disk

        CreateAndCachePackage(pkgName, "1.0.0");

        var source = new MockSource();
        source.Add(pkgName, "1.0.0");

        string manifestBefore = File.ReadAllText(Path.Combine(projectDir, "stash.json"));

        var manifest = PackageManifest.Load(projectDir)!;
        manifest.Dependencies = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [pkgName] = "^1.0.0"
        };

        // Should succeed — package is cached and source resolves it.
        PackageInstaller.Install(projectDir, source, manifest);

        // stash.json must be unchanged by the installer (caller writes it on success).
        string manifestAfter = File.ReadAllText(Path.Combine(projectDir, "stash.json"));
        Assert.Equal(manifestBefore, manifestAfter);

        // But the package itself must be installed.
        Assert.True(File.Exists(Path.Combine(projectDir, "stashes", pkgName, ".stash-version")));

        PackageCache.ClearPackage(pkgName);
    }

    // Source that resolves a package successfully but advertises a wrong integrity
    // value. This triggers the post-resolve integrity-check failure path inside
    // InstallEntry, which used to leave stash-lock.json mutated.
    private sealed class BadIntegritySource : IPackageSource
    {
        private readonly Dictionary<string, List<SemVer>> _versions = new(StringComparer.Ordinal);

        public void Add(string name, string version)
        {
            var sv = SemVer.Parse(version);
            if (!_versions.TryGetValue(name, out var list))
            {
                list = new List<SemVer>();
                _versions[name] = list;
            }
            list.Add(sv);
        }

        public List<SemVer> GetAvailableVersions(string n) =>
            _versions.TryGetValue(n, out var l) ? l : new List<SemVer>();

        public PackageManifest? GetManifest(string n, SemVer v) =>
            new PackageManifest { Name = n, Version = v.ToString() };

        public string GetResolvedUrl(string n, SemVer v) =>
            $"https://registry.example.com/{n}/{v}.tar.gz";

        // Deliberately wrong integrity — never matches the cached tarball.
        public string? GetIntegrity(string n, SemVer v) =>
            "sha256-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

        public (bool Deprecated, string? Message) GetDeprecation(string n, SemVer v) => (false, null);
    }

    [Fact]
    public void InstallNew_IntegrityMismatch_ManifestAndLockUnchanged()
    {
        // Acceptance criterion: a failed `stash pkg install <dep>` due to integrity
        // mismatch must leave BOTH stash.json AND stash-lock.json byte-identical to
        // their pre-command state. The earlier implementation wrote the new lock file
        // before InstallFromLockFile ran, leaking a lock entry on extraction failure.
        string pkgName = $"@atomic/bad-{Guid.NewGuid():N}";
        string projectDir = Path.Combine(_tempDir, "atomicity-integrity");
        Directory.CreateDirectory(projectDir);
        WriteManifest(projectDir);

        CreateAndCachePackage(pkgName, "1.0.0");

        string manifestBefore = File.ReadAllText(Path.Combine(projectDir, "stash.json"));
        string lockPath = Path.Combine(projectDir, "stash-lock.json");
        bool lockExistedBefore = File.Exists(lockPath);
        string? lockBefore = lockExistedBefore ? File.ReadAllText(lockPath) : null;

        var source = new BadIntegritySource();
        source.Add(pkgName, "1.0.0");

        var manifest = PackageManifest.Load(projectDir)!;
        manifest.Dependencies = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [pkgName] = "^1.0.0"
        };

        Assert.Throws<InvalidOperationException>(() =>
            PackageInstaller.Install(projectDir, source, manifest));

        // stash.json untouched.
        string manifestAfter = File.ReadAllText(Path.Combine(projectDir, "stash.json"));
        Assert.Equal(manifestBefore, manifestAfter);

        // stash-lock.json untouched: if it didn't exist before, it must not exist now;
        // if it existed before, its bytes must be identical.
        if (lockExistedBefore)
        {
            Assert.True(File.Exists(lockPath));
            Assert.Equal(lockBefore, File.ReadAllText(lockPath));
        }
        else
        {
            Assert.False(File.Exists(lockPath));
        }

        PackageCache.ClearPackage(pkgName);
    }
}

// ── LockFileFreshnessTests ────────────────────────────────────────────────────

/// <summary>
/// Tests for the lockfile freshness checks: orphan detection, constraint-mismatch
/// detection, and the no-op fast path when nothing has changed.
/// </summary>
public class LockFileFreshnessTests : IDisposable
{
    private readonly string _tempDir;

    public LockFileFreshnessTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"stash-freshness-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private static void WriteManifest(string projectDir, Dictionary<string, string>? deps = null)
    {
        string depsSection = deps == null || deps.Count == 0
            ? ""
            : ",\n  \"dependencies\": {" + string.Join(", ", deps.Select(kv => $"\"{kv.Key}\": \"{kv.Value}\"")) + "}";
        File.WriteAllText(
            Path.Combine(projectDir, "stash.json"),
            $"{{\n  \"name\": \"test-project\",\n  \"version\": \"1.0.0\"{depsSection}\n}}\n");
    }

    private string CreateAndCachePackage(string pkgName, string version)
    {
        string safeName = pkgName.TrimStart('@').Replace('/', '-');
        string srcDir = Path.Combine(_tempDir, $"src-{safeName}-{version}");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "main.stash"), $"// {pkgName} v{version}");
        string tarball = Path.Combine(_tempDir, $"{safeName}-{version}.tar.gz");
        Tarball.Pack(srcDir, tarball);
        PackageCache.Store(pkgName, version, tarball);
        return tarball;
    }

    private sealed class MockSource : IPackageSource
    {
        private readonly Dictionary<string, List<SemVer>> _versions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, PackageManifest> _manifests = new(StringComparer.Ordinal);

        public int ResolveCallCount { get; private set; }

        public void Add(string name, string version, Dictionary<string, string>? deps = null)
        {
            var sv = SemVer.Parse(version);
            if (!_versions.TryGetValue(name, out var list))
            {
                list = new List<SemVer>();
                _versions[name] = list;
            }
            list.Add(sv);
            _manifests[$"{name}@{version}"] = new PackageManifest { Name = name, Version = version, Dependencies = deps };
        }

        public List<SemVer> GetAvailableVersions(string n)
        {
            ResolveCallCount++;
            return _versions.TryGetValue(n, out var l) ? l : new List<SemVer>();
        }

        public PackageManifest? GetManifest(string n, SemVer v) =>
            _manifests.TryGetValue($"{n}@{v}", out var m) ? m : null;

        public string GetResolvedUrl(string n, SemVer v) =>
            $"https://registry.example.com/{n}/{v}.tar.gz";

        public string? GetIntegrity(string n, SemVer v) => null;
        public (bool Deprecated, string? Message) GetDeprecation(string n, SemVer v) => (false, null);
    }

    [Fact]
    public void Install_RemovesOrphanLockEntry_AndDirectory()
    {
        string orphanPkg = $"@test/orphan-{Guid.NewGuid():N}";
        string projectDir = Path.Combine(_tempDir, "orphan-test");
        Directory.CreateDirectory(projectDir);
        // Manifest no longer lists orphanPkg.
        WriteManifest(projectDir, deps: null);

        // Stale lock still has orphanPkg.
        var oldLock = new LockFile
        {
            LockVersion = 1,
            Resolved = new Dictionary<string, LockFileEntry>
            {
                [orphanPkg] = new LockFileEntry
                {
                    Version = "1.2.3",
                    Resolved = "https://example.com/orphan-1.2.3.tar.gz"
                }
            }
        };
        oldLock.Save(projectDir);

        // Simulate orphan already extracted on disk.
        string orphanDir = Path.Combine(projectDir, "stashes", orphanPkg);
        Directory.CreateDirectory(orphanDir);
        File.WriteAllText(Path.Combine(orphanDir, ".stash-version"), "1.2.3");

        // Source can resolve an empty dep set.
        var source = new MockSource();

        PackageInstaller.Install(projectDir, source);

        var newLock = LockFile.Load(projectDir)!;
        Assert.False(newLock.Resolved.ContainsKey(orphanPkg));
        Assert.False(Directory.Exists(orphanDir));
    }

    [Fact]
    public void Install_TransitiveDepReachable_StaysInLock()
    {
        // "a" depends on "b" transitively.  Manifest only lists "a".
        // The lock must keep "b" because it is reachable through "a".
        string pkgA = $"@pkg/a-{Guid.NewGuid():N}";
        string pkgB = $"@pkg/b-{Guid.NewGuid():N}";
        string projectDir = Path.Combine(_tempDir, "transitive-test");
        Directory.CreateDirectory(projectDir);
        WriteManifest(projectDir, new Dictionary<string, string> { [pkgA] = "^1.0.0" });

        CreateAndCachePackage(pkgA, "1.0.0");
        CreateAndCachePackage(pkgB, "1.0.0");

        var lockFile = new LockFile
        {
            LockVersion = 1,
            Resolved = new Dictionary<string, LockFileEntry>
            {
                [pkgA] = new LockFileEntry
                {
                    Version = "1.0.0",
                    Resolved = "https://example.com/a-1.0.0.tar.gz",
                    Dependencies = new Dictionary<string, string> { [pkgB] = "^1.0.0" }
                },
                [pkgB] = new LockFileEntry
                {
                    Version = "1.0.0",
                    Resolved = "https://example.com/b-1.0.0.tar.gz"
                }
            }
        };
        lockFile.Save(projectDir);

        // No source — if resolution were triggered this would throw.
        PackageInstaller.Install(projectDir);

        var resultLock = LockFile.Load(projectDir)!;
        Assert.True(resultLock.Resolved.ContainsKey(pkgA));
        Assert.True(resultLock.Resolved.ContainsKey(pkgB));

        PackageCache.ClearPackage(pkgA);
        PackageCache.ClearPackage(pkgB);
    }

    [Fact]
    public void Install_ConstraintUpgrade_ResolvesNewVersion()
    {
        string pkgName = $"@upgrade/pkg-{Guid.NewGuid():N}";
        string projectDir = Path.Combine(_tempDir, "constraint-upgrade");
        Directory.CreateDirectory(projectDir);
        // Manifest now requires ^2.0.0.
        WriteManifest(projectDir, new Dictionary<string, string> { [pkgName] = "^2.0.0" });

        CreateAndCachePackage(pkgName, "1.2.5");
        CreateAndCachePackage(pkgName, "2.0.1");

        // Stale lock has 1.2.5 — does not satisfy ^2.0.0.
        var oldLock = new LockFile
        {
            LockVersion = 1,
            Resolved = new Dictionary<string, LockFileEntry>
            {
                [pkgName] = new LockFileEntry
                {
                    Version = "1.2.5",
                    Resolved = "https://example.com/pkg-1.2.5.tar.gz"
                }
            }
        };
        oldLock.Save(projectDir);

        var source = new MockSource();
        source.Add(pkgName, "1.2.5");
        source.Add(pkgName, "2.0.1");

        PackageInstaller.Install(projectDir, source);

        var newLock = LockFile.Load(projectDir)!;
        Assert.True(newLock.Resolved.TryGetValue(pkgName, out var entry));
        Assert.Equal("2.0.1", entry.Version);

        string versionMarker = Path.Combine(projectDir, "stashes", pkgName, ".stash-version");
        Assert.True(File.Exists(versionMarker));
        Assert.Equal("2.0.1", File.ReadAllText(versionMarker).Trim());

        PackageCache.ClearPackage(pkgName);
    }

    [Fact]
    public void Install_NoChanges_FastPath()
    {
        // Lock is fully up-to-date: constraint satisfied, no orphans.
        // Passing null source proves that no resolution occurs.
        string pkgName = $"@fastpath/pkg-{Guid.NewGuid():N}";
        string projectDir = Path.Combine(_tempDir, "fast-path");
        Directory.CreateDirectory(projectDir);
        WriteManifest(projectDir, new Dictionary<string, string> { [pkgName] = "^1.0.0" });

        CreateAndCachePackage(pkgName, "1.0.0");

        var lockFile = new LockFile
        {
            LockVersion = 1,
            Resolved = new Dictionary<string, LockFileEntry>
            {
                [pkgName] = new LockFileEntry
                {
                    Version = "1.0.0",
                    Resolved = "https://example.com/fastpath-1.0.0.tar.gz"
                }
            }
        };
        lockFile.Save(projectDir);

        // null source: Install would throw if it tried to resolve.
        PackageInstaller.Install(projectDir);

        // Package installed from lock, lock unchanged.
        string versionMarker = Path.Combine(projectDir, "stashes", pkgName, ".stash-version");
        Assert.True(File.Exists(versionMarker));
        Assert.Equal("1.0.0", File.ReadAllText(versionMarker).Trim());

        PackageCache.ClearPackage(pkgName);
    }
}
