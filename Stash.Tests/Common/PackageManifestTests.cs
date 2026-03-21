using Stash.Common;

namespace Stash.Tests.Common;

public class PackageManifestTests
{
    [Fact]
    public void Load_ValidManifest_ReturnsManifest()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            string json = """
                {
                    "name": "my-package",
                    "version": "1.2.3",
                    "main": "lib/index.stash",
                    "description": "A test package",
                    "dependencies": {
                        "http-utils": "^1.0.0",
                        "json-tools": "~2.1.0"
                    }
                }
                """;
            File.WriteAllText(Path.Combine(tmpDir, "stash.json"), json);

            var manifest = PackageManifest.Load(tmpDir);

            Assert.NotNull(manifest);
            Assert.Equal("my-package", manifest.Name);
            Assert.Equal("1.2.3", manifest.Version);
            Assert.Equal("lib/index.stash", manifest.Main);
            Assert.Equal("A test package", manifest.Description);
            Assert.NotNull(manifest.Dependencies);
            Assert.Equal(2, manifest.Dependencies.Count);
            Assert.Equal("^1.0.0", manifest.Dependencies["http-utils"]);
            Assert.Equal("~2.1.0", manifest.Dependencies["json-tools"]);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void Load_MinimalManifest_ReturnsDefaults()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "stash.json"), """{"name": "test"}""");

            var manifest = PackageManifest.Load(tmpDir);

            Assert.NotNull(manifest);
            Assert.Equal("test", manifest.Name);
            Assert.Null(manifest.Version);
            Assert.Null(manifest.Main);
            Assert.Null(manifest.Description);
            Assert.Null(manifest.Dependencies);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void Load_NoStashJson_ReturnsNull()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            var result = PackageManifest.Load(tmpDir);

            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void Load_NonexistentDirectory_ReturnsNull()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        // Deliberately don't create the directory

        var result = PackageManifest.Load(tmpDir);

        Assert.Null(result);
    }

    [Fact]
    public void Load_MalformedJson_ThrowsException()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "stash.json"), "{ this is not valid json }");

            Assert.Throws<InvalidDataException>(() => PackageManifest.Load(tmpDir));
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void Load_WithDependencies_ParsesDependencies()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            string json = """
                {
                    "name": "app",
                    "dependencies": {
                        "http-utils": "1.0.0",
                        "json-tools": "2.0.0",
                        "@scope/pkg": "3.0.0"
                    }
                }
                """;
            File.WriteAllText(Path.Combine(tmpDir, "stash.json"), json);

            var manifest = PackageManifest.Load(tmpDir);

            Assert.NotNull(manifest);
            Assert.NotNull(manifest.Dependencies);
            Assert.Equal(3, manifest.Dependencies.Count);
            Assert.Equal("1.0.0", manifest.Dependencies["http-utils"]);
            Assert.Equal("2.0.0", manifest.Dependencies["json-tools"]);
            Assert.Equal("3.0.0", manifest.Dependencies["@scope/pkg"]);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void GetEntryPoint_CustomMain_ReturnsMain()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "stash.json"), """{"name": "pkg", "main": "lib/main.stash"}""");

            string result = PackageManifest.GetEntryPoint(tmpDir);

            Assert.Equal("lib/main.stash", result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void GetEntryPoint_NoMain_ReturnsIndexStash()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "stash.json"), """{"name": "pkg"}""");

            string result = PackageManifest.GetEntryPoint(tmpDir);

            Assert.Equal("index.stash", result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void GetEntryPoint_NoStashJson_ReturnsIndexStash()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            string result = PackageManifest.GetEntryPoint(tmpDir);

            Assert.Equal("index.stash", result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }
}
