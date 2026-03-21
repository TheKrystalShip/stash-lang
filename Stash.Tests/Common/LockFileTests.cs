using Stash.Common;

namespace Stash.Tests.Common;

public class LockFileTests
{
    // ── Load ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Load_ValidLockFile_ParsesAllFields()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            string json = """
                {
                  "lockVersion": 2,
                  "stash": ">=0.5.0",
                  "resolved": {
                    "my-lib@1.0.0": {
                      "version": "1.0.0",
                      "resolved": "https://registry.example.com/my-lib-1.0.0.tar.gz",
                      "integrity": "sha256-abc123=="
                    }
                  }
                }
                """;
            File.WriteAllText(Path.Combine(tmpDir, "stash-lock.json"), json);

            var lockFile = LockFile.Load(tmpDir);

            Assert.NotNull(lockFile);
            Assert.Equal(2, lockFile.LockVersion);
            Assert.Equal(">=0.5.0", lockFile.Stash);
            Assert.Single(lockFile.Resolved);
            var entry = lockFile.Resolved["my-lib@1.0.0"];
            Assert.Equal("1.0.0", entry.Version);
            Assert.Equal("https://registry.example.com/my-lib-1.0.0.tar.gz", entry.Resolved);
            Assert.Equal("sha256-abc123==", entry.Integrity);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void Load_EntryWithDependencies_PopulatesDependenciesDict()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            string json = """
                {
                  "lockVersion": 1,
                  "resolved": {
                    "pkg-a@2.0.0": {
                      "version": "2.0.0",
                      "dependencies": {
                        "pkg-b": "^1.0.0",
                        "pkg-c": "~3.2.1"
                      }
                    }
                  }
                }
                """;
            File.WriteAllText(Path.Combine(tmpDir, "stash-lock.json"), json);

            var lockFile = LockFile.Load(tmpDir);

            Assert.NotNull(lockFile);
            var entry = lockFile.Resolved["pkg-a@2.0.0"];
            Assert.NotNull(entry.Dependencies);
            Assert.Equal(2, entry.Dependencies.Count);
            Assert.Equal("^1.0.0", entry.Dependencies["pkg-b"]);
            Assert.Equal("~3.2.1", entry.Dependencies["pkg-c"]);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void Load_NoLockFileInDirectory_ReturnsNull()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            var result = LockFile.Load(tmpDir);

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
        string nonexistent = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));

        var result = LockFile.Load(nonexistent);

        Assert.Null(result);
    }

    [Fact]
    public void Load_MalformedJson_ThrowsInvalidDataException()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "stash-lock.json"), "{ not valid json {{");

            Assert.Throws<InvalidDataException>(() => LockFile.Load(tmpDir));
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    // ── Save ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Save_RoundTrip_PreservesAllData()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            var lockFile = new LockFile
            {
                LockVersion = 3,
                Stash = ">=1.0.0",
                Resolved = new Dictionary<string, LockFileEntry>
                {
                    ["foo@1.2.3"] = new LockFileEntry
                    {
                        Version = "1.2.3",
                        Resolved = "https://example.com/foo-1.2.3.tar.gz",
                        Integrity = "sha256-xyz==",
                        Dependencies = new Dictionary<string, string>
                        {
                            ["bar"] = "^2.0.0"
                        }
                    }
                }
            };

            lockFile.Save(tmpDir);
            var loaded = LockFile.Load(tmpDir);

            Assert.NotNull(loaded);
            Assert.Equal(3, loaded.LockVersion);
            Assert.Equal(">=1.0.0", loaded.Stash);
            Assert.Single(loaded.Resolved);
            var entry = loaded.Resolved["foo@1.2.3"];
            Assert.Equal("1.2.3", entry.Version);
            Assert.Equal("https://example.com/foo-1.2.3.tar.gz", entry.Resolved);
            Assert.Equal("sha256-xyz==", entry.Integrity);
            Assert.NotNull(entry.Dependencies);
            Assert.Equal("^2.0.0", entry.Dependencies["bar"]);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void Save_DeterministicOutput_SortedKeys()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            var lockFile = new LockFile
            {
                LockVersion = 1,
                Resolved = new Dictionary<string, LockFileEntry>
                {
                    ["beta@1.0.0"] = new LockFileEntry { Version = "1.0.0" },
                    ["alpha@2.0.0"] = new LockFileEntry { Version = "2.0.0" }
                }
            };

            lockFile.Save(tmpDir);

            string json = File.ReadAllText(Path.Combine(tmpDir, "stash-lock.json"));
            int alphaPos = json.IndexOf("alpha@2.0.0", StringComparison.Ordinal);
            int betaPos = json.IndexOf("beta@1.0.0", StringComparison.Ordinal);
            Assert.True(alphaPos < betaPos, "Resolved keys must be sorted alphabetically");
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void Save_Output_UsesTwoSpaceIndentation()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            var lockFile = new LockFile
            {
                LockVersion = 1,
                Resolved = new Dictionary<string, LockFileEntry>
                {
                    ["lib@1.0.0"] = new LockFileEntry { Version = "1.0.0" }
                }
            };

            lockFile.Save(tmpDir);

            string json = File.ReadAllText(Path.Combine(tmpDir, "stash-lock.json"));
            // Lines inside the root object must start with exactly two spaces
            string[] lines = json.Split('\n');
            var indentedLines = lines.Where(l => l.Length > 0 && l[0] == ' ').ToList();
            Assert.All(indentedLines, line => Assert.StartsWith("  ", line));
            // No four-space (or more) leading indent on top-level keys
            var topLevelKeys = lines.Where(l => l.StartsWith("  \"", StringComparison.Ordinal) && !l.StartsWith("    ", StringComparison.Ordinal)).ToList();
            Assert.NotEmpty(topLevelKeys);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void Save_NullOptionalFields_OmittedFromJson()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            var lockFile = new LockFile
            {
                LockVersion = 1,
                Stash = null,
                Resolved = new Dictionary<string, LockFileEntry>
                {
                    ["pkg@1.0.0"] = new LockFileEntry
                    {
                        Version = "1.0.0",
                        Resolved = null,
                        Integrity = null,
                        Dependencies = null
                    }
                }
            };

            lockFile.Save(tmpDir);

            string json = File.ReadAllText(Path.Combine(tmpDir, "stash-lock.json"));
            Assert.DoesNotContain("\"stash\"", json, StringComparison.Ordinal);
            Assert.DoesNotContain("\"resolved\": null", json, StringComparison.Ordinal);
            Assert.DoesNotContain("\"integrity\"", json, StringComparison.Ordinal);
            Assert.DoesNotContain("\"dependencies\"", json, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    // ── Deterministic Output ──────────────────────────────────────────────────

    [Fact]
    public void Save_ResolvedEntries_SortedAlphabetically()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            var lockFile = new LockFile
            {
                LockVersion = 1,
                Resolved = new Dictionary<string, LockFileEntry>
                {
                    ["zoo@1.0.0"] = new LockFileEntry { Version = "1.0.0" },
                    ["alpha@1.0.0"] = new LockFileEntry { Version = "1.0.0" },
                    ["mango@1.0.0"] = new LockFileEntry { Version = "1.0.0" }
                }
            };

            lockFile.Save(tmpDir);

            string json = File.ReadAllText(Path.Combine(tmpDir, "stash-lock.json"));
            int alphaPos = json.IndexOf("alpha@1.0.0", StringComparison.Ordinal);
            int mangoPos = json.IndexOf("mango@1.0.0", StringComparison.Ordinal);
            int zooPos = json.IndexOf("zoo@1.0.0", StringComparison.Ordinal);
            Assert.True(alphaPos < mangoPos, "alpha must appear before mango");
            Assert.True(mangoPos < zooPos, "mango must appear before zoo");
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void Save_DependenciesWithinEntry_SortedAlphabetically()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            var lockFile = new LockFile
            {
                LockVersion = 1,
                Resolved = new Dictionary<string, LockFileEntry>
                {
                    ["pkg@1.0.0"] = new LockFileEntry
                    {
                        Version = "1.0.0",
                        Dependencies = new Dictionary<string, string>
                        {
                            ["zebra-dep"] = "^1.0.0",
                            ["aardvark-dep"] = "^2.0.0",
                            ["meerkat-dep"] = "^3.0.0"
                        }
                    }
                }
            };

            lockFile.Save(tmpDir);

            string json = File.ReadAllText(Path.Combine(tmpDir, "stash-lock.json"));
            int aardvarkPos = json.IndexOf("aardvark-dep", StringComparison.Ordinal);
            int meerkatPos = json.IndexOf("meerkat-dep", StringComparison.Ordinal);
            int zebraPos = json.IndexOf("zebra-dep", StringComparison.Ordinal);
            Assert.True(aardvarkPos < meerkatPos, "aardvark-dep must appear before meerkat-dep");
            Assert.True(meerkatPos < zebraPos, "meerkat-dep must appear before zebra-dep");
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void Save_MultipleSaves_ProduceIdenticalOutput()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            var lockFile = new LockFile
            {
                LockVersion = 1,
                Stash = ">=0.1.0",
                Resolved = new Dictionary<string, LockFileEntry>
                {
                    ["bravo@1.0.0"] = new LockFileEntry { Version = "1.0.0", Resolved = "https://example.com/b.tar.gz" },
                    ["alpha@2.0.0"] = new LockFileEntry { Version = "2.0.0", Resolved = "https://example.com/a.tar.gz" }
                }
            };

            lockFile.Save(tmpDir);
            byte[] first = File.ReadAllBytes(Path.Combine(tmpDir, "stash-lock.json"));

            lockFile.Save(tmpDir);
            byte[] second = File.ReadAllBytes(Path.Combine(tmpDir, "stash-lock.json"));

            Assert.Equal(first, second);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    // ── Integrity ─────────────────────────────────────────────────────────────

    [Fact]
    public void ComputeIntegrity_ReturnsSha256PrefixedString()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            string filePath = Path.Combine(tmpDir, "pkg.tar.gz");
            File.WriteAllText(filePath, "dummy content");

            string integrity = LockFile.ComputeIntegrity(filePath);

            Assert.StartsWith("sha256-", integrity, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void ComputeIntegrity_SameContent_ProducesConsistentHash()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            string filePath = Path.Combine(tmpDir, "file.bin");
            byte[] content = System.Text.Encoding.UTF8.GetBytes("hello stash");
            File.WriteAllBytes(filePath, content);

            string first = LockFile.ComputeIntegrity(filePath);
            string second = LockFile.ComputeIntegrity(filePath);

            Assert.Equal(first, second);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void VerifyIntegrity_MatchingHash_ReturnsTrue()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            string filePath = Path.Combine(tmpDir, "pkg.tar.gz");
            File.WriteAllBytes(filePath, new byte[] { 1, 2, 3, 4, 5 });

            string integrity = LockFile.ComputeIntegrity(filePath);
            bool result = LockFile.VerifyIntegrity(filePath, integrity);

            Assert.True(result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void VerifyIntegrity_MismatchedHash_ReturnsFalse()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            string filePath = Path.Combine(tmpDir, "pkg.tar.gz");
            File.WriteAllBytes(filePath, new byte[] { 1, 2, 3, 4, 5 });

            bool result = LockFile.VerifyIntegrity(filePath, "sha256-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=");

            Assert.False(result);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    // ── Edge Cases ────────────────────────────────────────────────────────────

    [Fact]
    public void SaveLoad_EmptyResolvedMap_RoundTripsSuccessfully()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            var lockFile = new LockFile
            {
                LockVersion = 1,
                Resolved = new Dictionary<string, LockFileEntry>()
            };

            lockFile.Save(tmpDir);
            var loaded = LockFile.Load(tmpDir);

            Assert.NotNull(loaded);
            Assert.Equal(1, loaded.LockVersion);
            Assert.Empty(loaded.Resolved);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void SaveLoad_EntryWithNoDependenciesAndNoResolvedUrl_RoundTripsSuccessfully()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), "stash_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            var lockFile = new LockFile
            {
                LockVersion = 1,
                Resolved = new Dictionary<string, LockFileEntry>
                {
                    ["local-pkg@0.1.0"] = new LockFileEntry
                    {
                        Version = "0.1.0",
                        Resolved = null,
                        Integrity = null,
                        Dependencies = null
                    }
                }
            };

            lockFile.Save(tmpDir);
            var loaded = LockFile.Load(tmpDir);

            Assert.NotNull(loaded);
            var entry = loaded.Resolved["local-pkg@0.1.0"];
            Assert.Equal("0.1.0", entry.Version);
            Assert.Null(entry.Resolved);
            Assert.Null(entry.Integrity);
            Assert.Null(entry.Dependencies);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }
}
