using System.IO;
using System.Text;
using Stash.Analysis;
using Stash.Check;
using Microsoft.Extensions.Logging.Abstractions;

namespace Stash.Tests.Analysis;

/// <summary>
/// Tests for Phase 8 Task 8.6 — Domain-Based Rule Grouping:
/// parsing [domains] section, applying domain profiles to matching files,
/// and merging domain configs across hierarchical .stashcheck files.
/// </summary>
public class DomainConfigTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string CreateTempDir(Dictionary<string, string> files)
    {
        string dir = Path.Combine(Path.GetTempPath(), "stash-domain-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        foreach (var (name, content) in files)
        {
            string filePath = Path.Combine(dir, name);
            string? fileDir = Path.GetDirectoryName(filePath);
            if (fileDir != null) Directory.CreateDirectory(fileDir);
            File.WriteAllText(filePath, content, Encoding.UTF8);
        }
        return dir;
    }

    private static void Cleanup(string dir)
    {
        try { Directory.Delete(dir, true); } catch { /* best effort */ }
    }

    private static List<SemanticDiagnostic> AnalyzeFile(string filePath, ProjectConfig? config = null)
    {
        string source = File.ReadAllText(filePath);
        var uri = new Uri(filePath);
        var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
        var result = engine.Analyze(uri, source, noImports: true, configOverride: config);
        return result.SemanticDiagnostics;
    }

    // ── Parsing ───────────────────────────────────────────────────────────────

    [Fact]
    public void DomainParsing_RecommendedProfile_IsStored()
    {
        string content = "[domains]\ntest = recommended\n";
        var config = ProjectConfig.ParseContent(content);

        Assert.Single(config.Domains);
        Assert.True(config.Domains.TryGetValue("test", out string? profile));
        Assert.Equal("recommended", profile);
    }

    [Fact]
    public void DomainParsing_StrictProfile_IsStored()
    {
        string content = "[domains]\ntest = strict\n";
        var config = ProjectConfig.ParseContent(content);

        Assert.True(config.Domains.TryGetValue("test", out string? profile));
        Assert.Equal("strict", profile);
    }

    [Fact]
    public void DomainParsing_OffProfile_IsStored()
    {
        string content = "[domains]\ntest = off\n";
        var config = ProjectConfig.ParseContent(content);

        Assert.True(config.Domains.TryGetValue("test", out string? profile));
        Assert.Equal("off", profile);
    }

    [Fact]
    public void DomainParsing_UnknownProfile_IsIgnored()
    {
        string content = "[domains]\ntest = ultra\n";
        var config = ProjectConfig.ParseContent(content);

        Assert.Empty(config.Domains);
    }

    [Fact]
    public void DomainParsing_MultipleDomains_AllStored()
    {
        string content = "[domains]\ntest = recommended\nscripts = strict\n";
        var config = ProjectConfig.ParseContent(content);

        Assert.Equal(2, config.Domains.Count);
        Assert.Equal("recommended", config.Domains["test"]);
        Assert.Equal("strict", config.Domains["scripts"]);
    }

    [Fact]
    public void DomainParsing_Comments_Ignored()
    {
        string content = "[domains]\n# This is a comment\ntest = recommended\n";
        var config = ProjectConfig.ParseContent(content);

        Assert.Single(config.Domains);
        Assert.Equal("recommended", config.Domains["test"]);
    }

    [Fact]
    public void DomainParsing_CaseInsensitiveDomainName_Normalized()
    {
        string content = "[domains]\nTEST = recommended\n";
        var config = ProjectConfig.ParseContent(content);

        Assert.True(config.Domains.ContainsKey("test"));
    }

    // ── DomainRegistry ────────────────────────────────────────────────────────

    [Fact]
    public void DomainRegistry_GetDomain_Off_ReturnsNull()
    {
        var domain = DomainRegistry.GetDomain("test", "off");
        Assert.Null(domain);
    }

    [Fact]
    public void DomainRegistry_GetDomain_UnknownName_ReturnsNull()
    {
        var domain = DomainRegistry.GetDomain("unknown", "recommended");
        Assert.Null(domain);
    }

    [Fact]
    public void DomainRegistry_GetDomain_TestRecommended_HasExpectedPatterns()
    {
        var domain = DomainRegistry.GetDomain("test", "recommended");

        Assert.NotNull(domain);
        Assert.Equal("test", domain.Name);
        Assert.Equal("recommended", domain.Profile);
        Assert.Contains("**/*.test.stash", domain.FilePatterns);
        Assert.Contains("**/*.spec.stash", domain.FilePatterns);
    }

    [Fact]
    public void DomainRegistry_GetDomain_TestRecommended_SA0206Disabled()
    {
        var domain = DomainRegistry.GetDomain("test", "recommended");

        Assert.NotNull(domain);
        Assert.Contains("SA0206", domain.DisabledCodes);
    }

    [Fact]
    public void DomainRegistry_GetDomain_TestStrict_SA0206NotDisabled()
    {
        var domain = DomainRegistry.GetDomain("test", "strict");

        Assert.NotNull(domain);
        Assert.DoesNotContain("SA0206", domain.DisabledCodes);
    }

    [Fact]
    public void DomainRegistry_GetDomain_ScriptsRecommended_SA0201And0206Disabled()
    {
        var domain = DomainRegistry.GetDomain("scripts", "recommended");

        Assert.NotNull(domain);
        Assert.Contains("SA0201", domain.DisabledCodes);
        Assert.Contains("SA0206", domain.DisabledCodes);
    }

    // ── Application ───────────────────────────────────────────────────────────

    [Fact]
    public void DomainApplication_TestRecommended_SA0206SuppressedInTestFile()
    {
        string dir = CreateTempDir(new Dictionary<string, string>
        {
            [".stashcheck"] = "[domains]\ntest = recommended\n",
            // Function with unused parameter — triggers SA0206
            ["helpers.test.stash"] = "fn greet(name) {\n  return \"hello\";\n}\ngreet(\"world\");\n"
        });
        try
        {
            string testFile = Path.Combine(dir, "helpers.test.stash");
            var config = ProjectConfig.Load(dir);
            var diags = AnalyzeFile(testFile, config);

            // SA0206 (unused parameter 'name') should be suppressed in .test.stash with recommended
            Assert.DoesNotContain(diags, d => d.Code == "SA0206");
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void DomainApplication_TestRecommended_SA0206NotSuppressedInRegularFile()
    {
        string dir = CreateTempDir(new Dictionary<string, string>
        {
            [".stashcheck"] = "[domains]\ntest = recommended\n",
            ["helpers.stash"] = "fn greet(name) {\n  return \"hello\";\n}\ngreet(\"world\");\n"
        });
        try
        {
            string regularFile = Path.Combine(dir, "helpers.stash");
            var config = ProjectConfig.Load(dir);
            var diags = AnalyzeFile(regularFile, config);

            // SA0206 should still appear in non-test files
            Assert.Contains(diags, d => d.Code == "SA0206");
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void DomainApplication_TestStrict_SA0206NotSuppressedInTestFile()
    {
        string dir = CreateTempDir(new Dictionary<string, string>
        {
            [".stashcheck"] = "[domains]\ntest = strict\n",
            ["helpers.test.stash"] = "fn greet(name) {\n  return \"hello\";\n}\ngreet(\"world\");\n"
        });
        try
        {
            string testFile = Path.Combine(dir, "helpers.test.stash");
            var config = ProjectConfig.Load(dir);
            var diags = AnalyzeFile(testFile, config);

            // Strict profile — SA0206 is NOT relaxed; it should appear
            Assert.Contains(diags, d => d.Code == "SA0206");
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void DomainApplication_TestOff_SA0206NotSuppressedInTestFile()
    {
        string dir = CreateTempDir(new Dictionary<string, string>
        {
            [".stashcheck"] = "[domains]\ntest = off\n",
            ["helpers.test.stash"] = "fn greet(name) {\n  return \"hello\";\n}\ngreet(\"world\");\n"
        });
        try
        {
            string testFile = Path.Combine(dir, "helpers.test.stash");
            var config = ProjectConfig.Load(dir);
            var diags = AnalyzeFile(testFile, config);

            // Domain is off — no suppression active
            Assert.Contains(diags, d => d.Code == "SA0206");
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void DomainApplication_SpecFiles_AlsoMatchTestDomain()
    {
        string dir = CreateTempDir(new Dictionary<string, string>
        {
            [".stashcheck"] = "[domains]\ntest = recommended\n",
            ["helpers.spec.stash"] = "fn greet(name) {\n  return \"hello\";\n}\ngreet(\"world\");\n"
        });
        try
        {
            string specFile = Path.Combine(dir, "helpers.spec.stash");
            var config = ProjectConfig.Load(dir);
            var diags = AnalyzeFile(specFile, config);

            // .spec.stash also matches the test domain
            Assert.DoesNotContain(diags, d => d.Code == "SA0206");
        }
        finally { Cleanup(dir); }
    }

    // ── Merging ───────────────────────────────────────────────────────────────

    [Fact]
    public void DomainMerging_ChildOverridesParentProfile()
    {
        string dir = CreateTempDir(new Dictionary<string, string>
        {
            [".stashcheck"] = "[domains]\ntest = recommended\n",
            ["sub/.stashcheck"] = "[domains]\ntest = strict\n",
            ["sub/helpers.test.stash"] = "fn greet(name) {\n  return \"hello\";\n}\ngreet(\"world\");\n"
        });
        try
        {
            var config = ProjectConfig.Load(Path.Combine(dir, "sub"));

            // Child's 'strict' profile overrides parent's 'recommended'
            Assert.Equal("strict", config.Domains["test"]);

            string testFile = Path.Combine(dir, "sub", "helpers.test.stash");
            var diags = AnalyzeFile(testFile, config);

            // Strict: SA0206 not suppressed
            Assert.Contains(diags, d => d.Code == "SA0206");
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void DomainMerging_ChildAddsNewDomain()
    {
        string dir = CreateTempDir(new Dictionary<string, string>
        {
            [".stashcheck"] = "[domains]\ntest = recommended\n",
            ["sub/.stashcheck"] = "[domains]\nscripts = strict\n",
        });
        try
        {
            var config = ProjectConfig.Load(Path.Combine(dir, "sub"));

            // Both domains present after merge
            Assert.Equal("recommended", config.Domains["test"]);
            Assert.Equal("strict", config.Domains["scripts"]);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void DomainMerging_ChildTurnsOffParentDomain()
    {
        string dir = CreateTempDir(new Dictionary<string, string>
        {
            [".stashcheck"] = "[domains]\ntest = recommended\n",
            ["sub/.stashcheck"] = "[domains]\ntest = off\n",
            ["sub/helpers.test.stash"] = "fn greet(name) {\n  return \"hello\";\n}\ngreet(\"world\");\n"
        });
        try
        {
            var config = ProjectConfig.Load(Path.Combine(dir, "sub"));
            Assert.Equal("off", config.Domains["test"]);

            string testFile = Path.Combine(dir, "sub", "helpers.test.stash");
            var diags = AnalyzeFile(testFile, config);

            // Domain is off — SA0206 appears
            Assert.Contains(diags, d => d.Code == "SA0206");
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void DomainParsing_DomainsAndPerFileOverrides_CoexistCorrectly()
    {
        string content =
            "[per-file-overrides]\n\"**/generated/**\" = disable ALL\n" +
            "[domains]\ntest = recommended\n";
        var config = ProjectConfig.ParseContent(content);

        Assert.Single(config.PerFileOverrides);
        Assert.True(config.PerFileOverrides.ContainsKey("**/generated/**"));
        Assert.Single(config.Domains);
        Assert.Equal("recommended", config.Domains["test"]);
    }
}
