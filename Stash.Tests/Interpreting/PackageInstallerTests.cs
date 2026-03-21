using System.Formats.Tar;
using System.IO.Compression;
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
        _pkgName = $"test-pkg-{Guid.NewGuid():N}";
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
        string path = PackageCache.GetCachePath("mylib", "1.2.3");

        Assert.Contains("mylib", path);
        Assert.EndsWith(Path.Combine("mylib", "1.2.3.tar.gz"), path);
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

        public void AddPackage(string name, string version, Dictionary<string, string>? deps = null)
        {
            if (!_packages.TryGetValue(name, out var list))
            {
                list = new List<(SemVer, PackageManifest)>();
                _packages[name] = list;
            }
            list.Add((SemVer.Parse(version), new PackageManifest { Name = name, Version = version, Dependencies = deps }));
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
    }

    public PackageInstallerTests()
    {
        _pkgName = $"test-pkg-{Guid.NewGuid():N}";
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
        string pkgSrcDir = Path.Combine(_tempDir, $"pkg-src-{pkgName}-{version}");
        Directory.CreateDirectory(pkgSrcDir);
        File.WriteAllText(Path.Combine(pkgSrcDir, "main.stash"), $"// {pkgName} v{version}");

        string tarball = Path.Combine(_tempDir, $"{pkgName}-{version}.tar.gz");
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
