using Stash.Common;

namespace Stash.Tests.Common;

public class ModuleResolverTests
{
    // ── IsBareSpecifier ──────────────────────────────────────────────────────

    [Fact]
    public void IsBareSpecifier_RelativeDotSlash_ReturnsFalse()
    {
        Assert.False(ModuleResolver.IsBareSpecifier("./lib/utils"));
    }

    [Fact]
    public void IsBareSpecifier_RelativeDotDotSlash_ReturnsFalse()
    {
        Assert.False(ModuleResolver.IsBareSpecifier("../lib/utils"));
    }

    [Fact]
    public void IsBareSpecifier_AbsoluteSlash_ReturnsFalse()
    {
        Assert.False(ModuleResolver.IsBareSpecifier("/usr/local/lib"));
    }

    [Fact]
    public void IsBareSpecifier_PackageName_ReturnsTrue()
    {
        Assert.True(ModuleResolver.IsBareSpecifier("http-utils"));
    }

    [Fact]
    public void IsBareSpecifier_ScopedPackage_ReturnsTrue()
    {
        Assert.True(ModuleResolver.IsBareSpecifier("@scope/name"));
    }

    [Fact]
    public void IsBareSpecifier_PackageWithSubpath_ReturnsTrue()
    {
        Assert.True(ModuleResolver.IsBareSpecifier("http-utils/lib/core"));
    }

    // ── FindProjectRoot ──────────────────────────────────────────────────────

    [Fact]
    public void FindProjectRoot_DirectoryWithManifest_ReturnsDirectory()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "stash.json"), """{"name": "test"}""");

            string? result = ModuleResolver.FindProjectRoot(tmpDir);

            Assert.Equal(tmpDir, result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void FindProjectRoot_NestedDirectory_FindsParent()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "stash.json"), """{"name": "test"}""");
            string subDir = Path.Combine(tmpDir, "src", "utils");
            Directory.CreateDirectory(subDir);

            string? result = ModuleResolver.FindProjectRoot(subDir);

            Assert.Equal(tmpDir, result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void FindProjectRoot_SkipsStashesDirectory_ReturnsProjectRoot()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            // Project root stash.json
            File.WriteAllText(Path.Combine(tmpDir, "stash.json"), """{"name": "my-project"}""");

            // Package inside stashes/ also has a stash.json — it must be skipped
            string pkgDir = Path.Combine(tmpDir, "stashes", "http-utils");
            Directory.CreateDirectory(pkgDir);
            File.WriteAllText(Path.Combine(pkgDir, "stash.json"), """{"name": "http-utils"}""");

            // Start from inside the stashes package
            string? result = ModuleResolver.FindProjectRoot(pkgDir);

            Assert.Equal(tmpDir, result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void FindProjectRoot_InsideStashesPackage_ReturnsProjectRoot()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "stash.json"), """{"name": "my-project"}""");

            // Importing file is deep inside a stashes/ package
            string srcDir = Path.Combine(tmpDir, "stashes", "toolkit", "src");
            Directory.CreateDirectory(srcDir);

            string? result = ModuleResolver.FindProjectRoot(srcDir);

            Assert.Equal(tmpDir, result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void FindProjectRoot_NoManifest_ReturnsNull()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        string nested = Path.Combine(tmpDir, "src", "utils");
        Directory.CreateDirectory(nested);
        try
        {
            // No stash.json anywhere in the tree
            string? result = ModuleResolver.FindProjectRoot(nested);

            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    // ── ResolvePackageImport ─────────────────────────────────────────────────

    [Fact]
    public void ResolvePackageImport_SimplePackage_ResolvesEntryPoint()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "stash.json"), """{"name": "my-project"}""");

            string pkgDir = Path.Combine(tmpDir, "stashes", "http-utils");
            Directory.CreateDirectory(pkgDir);
            string indexPath = Path.Combine(pkgDir, "index.stash");
            File.WriteAllText(indexPath, "");

            string? result = ModuleResolver.ResolvePackageImport("http-utils", tmpDir);

            Assert.Equal(indexPath, result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void ResolvePackageImport_PackageWithCustomMain_ResolvesMain()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "stash.json"), """{"name": "my-project"}""");

            string pkgDir = Path.Combine(tmpDir, "stashes", "toolkit");
            Directory.CreateDirectory(pkgDir);
            File.WriteAllText(Path.Combine(pkgDir, "stash.json"), """{"name": "toolkit", "main": "lib/main.stash"}""");

            string libDir = Path.Combine(pkgDir, "lib");
            Directory.CreateDirectory(libDir);
            string mainPath = Path.Combine(libDir, "main.stash");
            File.WriteAllText(mainPath, "");

            string? result = ModuleResolver.ResolvePackageImport("toolkit", tmpDir);

            Assert.Equal(mainPath, result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void ResolvePackageImport_PackageWithSubpath_ResolvesSubpath()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "stash.json"), """{"name": "my-project"}""");

            string libDir = Path.Combine(tmpDir, "stashes", "toolkit", "lib");
            Directory.CreateDirectory(libDir);
            string corePath = Path.Combine(libDir, "core.stash");
            File.WriteAllText(corePath, "");

            // "toolkit/lib/core" → stashes/toolkit/lib/core.stash
            string? result = ModuleResolver.ResolvePackageImport("toolkit/lib/core", tmpDir);

            Assert.Equal(corePath, result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void ResolvePackageImport_ScopedPackage_ResolvesCorrectly()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "stash.json"), """{"name": "my-project"}""");

            // stashes/@scope/name/index.stash
            string pkgDir = Path.Combine(tmpDir, "stashes", "@scope", "name");
            Directory.CreateDirectory(pkgDir);
            string indexPath = Path.Combine(pkgDir, "index.stash");
            File.WriteAllText(indexPath, "");

            string? result = ModuleResolver.ResolvePackageImport("@scope/name", tmpDir);

            Assert.Equal(indexPath, result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void ResolvePackageImport_ScopedPackageSubpath_ResolvesCorrectly()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "stash.json"), """{"name": "my-project"}""");

            // stashes/@scope/name/lib/core.stash
            string libDir = Path.Combine(tmpDir, "stashes", "@scope", "name", "lib");
            Directory.CreateDirectory(libDir);
            string corePath = Path.Combine(libDir, "core.stash");
            File.WriteAllText(corePath, "");

            // "@scope/name/lib/core" → stashes/@scope/name/lib/core.stash
            string? result = ModuleResolver.ResolvePackageImport("@scope/name/lib/core", tmpDir);

            Assert.Equal(corePath, result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void ResolvePackageImport_DirectorySubpath_ResolvesIndexStash()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "stash.json"), """{"name": "my-project"}""");

            // stashes/toolkit/lib/ is a directory containing index.stash
            string libDir = Path.Combine(tmpDir, "stashes", "toolkit", "lib");
            Directory.CreateDirectory(libDir);
            string indexPath = Path.Combine(libDir, "index.stash");
            File.WriteAllText(indexPath, "");

            // "toolkit/lib" → stashes/toolkit/lib/index.stash (directory with index)
            string? result = ModuleResolver.ResolvePackageImport("toolkit/lib", tmpDir);

            Assert.Equal(indexPath, result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void ResolvePackageImport_NonexistentPackage_ReturnsNull()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "stash.json"), """{"name": "my-project"}""");
            // No stashes/ directory — guarantee uniqueness so global fallback also misses
            string uniquePackage = "stash-nonexistent-" + Guid.NewGuid().ToString("N");

            string? result = ModuleResolver.ResolvePackageImport(uniquePackage, tmpDir);

            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    // ── ResolveFilePath ──────────────────────────────────────────────────────

    [Fact]
    public void ResolveFilePath_ExactMatch_ReturnsPath()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            string filePath = Path.Combine(tmpDir, "utils.stash");
            File.WriteAllText(filePath, "");

            string? result = ModuleResolver.ResolveFilePath(filePath);

            Assert.Equal(filePath, result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void ResolveFilePath_AutoStashExtension_ReturnsPath()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            string filePath = Path.Combine(tmpDir, "utils.stash");
            File.WriteAllText(filePath, "");
            string baseWithoutExt = Path.Combine(tmpDir, "utils");

            // "utils" → "utils.stash" via auto-extension
            string? result = ModuleResolver.ResolveFilePath(baseWithoutExt);

            Assert.Equal(filePath, result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void ResolveFilePath_DirectoryWithIndex_ReturnsIndexStash()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            string libDir = Path.Combine(tmpDir, "lib");
            Directory.CreateDirectory(libDir);
            string indexPath = Path.Combine(libDir, "index.stash");
            File.WriteAllText(indexPath, "");

            string? result = ModuleResolver.ResolveFilePath(libDir);

            Assert.Equal(indexPath, result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void ResolveFilePath_NothingExists_ReturnsNull()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            string nonExistentBase = Path.Combine(tmpDir, "nonexistent");

            string? result = ModuleResolver.ResolveFilePath(nonExistentBase);

            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }
}
