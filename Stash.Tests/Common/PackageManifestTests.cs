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

    [Fact]
    public void Load_AllPhase2Fields_ParsedCorrectly()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            string json = """
                {
                    "name": "my-package",
                    "version": "1.0.0",
                    "author": "Alice",
                    "license": "MIT",
                    "repository": "https://github.com/example/my-package",
                    "keywords": ["cli", "scripting"],
                    "stash": ">=1.0.0",
                    "files": ["lib/**", "bin/**"],
                    "registries": { "default": "https://registry.example.com" },
                    "private": false
                }
                """;
            File.WriteAllText(Path.Combine(tmpDir, "stash.json"), json);

            var manifest = PackageManifest.Load(tmpDir);

            Assert.NotNull(manifest);
            Assert.Equal("Alice", manifest.Author);
            Assert.Equal("MIT", manifest.License);
            Assert.Equal("https://github.com/example/my-package", manifest.Repository);
            Assert.NotNull(manifest.Keywords);
            Assert.Equal(2, manifest.Keywords.Count);
            Assert.Contains("cli", manifest.Keywords);
            Assert.Contains("scripting", manifest.Keywords);
            Assert.Equal(">=1.0.0", manifest.Stash);
            Assert.NotNull(manifest.Files);
            Assert.Equal(2, manifest.Files.Count);
            Assert.Contains("lib/**", manifest.Files);
            Assert.Contains("bin/**", manifest.Files);
            Assert.NotNull(manifest.Registries);
            Assert.Equal("https://registry.example.com", manifest.Registries["default"]);
            Assert.False(manifest.Private);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void Load_PrivateTrue_PrivateIsTrue()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "stash.json"), """{"name": "internal-pkg", "private": true}""");

            var manifest = PackageManifest.Load(tmpDir);

            Assert.NotNull(manifest);
            Assert.True(manifest.Private);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Theory]
    [InlineData("my-package")]
    [InlineData("a")]
    [InlineData("test123")]
    public void IsValidPackageName_ValidName_ReturnsTrue(string name)
    {
        Assert.True(PackageManifest.IsValidPackageName(name));
    }

    [Theory]
    [InlineData("@scope/name")]
    [InlineData("@my-org/my-pkg")]
    public void IsValidPackageName_ValidScopedName_ReturnsTrue(string name)
    {
        Assert.True(PackageManifest.IsValidPackageName(name));
    }

    [Theory]
    [InlineData("My-Package")]
    [InlineData("123abc")]
    [InlineData("-test")]
    [InlineData("")]
    public void IsValidPackageName_InvalidName_ReturnsFalse(string name)
    {
        Assert.False(PackageManifest.IsValidPackageName(name));
    }

    [Fact]
    public void IsValidPackageName_NameOver64Chars_ReturnsFalse()
    {
        string longName = new string('a', 65);

        Assert.False(PackageManifest.IsValidPackageName(longName));
    }

    [Theory]
    [InlineData("@/name")]
    [InlineData("@scope/")]
    public void IsValidPackageName_InvalidScopedName_ReturnsFalse(string name)
    {
        Assert.False(PackageManifest.IsValidPackageName(name));
    }

    [Fact]
    public void Validate_ValidManifest_ReturnsEmptyErrors()
    {
        var manifest = new PackageManifest { Name = "my-package", Version = "1.0.0" };

        var errors = manifest.Validate();

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_InvalidName_ReturnsNameError()
    {
        var manifest = new PackageManifest { Name = "My-Package", Version = "1.0.0" };

        var errors = manifest.Validate();

        Assert.Contains(errors, e => e.Contains("Invalid package name"));
    }

    [Fact]
    public void Validate_InvalidVersion_ReturnsVersionError()
    {
        var manifest = new PackageManifest { Name = "my-package", Version = "not-semver" };

        var errors = manifest.Validate();

        Assert.Contains(errors, e => e.Contains("Invalid version"));
    }

    [Fact]
    public void Validate_InvalidDependencyConstraint_ReturnsDependencyError()
    {
        var manifest = new PackageManifest
        {
            Name = "my-package",
            Version = "1.0.0",
            Dependencies = new Dictionary<string, string> { { "some-lib", "!!invalid!!" } }
        };

        var errors = manifest.Validate();

        Assert.Contains(errors, e => e.Contains("some-lib"));
    }

    [Fact]
    public void Validate_GitDependency_ReturnsNoError()
    {
        var manifest = new PackageManifest
        {
            Name = "my-package",
            Version = "1.0.0",
            Dependencies = new Dictionary<string, string> { { "git-dep", "git:https://github.com/example/repo" } }
        };

        var errors = manifest.Validate();

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateForPublishing_CompleteValidManifest_ReturnsEmptyErrors()
    {
        var manifest = new PackageManifest { Name = "my-package", Version = "1.0.0" };

        var errors = manifest.ValidateForPublishing();

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateForPublishing_MissingName_ReturnsNameError()
    {
        var manifest = new PackageManifest { Version = "1.0.0" };

        var errors = manifest.ValidateForPublishing();

        Assert.Contains(errors, e => e.Contains("name"));
    }

    [Fact]
    public void ValidateForPublishing_MissingVersion_ReturnsVersionError()
    {
        var manifest = new PackageManifest { Name = "my-package" };

        var errors = manifest.ValidateForPublishing();

        Assert.Contains(errors, e => e.Contains("version"));
    }

    [Fact]
    public void ValidateForPublishing_PrivateTrue_ReturnsPrivateError()
    {
        var manifest = new PackageManifest { Name = "my-package", Version = "1.0.0", Private = true };

        var errors = manifest.ValidateForPublishing();

        Assert.Contains(errors, e => e.Contains("private"));
    }
}
