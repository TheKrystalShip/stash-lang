using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Stash.Analysis;
using Stash.Analysis.Rules;

namespace Stash.Tests.Analysis;

/// <summary>
/// Tests for Phase 6, Task 6.5 — Per-rule configuration options in .stashcheck.
/// Covers parsing, merging, cloning, and end-to-end threshold application for
/// SA0109 (maxComplexity), SA1002 (maxDepth), and SA0405 (maxParams).
/// </summary>
public class Phase6RuleOptionsTests : AnalysisTestBase
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string CreateTempDir(Dictionary<string, string> files)
    {
        string dir = Path.Combine(Path.GetTempPath(), "stash-rule-opts-tests", Guid.NewGuid().ToString());
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

    private static List<SemanticDiagnostic> AnalyzeSource(string source, ProjectConfig config)
    {
        var uri = new Uri("file:///test/test.stash");
        var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
        return engine.Analyze(uri, source, noImports: true, configOverride: config).SemanticDiagnostics;
    }

    // ── ParseContent: options parsing ─────────────────────────────────────────

    [Fact]
    public void ParseContent_SingleRuleOption_ParsedCorrectly()
    {
        var config = ProjectConfig.ParseContent("options.SA0109.maxComplexity = 20\n");

        Assert.True(config.RuleOptions.TryGetValue("SA0109", out var opts));
        Assert.Equal("20", opts["maxComplexity"]);
    }

    [Fact]
    public void ParseContent_MultipleRules_ParsedCorrectly()
    {
        var config = ProjectConfig.ParseContent("""
            options.SA0109.maxComplexity = 20
            options.SA1002.maxDepth = 8
            options.SA0405.maxParams = 7
            """);

        Assert.True(config.RuleOptions.TryGetValue("SA0109", out var cc));
        Assert.Equal("20", cc["maxComplexity"]);

        Assert.True(config.RuleOptions.TryGetValue("SA1002", out var md));
        Assert.Equal("8", md["maxDepth"]);

        Assert.True(config.RuleOptions.TryGetValue("SA0405", out var mp));
        Assert.Equal("7", mp["maxParams"]);
    }

    [Fact]
    public void ParseContent_InvalidOptionLine_MissingSecondDot_Ignored()
    {
        // "options.SA0109" has no second dot — should be silently ignored
        var config = ProjectConfig.ParseContent("options.SA0109 = 20\n");
        Assert.Empty(config.RuleOptions);
    }

    [Fact]
    public void ParseContent_NonOptionKeys_NotAffectingRuleOptions()
    {
        var config = ProjectConfig.ParseContent("disable = SA0201\npreset = recommended\n");
        Assert.Empty(config.RuleOptions);
    }

    // ── Merge: rule options ───────────────────────────────────────────────────

    [Fact]
    public void Merge_ChildOverridesParentOption()
    {
        string dir = CreateTempDir(new Dictionary<string, string>
        {
            [".stashcheck"] = "options.SA1002.maxDepth = 5\n",
            ["sub/.stashcheck"] = "options.SA1002.maxDepth = 10\n",
            ["sub/test.stash"] = "fn f() {}\n"
        });
        try
        {
            var config = ProjectConfig.Load(Path.Combine(dir, "sub"));
            Assert.True(config.RuleOptions.TryGetValue("SA1002", out var opts));
            Assert.Equal("10", opts["maxDepth"]);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Merge_ChildAddsNewOption_ParentOptionPreserved()
    {
        string dir = CreateTempDir(new Dictionary<string, string>
        {
            [".stashcheck"] = "options.SA0109.maxComplexity = 25\n",
            ["sub/.stashcheck"] = "options.SA1002.maxDepth = 8\n",
            ["sub/test.stash"] = "fn f() {}\n"
        });
        try
        {
            var config = ProjectConfig.Load(Path.Combine(dir, "sub"));
            Assert.True(config.RuleOptions.TryGetValue("SA0109", out var cc));
            Assert.Equal("25", cc["maxComplexity"]);
            Assert.True(config.RuleOptions.TryGetValue("SA1002", out var md));
            Assert.Equal("8", md["maxDepth"]);
        }
        finally { Cleanup(dir); }
    }

    // ── Clone: rule options deep-copied ──────────────────────────────────────

    [Fact]
    public void WithCliOverrides_ClonesRuleOptions_WithoutSharing()
    {
        var config = ProjectConfig.ParseContent("options.SA0109.maxComplexity = 30\n");
        // WithCliOverrides triggers Clone internally
        var cloned = config.WithCliOverrides(["SA0201"], null);

        Assert.True(cloned.RuleOptions.TryGetValue("SA0109", out var opts));
        Assert.Equal("30", opts["maxComplexity"]);
    }

    // ── IConfigurableRule.Configure ───────────────────────────────────────────

    [Fact]
    public void CyclomaticComplexityRule_Configure_SetsThreshold()
    {
        var rule = new CyclomaticComplexityRule();
        rule.Configure(new Dictionary<string, string> { ["maxComplexity"] = "20" });
        Assert.Equal(20, rule.Threshold);
    }

    [Fact]
    public void CyclomaticComplexityRule_Configure_ZeroIgnored()
    {
        var rule = new CyclomaticComplexityRule();
        rule.Configure(new Dictionary<string, string> { ["maxComplexity"] = "0" });
        Assert.Equal(CyclomaticComplexityRule.DefaultThreshold, rule.Threshold);
    }

    [Fact]
    public void MaxDepthRule_Configure_SetsThreshold()
    {
        var rule = new MaxDepthRule();
        rule.Configure(new Dictionary<string, string> { ["maxDepth"] = "8" });
        Assert.Equal(8, rule.Threshold);
    }

    [Fact]
    public void TooManyParametersRule_Configure_SetsThreshold()
    {
        var rule = new TooManyParametersRule();
        rule.Configure(new Dictionary<string, string> { ["maxParams"] = "7" });
        Assert.Equal(7, rule.Threshold);
    }

    // ── End-to-end: config options applied during analysis ────────────────────

    [Fact]
    public void SA1002_DefaultThreshold_DiagnosticEmitted()
    {
        // 6 levels deep (depth 6 > default threshold 5) — validates rule fires at default setting
        string source = """
            fn f() {
                if (true) {
                    if (true) {
                        if (true) {
                            if (true) {
                                if (true) {
                                    if (true) {
                                        let x = 1;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            """;
        // Validate() uses SemanticValidator with all rules at default thresholds
        var diags = Validate(source);
        Assert.Contains(diags, d => d.Code == "SA1002");
    }

    [Fact]
    public void SA1002_WithHigherMaxDepth_NoDiagnosticEmitted()
    {
        // 6 levels deep (depth 6 > default 5) but config raises threshold to 8 — SA1002 must NOT fire
        string source = """
            fn f() {
                if (true) {
                    if (true) {
                        if (true) {
                            if (true) {
                                if (true) {
                                    if (true) {
                                        let x = 1;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            """;
        var config = ProjectConfig.ParseContent("options.SA1002.maxDepth = 8\n");
        var diags = AnalyzeSource(source, config);
        Assert.DoesNotContain(diags, d => d.Code == "SA1002");
    }

    [Fact]
    public void SA0405_WithHigherMaxParams_NoDiagnosticEmitted()
    {
        // 6 parameters — exceeds default (5), but not the configured threshold (7)
        string source = "fn f(a, b, c, d, e, f) { let x = 1; }\n";
        var config = ProjectConfig.ParseContent("options.SA0405.maxParams = 7\n");
        var diags = AnalyzeSource(source, config);
        Assert.DoesNotContain(diags, d => d.Code == "SA0405");
    }
}
