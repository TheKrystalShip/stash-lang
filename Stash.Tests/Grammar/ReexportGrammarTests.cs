namespace Stash.Tests.Grammar;

using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

/// <summary>
/// Regression tests verifying that the re-export forms introduced by the
/// export-from-import feature tokenize correctly in both the Monarch grammar
/// (Playground) and the TextMate grammar (VS Code extension).
///
/// These tests use file-content assertions: they load the grammar files from
/// the repository and assert that required patterns / keyword entries are
/// present and match the expected inputs. No running Monaco or TextMate engine
/// is required.
/// </summary>
public class ReexportGrammarTests
{
    private static string FindRepoRoot()
    {
        string current = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && current != null; i++)
        {
            if (File.Exists(Path.Combine(current, "Stash.sln")))
                return current;
            current = Path.GetDirectoryName(current)!;
        }
        throw new InvalidOperationException(
            $"Could not find repo root (Stash.sln) above {AppContext.BaseDirectory}.");
    }

    // ── Monarch grammar (stash-language.js) ──────────────────────────────────

    [Fact]
    public void MonarchGrammar_ExportKeyword_InKeywordsList()
    {
        string repoRoot = FindRepoRoot();
        string monarchPath = Path.Combine(
            repoRoot, "Stash.Playground", "wwwroot", "js", "stash-language.js");

        Assert.True(File.Exists(monarchPath), $"Monarch grammar not found at {monarchPath}.");

        string content = File.ReadAllText(monarchPath);

        // The keywords array line should contain 'export'
        Assert.Contains("'export'", content);
    }

    [Fact]
    public void MonarchGrammar_FromKeyword_InKeywordsList()
    {
        string repoRoot = FindRepoRoot();
        string monarchPath = Path.Combine(
            repoRoot, "Stash.Playground", "wwwroot", "js", "stash-language.js");

        Assert.True(File.Exists(monarchPath), $"Monarch grammar not found at {monarchPath}.");

        string content = File.ReadAllText(monarchPath);

        // The keywords array line should contain 'from'
        Assert.Contains("'from'", content);
    }

    [Fact]
    public void MonarchGrammar_ExportAndFrom_BothPresentOnSameKeywordsLine()
    {
        string repoRoot = FindRepoRoot();
        string monarchPath = Path.Combine(
            repoRoot, "Stash.Playground", "wwwroot", "js", "stash-language.js");

        string content = File.ReadAllText(monarchPath);

        // Find the keywords array and verify both tokens appear in it.
        // The array is on a single line of the form:
        //   keywords: [ ..., 'export', 'from', ... ],
        var keywordsLineMatch = Regex.Match(content, @"keywords\s*:\s*\[([^\]]+)\]");
        Assert.True(keywordsLineMatch.Success, "Could not locate 'keywords' array in Monarch grammar.");

        string keywordsBody = keywordsLineMatch.Groups[1].Value;
        Assert.Contains("'export'", keywordsBody);
        Assert.Contains("'from'", keywordsBody);
    }

    // ── TextMate grammar (stash.tmLanguage.json) ─────────────────────────────

    private static JsonDocument LoadTmLanguage()
    {
        string repoRoot = FindRepoRoot();
        string tmPath = Path.Combine(
            repoRoot, ".vscode", "extensions", "stash-lang",
            "syntaxes", "stash.tmLanguage.json");

        Assert.True(File.Exists(tmPath), $"tmLanguage file not found at {tmPath}.");

        string json = File.ReadAllText(tmPath);
        return JsonDocument.Parse(json);
    }

    [Fact]
    public void TmLanguage_ExportDeclaration_PatternExists()
    {
        using var doc = LoadTmLanguage();
        var repo = doc.RootElement.GetProperty("repository");
        Assert.True(
            repo.TryGetProperty("export-declaration", out _),
            "Expected 'export-declaration' repository entry in tmLanguage.");
    }

    [Fact]
    public void TmLanguage_ExportBlockForm_MatchesExportKeyword()
    {
        // Pattern: export { foo, bar };
        // Asserts the existing block-export pattern fires on 'export'.
        using var doc = LoadTmLanguage();
        var repo = doc.RootElement.GetProperty("repository");
        var exportDecl = repo.GetProperty("export-declaration");
        var patterns = exportDecl.GetProperty("patterns");

        bool found = false;
        foreach (var p in patterns.EnumerateArray())
        {
            if (!p.TryGetProperty("match", out var match)) continue;
            string pattern = match.GetString()!;
            if (!Regex.IsMatch("export { foo, bar };", pattern)) continue;

            // Check that capture group 1 has keyword.control scope
            if (p.TryGetProperty("captures", out var caps) &&
                caps.TryGetProperty("1", out var cap1) &&
                cap1.TryGetProperty("name", out var name) &&
                name.GetString()!.StartsWith("keyword.control"))
            {
                found = true;
                break;
            }
        }

        Assert.True(found,
            "No pattern in 'export-declaration' matched 'export { foo, bar };' with keyword.control on capture 1.");
    }

    [Fact]
    public void TmLanguage_ReexportFromForm_MatchesExportAndFrom()
    {
        // Pattern: export { a } from "p";
        // Asserts a three-capture pattern exists that applies keyword.control
        // to both 'export' (capture 1) and 'from' (capture 3).
        using var doc = LoadTmLanguage();
        var repo = doc.RootElement.GetProperty("repository");
        var exportDecl = repo.GetProperty("export-declaration");
        var patterns = exportDecl.GetProperty("patterns");

        bool found = false;
        foreach (var p in patterns.EnumerateArray())
        {
            if (!p.TryGetProperty("match", out var match)) continue;
            string pattern = match.GetString()!;

            string testInput = "export { a } from \"p\";";
            var m = Regex.Match(testInput, pattern);
            if (!m.Success) continue;

            if (!p.TryGetProperty("captures", out var caps)) continue;

            // Capture 1 must have keyword.control scope
            bool cap1IsKeyword = caps.TryGetProperty("1", out var c1) &&
                                 c1.TryGetProperty("name", out var n1) &&
                                 n1.GetString()!.StartsWith("keyword.control");

            // Capture 3 must exist and have keyword.control scope (for 'from')
            bool cap3IsKeyword = caps.TryGetProperty("3", out var c3) &&
                                 c3.TryGetProperty("name", out var n3) &&
                                 n3.GetString()!.StartsWith("keyword.control");

            if (cap1IsKeyword && cap3IsKeyword)
            {
                found = true;
                break;
            }
        }

        Assert.True(found,
            "No pattern in 'export-declaration' matched 'export { a } from \"p\";' " +
            "with keyword.control on both 'export' (capture 1) and 'from' (capture 3).");
    }

    [Fact]
    public void TmLanguage_NamespaceReexportForm_ExportKeywordHighlighted()
    {
        // Pattern: export "lib/data.stash" as data;
        // 'export' must get keyword.control via the fallback export pattern.
        using var doc = LoadTmLanguage();
        var repo = doc.RootElement.GetProperty("repository");
        var exportDecl = repo.GetProperty("export-declaration");
        var patterns = exportDecl.GetProperty("patterns");

        string testInput = "export \"lib/data.stash\" as data;";
        bool exportHighlighted = false;

        foreach (var p in patterns.EnumerateArray())
        {
            if (!p.TryGetProperty("match", out var match)) continue;
            string pattern = match.GetString()!;
            var m = Regex.Match(testInput, pattern);
            if (!m.Success) continue;

            // Either the pattern has keyword.control via captures or via a top-level name
            bool hasKeywordScope = false;

            if (p.TryGetProperty("name", out var nameEl) &&
                nameEl.GetString()!.StartsWith("keyword.control"))
            {
                hasKeywordScope = true;
            }
            else if (p.TryGetProperty("captures", out var caps) &&
                     caps.TryGetProperty("1", out var c1) &&
                     c1.TryGetProperty("name", out var n1) &&
                     n1.GetString()!.StartsWith("keyword.control"))
            {
                hasKeywordScope = true;
            }

            if (hasKeywordScope)
            {
                exportHighlighted = true;
                break;
            }
        }

        Assert.True(exportHighlighted,
            "No pattern in 'export-declaration' highlighted 'export' as keyword.control " +
            "for the namespace re-export form 'export \"lib/data.stash\" as data;'.");
    }

    [Fact]
    public void TmLanguage_ParentFeatureExportBlock_StillMatches()
    {
        // Regression: parent-feature form 'export { foo };' must still match.
        using var doc = LoadTmLanguage();
        var repo = doc.RootElement.GetProperty("repository");
        var exportDecl = repo.GetProperty("export-declaration");
        var patterns = exportDecl.GetProperty("patterns");

        string testInput = "export { foo };";
        bool matched = false;

        foreach (var p in patterns.EnumerateArray())
        {
            if (!p.TryGetProperty("match", out var match)) continue;
            string pattern = match.GetString()!;
            if (Regex.IsMatch(testInput, pattern))
            {
                matched = true;
                break;
            }
        }

        Assert.True(matched,
            "Parent-feature form 'export { foo };' is no longer matched by any pattern " +
            "in 'export-declaration'. Regression check failed.");
    }

    [Fact]
    public void TmLanguage_ReexportFromForm_HasThreeCaptures()
    {
        // Structural check: the re-export-from pattern must declare at least 3 captures.
        using var doc = LoadTmLanguage();
        var repo = doc.RootElement.GetProperty("repository");
        var exportDecl = repo.GetProperty("export-declaration");
        var patterns = exportDecl.GetProperty("patterns");

        string testInput = "export { a } from \"p\";";
        bool found = false;

        foreach (var p in patterns.EnumerateArray())
        {
            if (!p.TryGetProperty("match", out var match)) continue;
            string pattern = match.GetString()!;
            if (!Regex.IsMatch(testInput, pattern)) continue;
            if (!p.TryGetProperty("captures", out var caps)) continue;

            bool has1 = caps.TryGetProperty("1", out _);
            bool has3 = caps.TryGetProperty("3", out _);
            if (has1 && has3) { found = true; break; }
        }

        Assert.True(found,
            "The re-export-from pattern matching 'export { a } from \"p\";' " +
            "does not declare captures 1 and 3.");
    }
}
