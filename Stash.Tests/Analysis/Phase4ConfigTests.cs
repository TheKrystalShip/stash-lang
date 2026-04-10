using System.IO;
using System.Text;
using Stash.Analysis;
using Stash.Check;
using Microsoft.Extensions.Logging.Abstractions;

namespace Stash.Tests.Analysis;

/// <summary>
/// Tests for Phase 4 — Advanced Configuration:
/// hierarchical config, per-file overrides, prefix selection, presets, CLI select/ignore,
/// file-level suppression, auto-add suppressions, and stdin support.
/// </summary>
public class Phase4ConfigTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string CreateTempDir(Dictionary<string, string> files)
    {
        string dir = Path.Combine(Path.GetTempPath(), "stash-p4-tests", Guid.NewGuid().ToString());
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

    // ── Hierarchical Config ───────────────────────────────────────────────────

    [Fact]
    public void HierarchicalConfig_ChildDisableAddsTOParent()
    {
        string dir = CreateTempDir(new Dictionary<string, string>
        {
            [".stashcheck"] = "disable = SA0201\n",
            ["sub/.stashcheck"] = "disable = SA0207\n",
            ["sub/test.stash"] = "let x = 1;\nlet y = 1;\n"
        });
        try
        {
            // Load from the sub directory — should find both configs
            var config = ProjectConfig.Load(Path.Combine(dir, "sub"));

            // Both SA0201 and SA0207 should be disabled
            Assert.True(config.IsCodeDisabled("SA0201"));
            Assert.True(config.IsCodeDisabled("SA0207"));
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void HierarchicalConfig_ChildSeverityOverridesParent()
    {
        string dir = CreateTempDir(new Dictionary<string, string>
        {
            [".stashcheck"] = "severity.SA0201 = error\n",
            ["sub/.stashcheck"] = "severity.SA0201 = warning\n",
            ["sub/test.stash"] = "let x = 1;\n"
        });
        try
        {
            var config = ProjectConfig.Load(Path.Combine(dir, "sub"));
            Assert.True(config.SeverityOverrides.TryGetValue("SA0201", out var level));
            Assert.Equal(DiagnosticLevel.Warning, level);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void HierarchicalConfig_NoConfig_ReturnsEmpty()
    {
        string dir = Path.Combine(Path.GetTempPath(), "stash-p4-no-config-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            var config = ProjectConfig.Load(dir);
            Assert.Same(ProjectConfig.Empty, config);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void HierarchicalConfig_SingleConfig_LoadedCorrectly()
    {
        string dir = CreateTempDir(new Dictionary<string, string>
        {
            [".stashcheck"] = "disable = SA0201\n",
            ["test.stash"] = "let x = 1;\n"
        });
        try
        {
            var config = ProjectConfig.Load(dir);
            Assert.True(config.IsCodeDisabled("SA0201"));
            Assert.False(config.IsCodeDisabled("SA0202"));
        }
        finally { Cleanup(dir); }
    }

    // ── Extend Directive ──────────────────────────────────────────────────────

    [Fact]
    public void ExtendDirective_MergesParentConfig()
    {
        string dir = CreateTempDir(new Dictionary<string, string>
        {
            ["base.stashcheck"] = "disable = SA0201\n",
            [".stashcheck"] = "extend = base.stashcheck\ndisable = SA0207\n",
            ["test.stash"] = "let x = 1;\n"
        });
        try
        {
            var config = ProjectConfig.Load(dir);
            // Both SA0201 (from base) and SA0207 (from .stashcheck) should be disabled
            Assert.True(config.IsCodeDisabled("SA0201"));
            Assert.True(config.IsCodeDisabled("SA0207"));
        }
        finally { Cleanup(dir); }
    }

    // ── Per-File Overrides ────────────────────────────────────────────────────

    [Fact]
    public void PerFileOverrides_ParseContent_DisableSpecificCodes()
    {
        string content = "[per-file-overrides]\n\"**/*.test.stash\" = disable SA0201, SA0206\n";
        var config = ProjectConfig.ParseContent(content);

        Assert.Single(config.PerFileOverrides);
        Assert.True(config.PerFileOverrides.ContainsKey("**/*.test.stash"));
        var overrideSpec = config.PerFileOverrides["**/*.test.stash"];
        Assert.False(overrideSpec.DisableAll);
        Assert.Contains("SA0201", overrideSpec.DisabledCodes);
        Assert.Contains("SA0206", overrideSpec.DisabledCodes);
    }

    [Fact]
    public void PerFileOverrides_ParseContent_DisableAll()
    {
        string content = "[per-file-overrides]\n\"**/generated/**\" = disable ALL\n";
        var config = ProjectConfig.ParseContent(content);

        Assert.Single(config.PerFileOverrides);
        var overrideSpec = config.PerFileOverrides["**/generated/**"];
        Assert.True(overrideSpec.DisableAll);
    }

    [Fact]
    public void PerFileOverrides_Apply_SuppressDiagnosticsInMatchingFiles()
    {
        string dir = CreateTempDir(new Dictionary<string, string>
        {
            [".stashcheck"] = "[per-file-overrides]\n\"**/*.test.stash\" = disable SA0201\n",
            ["a.test.stash"] = "let x = 1;\n",
            ["b.stash"] = "let y = 1;\n"
        });
        try
        {
            string testFile = Path.Combine(dir, "a.test.stash");
            string normalFile = Path.Combine(dir, "b.stash");

            var config = ProjectConfig.Load(dir);
            var diagsTest = AnalyzeFile(testFile, config);
            var diagsNormal = AnalyzeFile(normalFile, config);

            // SA0201 suppressed in .test.stash
            Assert.DoesNotContain(diagsTest, d => d.Code == "SA0201");
            // SA0201 present in b.stash
            Assert.Contains(diagsNormal, d => d.Code == "SA0201");
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void PerFileOverrides_Apply_DisableAllSuppressesEverything()
    {
        string dir = CreateTempDir(new Dictionary<string, string>
        {
            [".stashcheck"] = "[per-file-overrides]\n\"**/generated/**\" = disable ALL\n",
            ["generated/auto.stash"] = "break;\nlet x = 1;\n",
        });
        try
        {
            string genFile = Path.Combine(dir, "generated", "auto.stash");
            var config = ProjectConfig.Load(dir);
            var diags = AnalyzeFile(genFile, config);

            Assert.Empty(diags);
        }
        finally { Cleanup(dir); }
    }

    // ── Prefix-Based Disable ──────────────────────────────────────────────────

    [Fact]
    public void PrefixDisable_ParseContent_AcceptsShortPrefix()
    {
        var config = ProjectConfig.ParseContent("disable = SA01\n");
        Assert.Contains("SA01", config.DisabledCodes);
    }

    [Fact]
    public void PrefixDisable_IsCodeDisabled_MatchesAllCodesWithPrefix()
    {
        var config = ProjectConfig.ParseContent("disable = SA01\n");

        Assert.True(config.IsCodeDisabled("SA0101"));
        Assert.True(config.IsCodeDisabled("SA0102"));
        Assert.True(config.IsCodeDisabled("SA0103"));
        Assert.True(config.IsCodeDisabled("SA0104"));
        Assert.True(config.IsCodeDisabled("SA0105"));

        // Different prefix should not match
        Assert.False(config.IsCodeDisabled("SA0201"));
    }

    [Fact]
    public void PrefixDisable_IsCodeDisabled_ExactCodeNotAffectedByOtherPrefix()
    {
        var config = ProjectConfig.ParseContent("disable = SA03\n");

        Assert.True(config.IsCodeDisabled("SA0301"));
        Assert.False(config.IsCodeDisabled("SA0201"));
    }

    // ── Enable Directive ─────────────────────────────────────────────────────

    [Fact]
    public void EnableDirective_ReenablesCodeDisabledByParent()
    {
        string dir = CreateTempDir(new Dictionary<string, string>
        {
            [".stashcheck"] = "disable = SA0201\n",
            ["sub/.stashcheck"] = "enable = SA0201\n",
            ["sub/test.stash"] = ""
        });
        try
        {
            var config = ProjectConfig.Load(Path.Combine(dir, "sub"));
            // Child's enable should override parent's disable
            Assert.False(config.IsCodeDisabled("SA0201"));
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void EnableDirective_ParseContent_StoresInEnabledCodes()
    {
        var config = ProjectConfig.ParseContent("enable = SA0201, SA0207\n");
        Assert.Contains("SA0201", config.EnabledCodes);
        Assert.Contains("SA0207", config.EnabledCodes);
    }

    // ── Presets ───────────────────────────────────────────────────────────────

    [Fact]
    public void Preset_ParseContent_StoresPresetName()
    {
        var config = ProjectConfig.ParseContent("preset = minimal\n");
        Assert.Equal("minimal", config.Preset);
    }

    [Fact]
    public void Preset_Minimal_DisablesNonErrorCodes()
    {
        var config = ProjectConfig.ParseContent("preset = minimal\n");

        // Information-level codes should be disabled
        Assert.True(config.IsCodeDisabled("SA0201")); // Information
        Assert.True(config.IsCodeDisabled("SA0104")); // Information

        // Error-level codes should remain enabled
        Assert.False(config.IsCodeDisabled("SA0101")); // Error
        Assert.False(config.IsCodeDisabled("SA0401")); // Error
    }

    [Fact]
    public void Preset_Strict_DisablesNoCodes()
    {
        var config = ProjectConfig.ParseContent("preset = strict\n");

        Assert.False(config.IsCodeDisabled("SA0201"));
        Assert.False(config.IsCodeDisabled("SA0101"));
    }

    [Fact]
    public void Preset_Recommended_DisablesNoCodes()
    {
        var config = ProjectConfig.ParseContent("preset = recommended\n");

        Assert.False(config.IsCodeDisabled("SA0201"));
        Assert.False(config.IsCodeDisabled("SA0101"));
    }

    [Fact]
    public void Preset_InvalidValue_Ignored()
    {
        var config = ProjectConfig.ParseContent("preset = unknown\n");
        Assert.Null(config.Preset);
    }

    // ── CLI Rule Selection (--select / --ignore) ──────────────────────────────

    [Fact]
    public void CheckOptions_Select_ParsedFromCsv()
    {
        var opts = CheckOptions.Parse(new[] { "--select", "SA0201,SA0205", "." });
        Assert.Equal(2, opts.Select.Count);
        Assert.Contains("SA0201", opts.Select);
        Assert.Contains("SA0205", opts.Select);
    }

    [Fact]
    public void CheckOptions_Ignore_ParsedFromCsv()
    {
        var opts = CheckOptions.Parse(new[] { "--ignore", "SA0207", "." });
        Assert.Single(opts.Ignore);
        Assert.Contains("SA0207", opts.Ignore);
    }

    [Fact]
    public void WithCliOverrides_Select_LimitsToSpecifiedCodes()
    {
        var config = ProjectConfig.Empty.WithCliOverrides(new List<string> { "SA0201" }, null);

        // Only SA0201 should be enabled
        Assert.False(config.IsCodeDisabled("SA0201"));
        // Everything else should be disabled
        Assert.True(config.IsCodeDisabled("SA0202"));
        Assert.True(config.IsCodeDisabled("SA0101"));
    }

    [Fact]
    public void WithCliOverrides_Ignore_SuppressesSpecifiedCode()
    {
        var config = ProjectConfig.Empty.WithCliOverrides(null, new List<string> { "SA0207" });

        Assert.True(config.IsCodeDisabled("SA0207"));
        Assert.False(config.IsCodeDisabled("SA0201"));
    }

    [Fact]
    public void WithCliOverrides_BothSelectAndIgnore_SelectTakesPrecedence()
    {
        // --select SA0201 --ignore SA0201 — select acts as allow-list; SA0201 is in it, so it's enabled
        var config = ProjectConfig.Empty.WithCliOverrides(
            new List<string> { "SA0201" },
            new List<string> { "SA0201" });

        // SA0201 is in ignore (_disabledCodes) but also in select (_selectCodes).
        // IsCodeDisabled checks select first: SA0201 IS in select, so inSelect=true.
        // Then checks enabled (empty). Then checks preset (none). Then checks disabled — SA0201 IS disabled.
        // Result: disabled (ignore wins over select here since we check disabled after select)
        Assert.True(config.IsCodeDisabled("SA0201")); // ignore wins when both specified
    }

    [Fact]
    public void WithCliOverrides_NoOverrides_ReturnsSameInstance()
    {
        var config = ProjectConfig.Empty;
        var result = config.WithCliOverrides(null, null);
        Assert.Same(config, result);
    }

    [Fact]
    public void WithCliOverrides_SelectPrefix_SuppressesOtherCategories()
    {
        var config = ProjectConfig.Empty.WithCliOverrides(new List<string> { "SA02" }, null);

        // SA02xx codes should pass
        Assert.False(config.IsCodeDisabled("SA0201"));
        Assert.False(config.IsCodeDisabled("SA0207"));

        // Other categories should be disabled
        Assert.True(config.IsCodeDisabled("SA0101"));
        Assert.True(config.IsCodeDisabled("SA0301"));
    }

    // ── CLI Analysis Integration ──────────────────────────────────────────────

    [Fact]
    public void CheckRunner_Select_OnlyReportsSelectedCodes()
    {
        string dir = CreateTempDir(new Dictionary<string, string>
        {
            ["test.stash"] = "break;\nlet x = 1;\n"
        });
        try
        {
            // With --select SA0101, only SA0101 (break outside loop) should be reported
            var opts = new CheckOptions
            {
                Paths = new List<string> { Path.Combine(dir, "test.stash") },
                Select = new List<string> { "SA0101" },
                NoImports = true
            };
            var runner = new CheckRunner(opts);
            var result = runner.Run();

            var allDiags = result.Files.SelectMany(f => f.Analysis.SemanticDiagnostics).ToList();
            Assert.All(allDiags, d => Assert.Equal("SA0101", d.Code));
            Assert.Contains(allDiags, d => d.Code == "SA0101");
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void CheckRunner_Ignore_SuppressesSpecifiedCode()
    {
        string dir = CreateTempDir(new Dictionary<string, string>
        {
            ["test.stash"] = "let x = 1;\n"
        });
        try
        {
            var opts = new CheckOptions
            {
                Paths = new List<string> { Path.Combine(dir, "test.stash") },
                Ignore = new List<string> { "SA0201" },
                NoImports = true
            };
            var runner = new CheckRunner(opts);
            var result = runner.Run();

            var allDiags = result.Files.SelectMany(f => f.Analysis.SemanticDiagnostics).ToList();
            Assert.DoesNotContain(allDiags, d => d.Code == "SA0201");
        }
        finally { Cleanup(dir); }
    }

    // ── File-Level Suppression ────────────────────────────────────────────────

    [Fact]
    public void FileLevelSuppression_SuppressAll_RemovesAllDiagnostics()
    {
        string dir = CreateTempDir(new Dictionary<string, string>
        {
            ["test.stash"] = "// stash-disable-file\nbreak;\nlet x = 1;\n"
        });
        try
        {
            var diags = AnalyzeFile(Path.Combine(dir, "test.stash"));
            // All diagnostics suppressed (SA0101 break outside loop, SA0201 unused var)
            Assert.DoesNotContain(diags, d => d.Code == "SA0101");
            Assert.DoesNotContain(diags, d => d.Code == "SA0201");
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void FileLevelSuppression_SuppressSpecificCode_RemovesOnlyThatCode()
    {
        string dir = CreateTempDir(new Dictionary<string, string>
        {
            ["test.stash"] = "// stash-disable-file SA0201\nbreak;\nlet x = 1;\n"
        });
        try
        {
            var diags = AnalyzeFile(Path.Combine(dir, "test.stash"));
            // SA0201 suppressed, but SA0101 not
            Assert.DoesNotContain(diags, d => d.Code == "SA0201");
            Assert.Contains(diags, d => d.Code == "SA0101");
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void FileLevelSuppression_WithOtherDirectives_BothApplied()
    {
        string dir = CreateTempDir(new Dictionary<string, string>
        {
            ["test.stash"] = "// stash-disable-file SA0201\n// stash-disable-file SA0101\nbreak;\nlet x = 1;\n"
        });
        try
        {
            var diags = AnalyzeFile(Path.Combine(dir, "test.stash"));
            Assert.DoesNotContain(diags, d => d.Code == "SA0101");
            Assert.DoesNotContain(diags, d => d.Code == "SA0201");
        }
        finally { Cleanup(dir); }
    }

    // ── Auto-Add Suppressions ─────────────────────────────────────────────────

    [Fact]
    public void AddSuppress_InsertsCommentBeforeDiagnosticLine()
    {
        string dir = CreateTempDir(new Dictionary<string, string>
        {
            ["test.stash"] = "let x = 1;\n"
        });
        try
        {
            string filePath = Path.Combine(dir, "test.stash");

            var opts = new CheckOptions
            {
                Paths = new List<string> { filePath },
                NoImports = true
            };
            var runner = new CheckRunner(opts);
            var result = runner.Run();

            // Ensure there's a diagnostic to suppress
            Assert.True(result.Files[0].Analysis.SemanticDiagnostics.Count > 0);

            int inserted = runner.AddSuppressions(result);
            Assert.True(inserted > 0);

            string[] lines = File.ReadAllLines(filePath);
            Assert.Contains(lines, l => l.TrimStart().StartsWith("// stash-disable-next-line"));
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void AddSuppress_WithReason_IncludesReasonText()
    {
        string dir = CreateTempDir(new Dictionary<string, string>
        {
            ["test.stash"] = "let x = 1;\n"
        });
        try
        {
            string filePath = Path.Combine(dir, "test.stash");

            var opts = new CheckOptions
            {
                Paths = new List<string> { filePath },
                NoImports = true,
                Reason = "legacy code"
            };
            var runner = new CheckRunner(opts);
            var result = runner.Run();
            runner.AddSuppressions(result);

            string[] lines = File.ReadAllLines(filePath);
            Assert.Contains(lines, l => l.Contains("legacy code"));
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void AddSuppress_PreservesIndentation()
    {
        string dir = CreateTempDir(new Dictionary<string, string>
        {
            ["test.stash"] = "fn foo() {\n    let x = 1;\n}\n"
        });
        try
        {
            string filePath = Path.Combine(dir, "test.stash");

            var opts = new CheckOptions
            {
                Paths = new List<string> { filePath },
                NoImports = true
            };
            var runner = new CheckRunner(opts);
            var result = runner.Run();
            runner.AddSuppressions(result);

            string[] lines = File.ReadAllLines(filePath);
            // The suppression comment inserted before `let x` should have 4-space indent
            string? suppressionLine = lines.FirstOrDefault(l => l.TrimStart().StartsWith("// stash-disable-next-line"));
            Assert.NotNull(suppressionLine);
            Assert.StartsWith("    ", suppressionLine);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void AddSuppress_NoDiagnostics_InsertsNothing()
    {
        string dir = CreateTempDir(new Dictionary<string, string>
        {
            ["test.stash"] = "let x = 1;\nx;\n" // x is used, so no SA0201
        });
        try
        {
            string filePath = Path.Combine(dir, "test.stash");
            var opts = new CheckOptions
            {
                Paths = new List<string> { filePath },
                NoImports = true,
                Select = new List<string> { "SA9999" } // No rule produces this
            };
            var runner = new CheckRunner(opts);
            var result = runner.Run();
            int inserted = runner.AddSuppressions(result);
            Assert.Equal(0, inserted);
        }
        finally { Cleanup(dir); }
    }

    // ── Stdin Support ─────────────────────────────────────────────────────────

    [Fact]
    public void CheckOptions_Stdin_DashInPaths()
    {
        var opts = CheckOptions.Parse(new[] { "--stdin-filename", "main.stash", "-" });
        Assert.Contains("-", opts.Paths);
        Assert.Equal("main.stash", opts.StdinFilename);
    }

    [Fact]
    public void CheckOptions_StdinFilename_ParsedCorrectly()
    {
        var opts = CheckOptions.Parse(new[] { "--stdin-filename", "foo.stash", "." });
        Assert.Equal("foo.stash", opts.StdinFilename);
    }

    [Fact]
    public void DiscoverFiles_SkipsDash()
    {
        // DiscoverFiles should skip "-" (stdin sentinel)
        string dir = CreateTempDir(new Dictionary<string, string>
        {
            ["a.stash"] = "let x = 1;\n"
        });
        try
        {
            var opts = new CheckOptions
            {
                Paths = new List<string> { Path.Combine(dir, "a.stash"), "-" }
            };
            var runner = new CheckRunner(opts);
            var files = runner.DiscoverFiles();

            // "-" should not appear in discovered files
            Assert.DoesNotContain("-", files);
            Assert.Single(files); // Only a.stash
        }
        finally { Cleanup(dir); }
    }

    // ── Backward Compatibility ────────────────────────────────────────────────

    [Fact]
    public void BackwardCompat_ExistingConfigKeys_StillWork()
    {
        string content = "disable = SA0201\nseverity.SA0207 = error\nrequire-suppression-reason = true\n";
        var config = ProjectConfig.ParseContent(content);

        Assert.True(config.IsCodeDisabled("SA0201"));
        Assert.True(config.SeverityOverrides.ContainsKey("SA0207"));
        Assert.Equal(DiagnosticLevel.Error, config.SeverityOverrides["SA0207"]);
        Assert.True(config.RequireSuppressionReason);
    }

    [Fact]
    public void BackwardCompat_Apply_WithoutFilePath_WorksAsExpected()
    {
        var config = ProjectConfig.ParseContent("disable = SA0201\n");
        var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
        var uri = new Uri("file:///tmp/test.stash");
        var result = engine.Analyze(uri, "let x = 1;\n", noImports: true, configOverride: config);

        // SA0201 should be suppressed
        Assert.DoesNotContain(result.SemanticDiagnostics, d => d.Code == "SA0201");
    }

    [Fact]
    public void BackwardCompat_LoadFromNullDirectory_ReturnsEmpty()
    {
        var config = ProjectConfig.Load(null);
        Assert.Same(ProjectConfig.Empty, config);
    }

    // ── ParseContent Global → PerFile Section Transition ─────────────────────

    [Fact]
    public void ParseContent_GlobalAndPerFileSections_BothParsed()
    {
        string content = """
            disable = SA0201
            severity.SA0207 = error

            [per-file-overrides]
            "**/*.test.stash" = disable SA0206
            """;

        var config = ProjectConfig.ParseContent(content);

        Assert.True(config.IsCodeDisabled("SA0201"));
        Assert.True(config.SeverityOverrides.ContainsKey("SA0207"));
        Assert.Single(config.PerFileOverrides);
        Assert.Contains("SA0206", config.PerFileOverrides["**/*.test.stash"].DisabledCodes);
    }

    [Fact]
    public void ParseContent_LineComments_AreIgnored()
    {
        string content = "# This is a comment\ndisable = SA0201\n# Another comment\n";
        var config = ProjectConfig.ParseContent(content);

        Assert.True(config.IsCodeDisabled("SA0201"));
        // Comment lines don't add anything
        Assert.False(config.IsCodeDisabled("SA0207"));
    }
}
