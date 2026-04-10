using System.Collections.Generic;
using Stash.Analysis;
using Stash.Check;

namespace Stash.Tests.Analysis;

/// <summary>
/// Tests for the Phase 2 autofix infrastructure: CodeFix model, SA0802 unused import,
/// SA0205/SA0203 fixes, CLI --fix/--unsafe-fixes/--diff, and fix conflict resolution.
/// </summary>
public class AutofixTests : AnalysisTestBase
{
    // ── CodeFix model ─────────────────────────────────────────────────

    [Fact]
    public void CodeFix_Creation_StoresProperties()
    {
        var span = new Stash.Common.SourceSpan("<test>", 1, 1, 1, 3);
        var edit = new SourceEdit(span, "const");
        var fix = new CodeFix("Change 'let' to 'const'", FixApplicability.Safe, [edit]);

        Assert.Equal("Change 'let' to 'const'", fix.Title);
        Assert.Equal(FixApplicability.Safe, fix.Applicability);
        Assert.Single(fix.Edits);
        Assert.Equal(span, fix.Edits[0].Span);
        Assert.Equal("const", fix.Edits[0].NewText);
    }

    [Fact]
    public void FixApplicability_EnumValues_Exist()
    {
        Assert.Equal(0, (int)FixApplicability.Safe);
        Assert.Equal(1, (int)FixApplicability.Unsafe);
        Assert.Equal(2, (int)FixApplicability.Suggestion);
    }

    // ── DiagnosticDescriptor.IsFixable ────────────────────────────────

    [Fact]
    public void DiagnosticDescriptor_IsFixable_FalseByDefault()
    {
        Assert.False(DiagnosticDescriptors.SA0201.IsFixable);
        Assert.False(DiagnosticDescriptors.SA0202.IsFixable);
        Assert.False(DiagnosticDescriptors.SA0207.IsFixable);
    }

    [Fact]
    public void DiagnosticDescriptor_SA0802_IsFixable()
    {
        Assert.True(DiagnosticDescriptors.SA0802.IsFixable);
        Assert.Equal(FixApplicability.Safe, DiagnosticDescriptors.SA0802.DefaultFixApplicability);
    }

    [Fact]
    public void DiagnosticDescriptor_SA0205_IsFixable()
    {
        Assert.True(DiagnosticDescriptors.SA0205.IsFixable);
        Assert.Equal(FixApplicability.Safe, DiagnosticDescriptors.SA0205.DefaultFixApplicability);
    }

    [Fact]
    public void DiagnosticDescriptor_SA0203_IsFixable()
    {
        Assert.True(DiagnosticDescriptors.SA0203.IsFixable);
        Assert.Equal(FixApplicability.Unsafe, DiagnosticDescriptors.SA0203.DefaultFixApplicability);
    }

    // ── SA0802 — Unused Import ────────────────────────────────────────

    [Fact]
    public void UnusedImport_SingleUnusedName_ReportsSA0802()
    {
        var diagnostics = Validate("import { foo } from \"./mod.stash\";\nio.println(\"hello\");");
        Assert.Contains(diagnostics, d => d.Code == "SA0802" && d.Message.Contains("foo"));
    }

    [Fact]
    public void UnusedImport_AllUnused_ReportsSA0802()
    {
        var diagnostics = Validate("import { foo, bar } from \"./mod.stash\";\nio.println(\"hello\");");
        Assert.Contains(diagnostics, d => d.Code == "SA0802" && d.Message.Contains("foo"));
    }

    [Fact]
    public void UnusedImport_UsedName_NoDiagnostic()
    {
        var diagnostics = Validate("import { foo } from \"./mod.stash\";\nio.println(foo);");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0802");
    }

    [Fact]
    public void UnusedImport_PartiallyUsed_NoDiagnostic()
    {
        // When at least one name is used, the whole import is NOT flagged as SA0802.
        var diagnostics = Validate("import { foo, bar } from \"./mod.stash\";\nio.println(foo);");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0802");
    }

    [Fact]
    public void UnusedImport_SA0201_NotEmittedForImportedNames()
    {
        // SA0201 should NOT fire for imported names — SA0802 handles them.
        var diagnostics = Validate("import { foo } from \"./mod.stash\";\nio.println(\"hello\");");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0201" && d.Message.Contains("'foo'"));
        Assert.Contains(diagnostics, d => d.Code == "SA0802");
    }

    [Fact]
    public void UnusedImport_HasSafeFix()
    {
        var diagnostics = Validate("import { foo } from \"./mod.stash\";\nio.println(\"hello\");");
        var diag = diagnostics.FirstOrDefault(d => d.Code == "SA0802");
        Assert.NotNull(diag);
        Assert.Single(diag.Fixes);
        Assert.Equal(FixApplicability.Safe, diag.Fixes[0].Applicability);
        Assert.Equal("Remove unused import", diag.Fixes[0].Title);
        // Fix should have one edit replacing the import span with empty string
        Assert.Single(diag.Fixes[0].Edits);
        Assert.Equal("", diag.Fixes[0].Edits[0].NewText);
    }

    [Fact]
    public void UnusedImport_IsUnnecessary()
    {
        var diagnostics = Validate("import { foo } from \"./mod.stash\";\nio.println(\"hello\");");
        var diag = diagnostics.FirstOrDefault(d => d.Code == "SA0802");
        Assert.NotNull(diag);
        Assert.True(diag.IsUnnecessary);
    }

    [Fact]
    public void UnusedImport_SA0802_InAllByCode()
    {
        Assert.True(DiagnosticDescriptors.AllByCode.ContainsKey("SA0802"));
    }

    // ── SA0205 — let-could-be-const fix ──────────────────────────────

    [Fact]
    public void LetCouldBeConst_HasSafeFix()
    {
        var diagnostics = Validate("let x = 5; io.println(x);");
        var diag = diagnostics.FirstOrDefault(d => d.Code == "SA0205" && d.Message.Contains("'x'"));
        Assert.NotNull(diag);
        Assert.Single(diag.Fixes);
        Assert.Equal(FixApplicability.Safe, diag.Fixes[0].Applicability);
        Assert.Equal("Change 'let' to 'const'", diag.Fixes[0].Title);
    }

    [Fact]
    public void LetCouldBeConst_FixEdit_ReplacesLetWithConst()
    {
        var diagnostics = Validate("let x = 5; io.println(x);");
        var diag = diagnostics.FirstOrDefault(d => d.Code == "SA0205" && d.Message.Contains("'x'"));
        Assert.NotNull(diag);
        Assert.Single(diag.Fixes);

        var fix = diag.Fixes[0];
        Assert.Single(fix.Edits);

        var edit = fix.Edits[0];
        Assert.Equal("const", edit.NewText);
        // The edit should span exactly "let" (3 characters)
        Assert.Equal(edit.Span.StartLine, edit.Span.EndLine);
        Assert.Equal(edit.Span.StartColumn + 2, edit.Span.EndColumn);
    }

    [Fact]
    public void LetCouldBeConst_Reassigned_NoDiagnosticFix()
    {
        var diagnostics = Validate("let x = 5; x = 10; io.println(x);");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0205");
    }

    // ── SA0203 — const reassignment fix ──────────────────────────────

    [Fact]
    public void ConstReassignment_HasUnsafeFix()
    {
        var diagnostics = Validate("const x = 5; x = 10;");
        var diag = diagnostics.FirstOrDefault(d => d.Code == "SA0203");
        Assert.NotNull(diag);
        Assert.Single(diag.Fixes);
        Assert.Equal(FixApplicability.Unsafe, diag.Fixes[0].Applicability);
    }

    [Fact]
    public void ConstReassignment_FixEdit_ReplacesConstWithLet()
    {
        var diagnostics = Validate("const x = 5; x = 10;");
        var diag = diagnostics.FirstOrDefault(d => d.Code == "SA0203");
        Assert.NotNull(diag);

        var fix = diag.Fixes[0];
        Assert.Single(fix.Edits);

        var edit = fix.Edits[0];
        Assert.Equal("let", edit.NewText);
        // The edit should span exactly "const" (5 characters)
        Assert.Equal(edit.Span.StartLine, edit.Span.EndLine);
        Assert.Equal(edit.Span.StartColumn + 4, edit.Span.EndColumn);
    }

    // ── SemanticDiagnostic.Fixes default ─────────────────────────────

    [Fact]
    public void SemanticDiagnostic_Fixes_DefaultEmpty()
    {
        var diag = DiagnosticDescriptors.SA0101.CreateDiagnostic(
            new Stash.Common.SourceSpan("<test>", 1, 1, 1, 5));
        Assert.Empty(diag.Fixes);
    }

    [Fact]
    public void SemanticDiagnostic_CreateDiagnosticWithFix_AssociatesFix()
    {
        var span = new Stash.Common.SourceSpan("<test>", 1, 1, 1, 3);
        var fix = new CodeFix("Fix it", FixApplicability.Safe, [new SourceEdit(span, "const")]);
        var diag = DiagnosticDescriptors.SA0205.CreateDiagnosticWithFix(span, fix, "x");

        Assert.Single(diag.Fixes);
        Assert.Equal("Fix it", diag.Fixes[0].Title);
    }

    // ── FixApplier — source text editing ─────────────────────────────

    [Fact]
    public void FixApplier_ApplyFixes_ReplacesLetWithConst()
    {
        const string source = "let x = 5;\nlet y = 10;\n";
        // Simulate the SA0205 fix for 'x' on line 1, columns 1-3 ("let")
        var edit = new SourceEdit(new Stash.Common.SourceSpan("<test>", 1, 1, 1, 3), "const");
        var fix = new CodeFix("let->const", FixApplicability.Safe, [edit]);

        string result = FixApplier.ApplyFixesToSource(source, [fix]);
        Assert.StartsWith("const x = 5;", result);
        Assert.Contains("let y = 10;", result);
    }

    [Fact]
    public void FixApplier_ApplyFixes_RemovesUnusedImport()
    {
        const string source = "import { foo } from \"./mod.stash\";\nlet x = 5;\n";
        // Span covering the import statement (line 1)
        var importSpan = new Stash.Common.SourceSpan("<test>", 1, 1, 1, 34);
        var edit = new SourceEdit(importSpan, "");
        var fix = new CodeFix("Remove import", FixApplicability.Safe, [edit]);

        string result = FixApplier.ApplyFixesToSource(source, [fix]);
        Assert.DoesNotContain("import { foo }", result);
        Assert.Contains("let x = 5;", result);
    }

    [Fact]
    public void FixApplier_ApplyFixes_ConflictingEdits_FirstWins()
    {
        const string source = "let x = 5;\n";
        var span = new Stash.Common.SourceSpan("<test>", 1, 1, 1, 3);
        var edit1 = new SourceEdit(span, "const");
        var edit2 = new SourceEdit(span, "var");
        var fix1 = new CodeFix("fix1", FixApplicability.Safe, [edit1]);
        var fix2 = new CodeFix("fix2", FixApplicability.Safe, [edit2]);

        // Both fixes target the same span; the first one encountered should win.
        string result = FixApplier.ApplyFixesToSource(source, [fix1, fix2]);
        // One of the two replacements should be applied (not duplicated)
        int constOccurrences = result.Split("const").Length - 1;
        int varOccurrences = result.Split("var ").Length - 1;
        Assert.Equal(1, constOccurrences + varOccurrences);
    }

    [Fact]
    public void FixApplier_ApplyFixes_MultipleNonOverlapping_AllApplied()
    {
        const string source = "let x = 5;\nlet y = 10;\n";
        var edit1 = new SourceEdit(new Stash.Common.SourceSpan("<test>", 1, 1, 1, 3), "const");
        var edit2 = new SourceEdit(new Stash.Common.SourceSpan("<test>", 2, 1, 2, 3), "const");
        var fix1 = new CodeFix("fix1", FixApplicability.Safe, [edit1]);
        var fix2 = new CodeFix("fix2", FixApplicability.Safe, [edit2]);

        string result = FixApplier.ApplyFixesToSource(source, [fix1, fix2]);
        Assert.Contains("const x = 5;", result);
        Assert.Contains("const y = 10;", result);
    }

    // ── CheckOptions — fix flags ──────────────────────────────────────

    [Fact]
    public void CheckOptions_Fix_ParsedCorrectly()
    {
        var opts = CheckOptions.Parse(new[] { "--fix", "." });
        Assert.True(opts.Fix);
        Assert.False(opts.UnsafeFixes);
        Assert.False(opts.Diff);
    }

    [Fact]
    public void CheckOptions_UnsafeFixes_ParsedCorrectly()
    {
        var opts = CheckOptions.Parse(new[] { "--unsafe-fixes", "." });
        Assert.False(opts.Fix);
        Assert.True(opts.UnsafeFixes);
    }

    [Fact]
    public void CheckOptions_Diff_ParsedCorrectly()
    {
        var opts = CheckOptions.Parse(new[] { "--diff", "." });
        Assert.False(opts.Fix);
        Assert.True(opts.Diff);
    }

    // ── FixApplier.CollectFixes ───────────────────────────────────────

    [Fact]
    public void CollectFixes_SafeOnly_ExcludesUnsafe()
    {
        string file = CreateTempStashFile("const x = 5;\nx = 10;\n");
        try
        {
            var opts = new CheckOptions { Paths = [file], NoImports = true };
            var runner = new CheckRunner(opts);
            var result = runner.Run();

            var fixesByFile = FixApplier.CollectFixes(result, allowUnsafe: false);

            // SA0203 has an Unsafe fix — should not be included when allowUnsafe=false
            foreach (var fixes in fixesByFile.Values)
            {
                foreach (var (fix, _) in fixes)
                {
                    Assert.NotEqual(FixApplicability.Unsafe, fix.Applicability);
                }
            }
        }
        finally
        {
            CleanupTempFile(file);
        }
    }

    [Fact]
    public void CollectFixes_AllowUnsafe_IncludesUnsafeFixes()
    {
        string file = CreateTempStashFile("const x = 5;\nx = 10;\n");
        try
        {
            var opts = new CheckOptions { Paths = [file], NoImports = true };
            var runner = new CheckRunner(opts);
            var result = runner.Run();

            var fixesByFile = FixApplier.CollectFixes(result, allowUnsafe: true);

            bool hasUnsafe = fixesByFile.Values
                .Any(fixes => fixes.Any(f => f.Fix.Applicability == FixApplicability.Unsafe));
            Assert.True(hasUnsafe);
        }
        finally
        {
            CleanupTempFile(file);
        }
    }

    // ── CLI --fix applies fixes to file ───────────────────────────────

    [Fact]
    public void ApplyFixes_SA0205_OverwritesFile()
    {
        string file = CreateTempStashFile("let x = 5;\nio.println(x);\n");
        try
        {
            var opts = new CheckOptions { Paths = [file], NoImports = true };
            var runner = new CheckRunner(opts);
            var result = runner.Run();

            var fixesByFile = FixApplier.CollectFixes(result, allowUnsafe: false);
            FixApplier.ApplyFixes(fixesByFile);

            string written = File.ReadAllText(file);
            Assert.Contains("const x = 5;", written);
        }
        finally
        {
            CleanupTempFile(file);
        }
    }

    [Fact]
    public void ApplyFixes_SA0802_RemovesImportLine()
    {
        string file = CreateTempStashFile("import { foo } from \"./unused.stash\";\nio.println(\"hello\");\n");
        try
        {
            var opts = new CheckOptions { Paths = [file], NoImports = true };
            var runner = new CheckRunner(opts);
            var result = runner.Run();

            var fixesByFile = FixApplier.CollectFixes(result, allowUnsafe: false);
            FixApplier.ApplyFixes(fixesByFile);

            string written = File.ReadAllText(file);
            Assert.DoesNotContain("import { foo }", written);
            Assert.Contains("io.println", written);
        }
        finally
        {
            CleanupTempFile(file);
        }
    }

    // ── FixApplier.WriteDiff ──────────────────────────────────────────

    [Fact]
    public void WriteDiff_SA0205_ShowsDiff()
    {
        string file = CreateTempStashFile("let x = 5;\nio.println(x);\n");
        try
        {
            var opts = new CheckOptions { Paths = [file], NoImports = true };
            var runner = new CheckRunner(opts);
            var result = runner.Run();

            var fixesByFile = FixApplier.CollectFixes(result, allowUnsafe: false);

            using var writer = new StringWriter();
            FixApplier.WriteDiff(result, fixesByFile, writer);
            string diff = writer.ToString();

            Assert.Contains("---", diff);
            Assert.Contains("+++", diff);
            Assert.Contains("-let x = 5;", diff);
            Assert.Contains("+const x = 5;", diff);
        }
        finally
        {
            CleanupTempFile(file);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static string CreateTempStashFile(string content)
    {
        string dir = Path.Combine(Path.GetTempPath(), "stash-autofix-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "test.stash");
        File.WriteAllText(file, content);
        return file;
    }

    private static void CleanupTempFile(string file)
    {
        try
        {
            string? dir = Path.GetDirectoryName(file);
            if (dir != null)
            {
                Directory.Delete(dir, true);
            }
        }
        catch { /* Best effort */ }
    }
}
