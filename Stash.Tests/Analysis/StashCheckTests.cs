using System.Text.Json;
using Stash.Analysis;
using Stash.Check;

namespace Stash.Tests.Analysis;

public class StashCheckTests
{
    // ── Helper: Create temp .stash files for testing ─────────────────

    private static string CreateTempStashFile(string content, string? fileName = null)
    {
        string dir = Path.Combine(Path.GetTempPath(), "stash-check-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, fileName ?? "test.stash");
        File.WriteAllText(file, content);
        return file;
    }

    private static string CreateTempDir(Dictionary<string, string> files)
    {
        string dir = Path.Combine(Path.GetTempPath(), "stash-check-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        foreach (var (name, content) in files)
        {
            string filePath = Path.Combine(dir, name);
            string? fileDir = Path.GetDirectoryName(filePath);
            if (fileDir != null) Directory.CreateDirectory(fileDir);
            File.WriteAllText(filePath, content);
        }
        return dir;
    }

    private static JsonDocument RunAndParseSarif(CheckResult result)
    {
        var formatter = new SarifFormatter("stash-check test", DateTime.UtcNow);
        using var stream = new MemoryStream();
        formatter.Write(result, stream);
        stream.Position = 0;
        return JsonDocument.Parse(stream);
    }

    // ── CheckOptions Tests ───────────────────────────────────────────

    [Fact]
    public void CheckOptions_NoArgs_DefaultsToCurrentDir()
    {
        var opts = CheckOptions.Parse(Array.Empty<string>());
        Assert.Equal("sarif", opts.Format);
        Assert.Equal("information", opts.Severity);
        Assert.False(opts.NoImports);
        Assert.False(opts.ShowVersion);
        Assert.False(opts.ShowHelp);
        Assert.Single(opts.Paths);
        Assert.Equal(".", opts.Paths[0]);
    }

    [Fact]
    public void CheckOptions_AllFlags_ParsedCorrectly()
    {
        var opts = CheckOptions.Parse(new[]
        {
            "--format", "sarif",
            "--output", "out.sarif",
            "--exclude", "**/*.test.stash",
            "--exclude", "**/vendor/**",
            "--severity", "warning",
            "--no-imports",
            "src/"
        });

        Assert.Equal("sarif", opts.Format);
        Assert.Equal("out.sarif", opts.OutputPath);
        Assert.Equal(2, opts.ExcludeGlobs.Count);
        Assert.Equal("warning", opts.Severity);
        Assert.True(opts.NoImports);
        Assert.Single(opts.Paths);
        Assert.Equal("src/", opts.Paths[0]);
    }

    [Fact]
    public void CheckOptions_Help_SetsFlag()
    {
        var opts = CheckOptions.Parse(new[] { "--help" });
        Assert.True(opts.ShowHelp);
    }

    [Fact]
    public void CheckOptions_Version_SetsFlag()
    {
        var opts = CheckOptions.Parse(new[] { "--version" });
        Assert.True(opts.ShowVersion);
    }

    [Fact]
    public void CheckOptions_ShortHelp_SetsFlag()
    {
        var opts = CheckOptions.Parse(new[] { "-h" });
        Assert.True(opts.ShowHelp);
    }

    [Fact]
    public void CheckOptions_MultiplePaths_AllCaptured()
    {
        var opts = CheckOptions.Parse(new[] { "file1.stash", "src/", "lib/" });
        Assert.Equal(3, opts.Paths.Count);
    }

    // ── CheckRunner Tests ────────────────────────────────────────────

    [Fact]
    public void CheckRunner_SingleFile_ReturnsAnalysisResult()
    {
        string file = CreateTempStashFile("break;");
        try
        {
            var opts = new CheckOptions { Paths = new List<string> { file } };
            var runner = new CheckRunner(opts);
            var result = runner.Run();

            Assert.Single(result.Files);
            Assert.True(result.TotalErrors > 0);
        }
        finally
        {
            CleanupTempFile(file);
        }
    }

    [Fact]
    public void CheckRunner_Directory_FindsAllStashFiles()
    {
        string dir = CreateTempDir(new Dictionary<string, string>
        {
            ["a.stash"] = "let x = 1;",
            ["b.stash"] = "let y = 2;",
            ["sub/c.stash"] = "let z = 3;"
        });
        try
        {
            var opts = new CheckOptions { Paths = new List<string> { dir } };
            var runner = new CheckRunner(opts);
            var files = runner.DiscoverFiles();
            Assert.Equal(3, files.Count);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void CheckRunner_EmptyFile_NoDiagnostics()
    {
        string file = CreateTempStashFile("");
        try
        {
            var opts = new CheckOptions { Paths = new List<string> { file } };
            var runner = new CheckRunner(opts);
            var result = runner.Run();

            Assert.Single(result.Files);
            Assert.Equal(0, result.TotalErrors);
            Assert.Equal(0, result.TotalWarnings);
        }
        finally
        {
            CleanupTempFile(file);
        }
    }

    [Fact]
    public void CheckRunner_CleanFile_NoDiagnostics()
    {
        string file = CreateTempStashFile("fn greet() { return \"hello\"; }");
        try
        {
            var opts = new CheckOptions { Paths = new List<string> { file } };
            var runner = new CheckRunner(opts);
            var result = runner.Run();

            Assert.Single(result.Files);
            Assert.Equal(0, result.TotalErrors);
        }
        finally
        {
            CleanupTempFile(file);
        }
    }

    [Fact]
    public void CheckRunner_ExcludeGlob_FiltersFiles()
    {
        string dir = CreateTempDir(new Dictionary<string, string>
        {
            ["main.stash"] = "let x = 1;",
            ["test.test.stash"] = "break;"
        });
        try
        {
            var savedCwd = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(dir);
            try
            {
                var opts = new CheckOptions
                {
                    Paths = new List<string> { dir },
                    ExcludeGlobs = new List<string> { "**/*.test.stash" }
                };
                var runner = new CheckRunner(opts);
                var files = runner.DiscoverFiles();
                Assert.Single(files);
                Assert.Contains("main.stash", files[0]);
            }
            finally
            {
                Directory.SetCurrentDirectory(savedCwd);
            }
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void CheckRunner_NoStashFiles_EmptyResult()
    {
        string dir = CreateTempDir(new Dictionary<string, string>
        {
            ["readme.txt"] = "not a stash file"
        });
        try
        {
            var opts = new CheckOptions { Paths = new List<string> { dir } };
            var runner = new CheckRunner(opts);
            var result = runner.Run();
            Assert.Empty(result.Files);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // ── SarifFormatter Tests ─────────────────────────────────────────

    [Fact]
    public void SarifFormatter_ValidSarif_HasCorrectStructure()
    {
        string file = CreateTempStashFile("break;");
        try
        {
            var opts = new CheckOptions { Paths = new List<string> { file } };
            var runner = new CheckRunner(opts);
            var result = runner.Run();
            using var doc = RunAndParseSarif(result);

            var root = doc.RootElement;
            Assert.Equal("2.1.0", root.GetProperty("version").GetString());
            Assert.True(root.TryGetProperty("$schema", out _));
            Assert.True(root.TryGetProperty("runs", out var runs));
            Assert.Equal(1, runs.GetArrayLength());

            var run = runs[0];
            Assert.True(run.TryGetProperty("tool", out _));
            Assert.True(run.TryGetProperty("results", out _));
            Assert.True(run.TryGetProperty("invocations", out _));
        }
        finally
        {
            CleanupTempFile(file);
        }
    }

    [Fact]
    public void SarifFormatter_DiagnosticsPresent_MapsCorrectly()
    {
        // "break;" outside a loop triggers SA0101 (error)
        string file = CreateTempStashFile("break;");
        try
        {
            var opts = new CheckOptions { Paths = new List<string> { file } };
            var runner = new CheckRunner(opts);
            var result = runner.Run();
            using var doc = RunAndParseSarif(result);

            var results = doc.RootElement.GetProperty("runs")[0].GetProperty("results");
            bool foundBreakDiag = false;
            foreach (var r in results.EnumerateArray())
            {
                if (r.GetProperty("ruleId").GetString() == "SA0101")
                {
                    foundBreakDiag = true;
                    Assert.Equal("error", r.GetProperty("level").GetString());
                    Assert.Contains("break", r.GetProperty("message").GetProperty("text").GetString()!);

                    // Verify location exists
                    var loc = r.GetProperty("locations")[0].GetProperty("physicalLocation");
                    Assert.True(loc.TryGetProperty("region", out var region));
                    Assert.True(region.GetProperty("startLine").GetInt32() >= 1);
                }
            }
            Assert.True(foundBreakDiag, "Expected SA0101 diagnostic for 'break' outside loop");
        }
        finally
        {
            CleanupTempFile(file);
        }
    }

    [Fact]
    public void SarifFormatter_AllRulesIncluded_InToolDriver()
    {
        string file = CreateTempStashFile("");
        try
        {
            var opts = new CheckOptions { Paths = new List<string> { file } };
            var runner = new CheckRunner(opts);
            var result = runner.Run();
            using var doc = RunAndParseSarif(result);

            var rules = doc.RootElement.GetProperty("runs")[0]
                .GetProperty("tool").GetProperty("driver").GetProperty("rules");

            // Should have STASH001, STASH002, plus all DiagnosticDescriptors
            int expectedCount = DiagnosticDescriptors.AllByCode.Count + 2; // +2 for STASH001 and STASH002
            Assert.Equal(expectedCount, rules.GetArrayLength());

            // Verify STASH001 and STASH002 are present
            var ruleIds = new HashSet<string>();
            foreach (var rule in rules.EnumerateArray())
            {
                ruleIds.Add(rule.GetProperty("id").GetString()!);
            }
            Assert.Contains("STASH001", ruleIds);
            Assert.Contains("STASH002", ruleIds);
        }
        finally
        {
            CleanupTempFile(file);
        }
    }

    [Fact]
    public void SarifFormatter_LexErrors_MappedAsSTASH001()
    {
        // Unterminated string should cause a lex error
        string file = CreateTempStashFile("let x = \"unterminated;");
        try
        {
            var opts = new CheckOptions { Paths = new List<string> { file } };
            var runner = new CheckRunner(opts);
            var result = runner.Run();
            using var doc = RunAndParseSarif(result);

            var results = doc.RootElement.GetProperty("runs")[0].GetProperty("results");
            bool foundLexError = false;
            foreach (var r in results.EnumerateArray())
            {
                if (r.GetProperty("ruleId").GetString() == "STASH001")
                {
                    foundLexError = true;
                    Assert.Equal("error", r.GetProperty("level").GetString());
                }
            }
            Assert.True(foundLexError, "Expected STASH001 lex error for unterminated string");
        }
        finally
        {
            CleanupTempFile(file);
        }
    }

    [Fact]
    public void SarifFormatter_ParseErrors_MappedAsSTASH002()
    {
        // Missing semicolon should cause a parse error
        string file = CreateTempStashFile("let x = ");
        try
        {
            var opts = new CheckOptions { Paths = new List<string> { file } };
            var runner = new CheckRunner(opts);
            var result = runner.Run();
            using var doc = RunAndParseSarif(result);

            var results = doc.RootElement.GetProperty("runs")[0].GetProperty("results");
            bool foundParseError = false;
            foreach (var r in results.EnumerateArray())
            {
                if (r.GetProperty("ruleId").GetString() == "STASH002")
                {
                    foundParseError = true;
                    Assert.Equal("error", r.GetProperty("level").GetString());
                }
            }
            Assert.True(foundParseError, "Expected STASH002 parse error for incomplete statement");
        }
        finally
        {
            CleanupTempFile(file);
        }
    }

    [Fact]
    public void SarifFormatter_RelativePaths_UseForwardSlashes()
    {
        string file = CreateTempStashFile("break;");
        try
        {
            var savedCwd = Directory.GetCurrentDirectory();
            string? dir = Path.GetDirectoryName(file);
            if (dir != null) Directory.SetCurrentDirectory(dir);
            try
            {
                var opts = new CheckOptions { Paths = new List<string> { file } };
                var runner = new CheckRunner(opts);
                var result = runner.Run();
                using var doc = RunAndParseSarif(result);

                var results = doc.RootElement.GetProperty("runs")[0].GetProperty("results");
                foreach (var r in results.EnumerateArray())
                {
                    if (r.TryGetProperty("locations", out var locs))
                    {
                        foreach (var loc in locs.EnumerateArray())
                        {
                            var uri = loc.GetProperty("physicalLocation")
                                .GetProperty("artifactLocation")
                                .GetProperty("uri").GetString();
                            Assert.DoesNotContain("\\", uri);
                        }
                    }
                }
            }
            finally
            {
                Directory.SetCurrentDirectory(savedCwd);
            }
        }
        finally
        {
            CleanupTempFile(file);
        }
    }

    [Fact]
    public void SarifFormatter_Invocations_Present()
    {
        string file = CreateTempStashFile("");
        try
        {
            var opts = new CheckOptions { Paths = new List<string> { file } };
            var runner = new CheckRunner(opts);
            var result = runner.Run();
            using var doc = RunAndParseSarif(result);

            var invocations = doc.RootElement.GetProperty("runs")[0].GetProperty("invocations");
            Assert.Equal(1, invocations.GetArrayLength());
            var inv = invocations[0];
            Assert.True(inv.GetProperty("executionSuccessful").GetBoolean());
            Assert.True(inv.TryGetProperty("commandLine", out _));
            Assert.True(inv.TryGetProperty("startTimeUtc", out _));
            Assert.True(inv.TryGetProperty("endTimeUtc", out _));
        }
        finally
        {
            CleanupTempFile(file);
        }
    }

    [Fact]
    public void SarifFormatter_OriginalUriBaseIds_Present()
    {
        string file = CreateTempStashFile("");
        try
        {
            var opts = new CheckOptions { Paths = new List<string> { file } };
            var runner = new CheckRunner(opts);
            var result = runner.Run();
            using var doc = RunAndParseSarif(result);

            var run = doc.RootElement.GetProperty("runs")[0];
            Assert.True(run.TryGetProperty("originalUriBaseIds", out var baseIds));
            Assert.True(baseIds.TryGetProperty("%SRCROOT%", out var srcRoot));
            Assert.True(srcRoot.TryGetProperty("uri", out var uri));
            Assert.StartsWith("file://", uri.GetString());
        }
        finally
        {
            CleanupTempFile(file);
        }
    }

    // ── Program Tests ────────────────────────────────────────────────

    [Fact]
    public void Program_Help_ReturnsZero()
    {
        int exitCode = Stash.Check.Program.Main(new[] { "--help" });
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Program_Version_ReturnsZero()
    {
        int exitCode = Stash.Check.Program.Main(new[] { "--version" });
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Program_InvalidFormat_ReturnsTwo()
    {
        int exitCode = Stash.Check.Program.Main(new[] { "--format", "xml", "." });
        Assert.Equal(2, exitCode);
    }

    [Fact]
    public void Program_CleanFile_ReturnsZero()
    {
        string file = CreateTempStashFile("fn test() { return 1; }");
        try
        {
            int exitCode = Stash.Check.Program.Main(new[] { "--output", "/dev/null", file });
            Assert.Equal(0, exitCode);
        }
        finally
        {
            CleanupTempFile(file);
        }
    }

    [Fact]
    public void Program_FileWithErrors_ReturnsOne()
    {
        string file = CreateTempStashFile("break;");
        try
        {
            int exitCode = Stash.Check.Program.Main(new[] { "--output", "/dev/null", file });
            Assert.Equal(1, exitCode);
        }
        finally
        {
            CleanupTempFile(file);
        }
    }

    // ── Cleanup helper ───────────────────────────────────────────────

    private static void CleanupTempFile(string file)
    {
        try
        {
            string? dir = Path.GetDirectoryName(file);
            // Clean up to the test session dir
            if (dir != null && dir.Contains("stash-check-tests"))
            {
                Directory.Delete(dir, true);
            }
        }
        catch { /* Best effort */ }
    }
}
