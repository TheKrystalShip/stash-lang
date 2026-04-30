using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Stash.Analysis;
using Stash.Check;
using Stash.Common;
using Stash.Tests.Analysis.Helpers;

namespace Stash.Tests.Analysis;

/// <summary>
/// Tests for Phase 6: Polish &amp; Ecosystem.
/// Covers related locations, IsDeprecated, JSON/GitHub/Grouped formatters,
/// --timing, SA0109 (cyclomatic complexity), SA0209 (naming convention),
/// SA0405 (too many parameters), SA0804 (import ordering), FixVerifier helper.
/// </summary>
public class Phase6Tests : AnalysisTestBase
{
    // ── RelatedLocation infrastructure ───────────────────────────────

    [Fact]
    public void RelatedLocation_Record_StoresMessageAndSpan()
    {
        var span = new SourceSpan("<test>", 1, 1, 1, 5);
        var related = new RelatedLocation("see declaration here", span);
        Assert.Equal("see declaration here", related.Message);
        Assert.Equal(span, related.Span);
    }

    [Fact]
    public void SemanticDiagnostic_RelatedLocations_DefaultsEmpty()
    {
        var span = new SourceSpan("<test>", 1, 1, 1, 5);
        var diag = new SemanticDiagnostic("SA0201", "msg", DiagnosticLevel.Information, span);
        Assert.Empty(diag.RelatedLocations);
    }

    [Fact]
    public void SemanticDiagnostic_RelatedLocations_SetViaInit()
    {
        var span = new SourceSpan("<test>", 1, 1, 1, 5);
        var related = new RelatedLocation("related", span);
        var diag = new SemanticDiagnostic("SA0201", "msg", DiagnosticLevel.Information, span)
        {
            RelatedLocations = [related]
        };
        Assert.Single(diag.RelatedLocations);
        Assert.Equal("related", diag.RelatedLocations[0].Message);
    }

    [Fact]
    public void DiagnosticDescriptor_CreateDiagnosticWithRelated_SetsLocations()
    {
        var span = new SourceSpan("<test>", 1, 1, 1, 5);
        var related = new RelatedLocation("declaration", span);
        var diag = DiagnosticDescriptors.SA0207.CreateDiagnosticWithRelated(span, [related], "x");
        Assert.Equal("SA0207", diag.Code);
        Assert.Single(diag.RelatedLocations);
        Assert.Equal("declaration", diag.RelatedLocations[0].Message);
    }

    // ── IsDeprecated infrastructure ───────────────────────────────────

    [Fact]
    public void SemanticDiagnostic_IsDeprecated_DefaultsFalse()
    {
        var span = new SourceSpan("<test>", 1, 1, 1, 5);
        var diag = new SemanticDiagnostic("SA0201", "msg", DiagnosticLevel.Information, span);
        Assert.False(diag.IsDeprecated);
    }

    [Fact]
    public void SemanticDiagnostic_IsDeprecated_SetViaInit()
    {
        var span = new SourceSpan("<test>", 1, 1, 1, 5);
        var diag = new SemanticDiagnostic("SA0201", "msg", DiagnosticLevel.Information, span)
        {
            IsDeprecated = true
        };
        Assert.True(diag.IsDeprecated);
    }

    [Fact]
    public void DiagnosticDescriptor_CreateDeprecatedDiagnostic_SetsFlag()
    {
        var span = new SourceSpan("<test>", 1, 1, 1, 5);
        var diag = DiagnosticDescriptors.SA0201.CreateDeprecatedDiagnostic(span, "Variable", "x");
        Assert.True(diag.IsDeprecated);
        Assert.Equal("SA0201", diag.Code);
    }

    // ── SA0109 — Cyclomatic Complexity ────────────────────────────────

    [Fact]
    public void CyclomaticComplexity_SimpleFunction_NoDiagnostic()
    {
        var diagnostics = Validate("fn foo(x) { return x + 1; }");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0109");
    }

    [Fact]
    public void CyclomaticComplexity_HighComplexity_ReportsSA0109()
    {
        // 11 if-branches = complexity 12 (1 base + 11 ifs)
        string source = @"fn complex(a, b, c, d, e) {
    if (a) { return 1; }
    if (b) { return 2; }
    if (c) { return 3; }
    if (d) { return 4; }
    if (e) { return 5; }
    if (a && b) { return 6; }
    if (b && c) { return 7; }
    if (c && d) { return 8; }
    if (d && e) { return 9; }
    if (a || b) { return 10; }
    if (b || c) { return 11; }
    return 0;
}";
        var diagnostics = Validate(source);
        Assert.Contains(diagnostics, d => d.Code == "SA0109" && d.Message.Contains("complex"));
    }

    [Fact]
    public void CyclomaticComplexity_AtThreshold_NoDiagnostic()
    {
        // Exactly 10: 1 base + 9 ifs
        string source = @"fn atLimit(a, b, c) {
    if (a) { return 1; }
    if (b) { return 2; }
    if (a && b) { return 3; }
    if (a || b) { return 4; }
    if (c) { return 5; }
    if (a && c) { return 6; }
    if (b && c) { return 7; }
    if (a || c) { return 8; }
    if (b || c) { return 9; }
    return 0;
}";
        var diagnostics = Validate(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0109");
    }

    [Fact]
    public void CyclomaticComplexity_TernaryAndLoops_Counted()
    {
        // 1 base + while + for + for-in + do-while + ternary = 6 — under threshold
        string source = @"fn withLoops(arr) {
    let i = 0;
    while (i < 10) { i = i + 1; }
    for (let j = 0; j < 5; j++) {}
    for (let x in arr) {}
    do {} while (i > 0);
    let y = i > 0 ? 1 : 0;
    return y;
}";
        var diagnostics = Validate(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0109");
    }

    [Fact]
    public void CyclomaticComplexity_SA0109_Registered()
    {
        Assert.True(DiagnosticDescriptors.AllByCode.ContainsKey("SA0109"));
        Assert.Equal("Control flow", DiagnosticDescriptors.SA0109.Category);
    }

    // ── SA0405 — Too Many Parameters ──────────────────────────────────

    [Fact]
    public void TooManyParameters_BelowThreshold_NoDiagnostic()
    {
        var diagnostics = Validate("fn foo(a, b, c, d, e) { return a; }");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0405");
    }

    [Fact]
    public void TooManyParameters_ExceedsThreshold_ReportsSA0405()
    {
        var diagnostics = Validate("fn foo(a, b, c, d, e, f) { return a; }");
        Assert.Contains(diagnostics, d => d.Code == "SA0405" && d.Message.Contains("foo") && d.Message.Contains("6"));
    }

    [Fact]
    public void TooManyParameters_ExactlyAtThreshold_NoDiagnostic()
    {
        var diagnostics = Validate("fn foo(a, b, c, d, e) { return a; }");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0405");
    }

    [Fact]
    public void TooManyParameters_SA0405_Registered()
    {
        Assert.True(DiagnosticDescriptors.AllByCode.ContainsKey("SA0405"));
        Assert.Equal("Functions & calls", DiagnosticDescriptors.SA0405.Category);
    }

    // ── SA0209 — Naming Convention ────────────────────────────────────

    [Fact]
    public void NamingConvention_ValidCamelCase_NoDiagnostic()
    {
        var diagnostics = Validate("let myVar = 5;");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0209");
    }

    [Fact]
    public void NamingConvention_VariableStartsWithUpper_ReportsSA0209()
    {
        var diagnostics = Validate("let MyVar = 5;");
        Assert.Contains(diagnostics, d => d.Code == "SA0209" && d.Message.Contains("MyVar"));
    }

    [Fact]
    public void NamingConvention_ValidPascalCaseStruct_NoDiagnostic()
    {
        var diagnostics = Validate("struct MyPoint { x: int, y: int }");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0209");
    }

    [Fact]
    public void NamingConvention_StructStartsWithLower_ReportsSA0209()
    {
        var diagnostics = Validate("struct myPoint { x: int, y: int }");
        Assert.Contains(diagnostics, d => d.Code == "SA0209" && d.Message.Contains("myPoint"));
    }

    [Fact]
    public void NamingConvention_EnumStartsWithLower_ReportsSA0209()
    {
        var diagnostics = Validate("enum direction { North, South }");
        Assert.Contains(diagnostics, d => d.Code == "SA0209" && d.Message.Contains("direction"));
    }

    [Fact]
    public void NamingConvention_FunctionStartsWithLower_NoDiagnostic()
    {
        var diagnostics = Validate("fn fooBar() { return 1; }");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0209");
    }

    [Fact]
    public void NamingConvention_FunctionStartsWithUpper_ReportsSA0209()
    {
        var diagnostics = Validate("fn FooBar() { return 1; }");
        Assert.Contains(diagnostics, d => d.Code == "SA0209" && d.Message.Contains("FooBar"));
    }

    [Fact]
    public void NamingConvention_UnderscorePrefix_NoDiagnostic()
    {
        var diagnostics = Validate("let _private = 5;");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0209");
    }

    [Fact]
    public void NamingConvention_SingleLetterVar_NoDiagnostic()
    {
        var diagnostics = Validate("let x = 5;");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0209");
    }

    [Fact]
    public void NamingConvention_SA0209_Registered()
    {
        Assert.True(DiagnosticDescriptors.AllByCode.ContainsKey("SA0209"));
        Assert.Equal("Declarations", DiagnosticDescriptors.SA0209.Category);
    }

    // ── SA0804 — Import Ordering ──────────────────────────────────────

    [Fact]
    public void ImportOrdering_AlreadySorted_NoDiagnostic()
    {
        string source = @"import { foo } from ""std:core"";
import { bar } from ""@pkg/utils"";
import { baz } from ""./local"";
io.println(foo);
io.println(bar);
io.println(baz);";
        var diagnostics = Validate(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0804");
    }

    [Fact]
    public void ImportOrdering_RelativeBeforeStdlib_ReportsSA0804()
    {
        string source = @"import { baz } from ""./local"";
import { foo } from ""std:core"";
io.println(foo);
io.println(baz);";
        var diagnostics = Validate(source);
        Assert.Contains(diagnostics, d => d.Code == "SA0804");
    }

    [Fact]
    public void ImportOrdering_PackageBeforeStdlib_ReportsSA0804()
    {
        string source = @"import { bar } from ""@pkg/utils"";
import { foo } from ""std:core"";
io.println(foo);
io.println(bar);";
        var diagnostics = Validate(source);
        Assert.Contains(diagnostics, d => d.Code == "SA0804");
    }

    [Fact]
    public void ImportOrdering_SingleImport_NoDiagnostic()
    {
        var diagnostics = Validate("import { foo } from \"./a\";\nio.println(foo);");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0804");
    }

    [Fact]
    public void ImportOrdering_HasFix()
    {
        string source = @"import { baz } from ""./local"";
import { foo } from ""std:core"";
io.println(foo);
io.println(baz);";
        var diagnostics = Validate(source);
        var sa0804 = diagnostics.FirstOrDefault(d => d.Code == "SA0804");
        Assert.NotNull(sa0804);
        Assert.True(sa0804!.Fixes.Count > 0);
    }

    [Fact]
    public void ImportOrdering_SA0804_Registered()
    {
        Assert.True(DiagnosticDescriptors.AllByCode.ContainsKey("SA0804"));
        Assert.Equal("Imports", DiagnosticDescriptors.SA0804.Category);
    }

    // ── JSON Formatter ────────────────────────────────────────────────

    [Fact]
    public void JsonFormatter_EmptyResult_OutputsEmptyArray()
    {
        var result = new CheckResult(new List<FileResult>());
        var formatter = new JsonFormatter();

        using var stream = new MemoryStream();
        formatter.Write(result, stream);
        stream.Position = 0;
        string json = new StreamReader(stream, Encoding.UTF8).ReadToEnd();

        Assert.Equal("[]", json.Trim());
    }

    [Fact]
    public void JsonFormatter_WithDiagnostics_ValidJsonArray()
    {
        string source = "let MyVar = 5;";
        var result = BuildCheckResult(source, "test.stash");
        var formatter = new JsonFormatter();

        using var stream = new MemoryStream();
        formatter.Write(result, stream);
        stream.Position = 0;

        var doc = JsonDocument.Parse(stream);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        // Should have at least one item for SA0209
        Assert.True(doc.RootElement.GetArrayLength() > 0);

        var first = doc.RootElement[0];
        Assert.True(first.TryGetProperty("file", out _));
        Assert.True(first.TryGetProperty("line", out _));
        Assert.True(first.TryGetProperty("column", out _));
        Assert.True(first.TryGetProperty("code", out _));
        Assert.True(first.TryGetProperty("severity", out _));
        Assert.True(first.TryGetProperty("message", out _));
    }

    [Fact]
    public void JsonFormatter_FormatString_IsJson()
    {
        Assert.Equal("json", new JsonFormatter().Format);
    }

    // ── GitHub Formatter ──────────────────────────────────────────────

    [Fact]
    public void GitHubFormatter_WithDiagnostics_OutputsAnnotations()
    {
        string source = "let MyVar = 5;";
        var result = BuildCheckResult(source, "test.stash");
        var formatter = new GitHubFormatter();

        using var stream = new MemoryStream();
        formatter.Write(result, stream);
        stream.Position = 0;
        string output = new StreamReader(stream, Encoding.UTF8).ReadToEnd();

        // Should have GitHub annotation format
        Assert.Contains("::", output);
        Assert.Contains("file=", output);
        Assert.Contains("line=", output);
        Assert.Contains("col=", output);
    }

    [Fact]
    public void GitHubFormatter_ErrorLevel_UsesErrorAnnotation()
    {
        string source = "break;"; // SA0101 — break outside loop
        var result = BuildCheckResult(source, "test.stash");
        var formatter = new GitHubFormatter();

        using var stream = new MemoryStream();
        formatter.Write(result, stream);
        stream.Position = 0;
        string output = new StreamReader(stream, Encoding.UTF8).ReadToEnd();

        Assert.Contains("::error ", output);
    }

    [Fact]
    public void GitHubFormatter_InfoLevel_UsesNotice()
    {
        string source = "let myVar = 5;"; // SA0205 — could be const (if unused, SA0201)
        var result = BuildCheckResult(source, "test.stash");
        var formatter = new GitHubFormatter();

        using var stream = new MemoryStream();
        formatter.Write(result, stream);
        stream.Position = 0;
        string output = new StreamReader(stream, Encoding.UTF8).ReadToEnd();

        // Info-level diagnostics should use ::notice
        Assert.Contains("::notice ", output);
    }

    [Fact]
    public void GitHubFormatter_FormatString_IsGitHub()
    {
        Assert.Equal("github", new GitHubFormatter().Format);
    }

    // ── Grouped Formatter ─────────────────────────────────────────────

    [Fact]
    public void GroupedFormatter_WithDiagnostics_GroupsPerFile()
    {
        string source = "let MyVar = 5;";
        var result = BuildCheckResult(source, "test.stash");
        var formatter = new GroupedFormatter();

        using var stream = new MemoryStream();
        formatter.Write(result, stream);
        stream.Position = 0;
        string output = new StreamReader(stream, Encoding.UTF8).ReadToEnd();

        // Should have a header line with dashes
        Assert.Contains("──", output);
        Assert.Contains("test.stash", output);
    }

    [Fact]
    public void GroupedFormatter_EmptyResult_NoOutput()
    {
        var result = new CheckResult(new List<FileResult>());
        var formatter = new GroupedFormatter();

        using var stream = new MemoryStream();
        formatter.Write(result, stream);
        stream.Position = 0;
        string output = new StreamReader(stream, Encoding.UTF8).ReadToEnd();

        Assert.Equal("", output.Trim());
    }

    [Fact]
    public void GroupedFormatter_FormatString_IsGrouped()
    {
        Assert.Equal("grouped", new GroupedFormatter().Format);
    }

    // ── CheckOptions — new flags ──────────────────────────────────────

    [Fact]
    public void CheckOptions_Watch_ParsedCorrectly()
    {
        var opts = CheckOptions.Parse(["--watch", "."]);
        Assert.True(opts.Watch);
    }

    [Fact]
    public void CheckOptions_Timing_ParsedCorrectly()
    {
        var opts = CheckOptions.Parse(["--timing", "."]);
        Assert.True(opts.Timing);
    }

    [Fact]
    public void CheckOptions_WatchDefault_False()
    {
        var opts = CheckOptions.Parse(["."]);
        Assert.False(opts.Watch);
    }

    [Fact]
    public void CheckOptions_TimingDefault_False()
    {
        var opts = CheckOptions.Parse(["."]);
        Assert.False(opts.Timing);
    }

    [Fact]
    public void CheckOptions_FormatJson_Accepted()
    {
        var opts = CheckOptions.Parse(["--format", "json", "."]);
        Assert.Equal("json", opts.Format);
    }

    [Fact]
    public void CheckOptions_FormatGitHub_Accepted()
    {
        var opts = CheckOptions.Parse(["--format", "github", "."]);
        Assert.Equal("github", opts.Format);
    }

    [Fact]
    public void CheckOptions_FormatGrouped_Accepted()
    {
        var opts = CheckOptions.Parse(["--format", "grouped", "."]);
        Assert.Equal("grouped", opts.Format);
    }

    // ── Timing — CheckRunner ──────────────────────────────────────────

    [Fact]
    public void CheckRunner_Timing_PopulatedAfterRun()
    {
        string dir = Path.Combine(Path.GetTempPath(), "stash-phase6-timing", Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "test.stash");
        File.WriteAllText(file, "let x = 5;");

        try
        {
            var opts = CheckOptions.Parse(["--timing", file]);
            var runner = new CheckRunner(opts);
            runner.Run();

            Assert.True(runner.LastTiming.Count > 0);
            Assert.Contains(runner.LastTiming, t => t.Pass == "Total");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ── SARIF — RelatedLocations ──────────────────────────────────────

    [Fact]
    public void SarifFormatter_RelatedLocations_IncludedInOutput()
    {
        var span = new SourceSpan("<test>", 5, 1, 5, 10);
        var relSpan = new SourceSpan("<test>", 1, 1, 1, 5);
        var related = new RelatedLocation("see original declaration", relSpan);
        var diag = new SemanticDiagnostic("SA0207", "test msg", DiagnosticLevel.Warning, span)
        {
            RelatedLocations = [related]
        };

        var uri = new Uri("file:///tmp/test.stash");
        var globalScope = new Stash.Analysis.Scope(Stash.Analysis.ScopeKind.Global, null, new SourceSpan("<test>", 0, 0, 0, 0));
        var analysis = new AnalysisResult(
            [],
            [],
            [],
            [],
            [],
            [],
            new ScopeTree(globalScope),
            [diag]);

        var result = new CheckResult([new FileResult(uri, analysis)]);
        var formatter = new SarifFormatter("test", DateTime.UtcNow);

        using var stream = new MemoryStream();
        formatter.Write(result, stream);
        stream.Position = 0;

        var doc = JsonDocument.Parse(stream);
        var results = doc.RootElement
            .GetProperty("runs")[0]
            .GetProperty("results");

        Assert.Equal(1, results.GetArrayLength());
        var sarifResult = results[0];
        Assert.True(sarifResult.TryGetProperty("relatedLocations", out var relLocs));
        Assert.Equal(1, relLocs.GetArrayLength());
        Assert.Contains("see original declaration", relLocs[0].GetProperty("message").GetProperty("text").GetString());
    }

    [Fact]
    public void SarifFormatter_IsDeprecated_WritesDeprecatedTag()
    {
        var span = new SourceSpan("<test>", 1, 1, 1, 5);
        var diag = new SemanticDiagnostic("SA0201", "test msg", DiagnosticLevel.Information, span)
        {
            IsDeprecated = true
        };

        var uri = new Uri("file:///tmp/dep.stash");
        var globalScope = new Stash.Analysis.Scope(Stash.Analysis.ScopeKind.Global, null, new SourceSpan("<test>", 0, 0, 0, 0));
        var analysis = new AnalysisResult(
            [],
            [],
            [],
            [],
            [],
            [],
            new ScopeTree(globalScope),
            [diag]);

        var result = new CheckResult([new FileResult(uri, analysis)]);
        var formatter = new SarifFormatter("test", DateTime.UtcNow);

        using var stream = new MemoryStream();
        formatter.Write(result, stream);
        stream.Position = 0;

        var doc = JsonDocument.Parse(stream);
        var sarifResult = doc.RootElement
            .GetProperty("runs")[0]
            .GetProperty("results")[0];

        Assert.True(sarifResult.TryGetProperty("properties", out var props));
        var tags = props.GetProperty("tags");
        bool hasDeprecated = false;
        foreach (var tag in tags.EnumerateArray())
        {
            if (tag.GetString() == "deprecated")
            {
                hasDeprecated = true;
                break;
            }
        }
        Assert.True(hasDeprecated);
    }

    // ── FixVerifier test helpers ──────────────────────────────────────

    [Fact]
    public void FixVerifier_SA0802_RemovesUnusedImport()
    {
        string source = "import { foo } from \"./mod.stash\";\nio.println(\"hello\");";
        // The fix should remove the import line
        FixVerifier.Verify(source, "io.println(\"hello\");", "SA0802");
    }

    [Fact]
    public void FixVerifier_SA0205_ChangeLetToConst()
    {
        string source = "let x = 5;\nio.println(x);";
        FixVerifier.Verify(source, "const x = 5;\nio.println(x);", "SA0205");
    }

    [Fact]
    public void FixVerifier_VerifyNoFix_NoDiagnostic()
    {
        // SA0202 has no fix
        FixVerifier.VerifyNoFix("io.println(unknownVar);", "SA0202");
    }

    [Fact]
    public void FixVerifier_VerifyNoFix_DiagnosticWithoutFix()
    {
        // SA0201 has no fix
        FixVerifier.VerifyNoFix("let unused = 5;", "SA0201");
    }

    // ── DiagnosticDescriptors — new codes registered ──────────────────

    [Fact]
    public void DiagnosticDescriptors_AllNewCodes_Registered()
    {
        Assert.True(DiagnosticDescriptors.AllByCode.ContainsKey("SA0109"));
        Assert.True(DiagnosticDescriptors.AllByCode.ContainsKey("SA0209"));
        Assert.True(DiagnosticDescriptors.AllByCode.ContainsKey("SA0405"));
        Assert.True(DiagnosticDescriptors.AllByCode.ContainsKey("SA0804"));
    }

    [Fact]
    public void DiagnosticDescriptors_SA0804_IsFixable()
    {
        Assert.True(DiagnosticDescriptors.SA0804.IsFixable);
        Assert.Equal(FixApplicability.Safe, DiagnosticDescriptors.SA0804.DefaultFixApplicability);
    }

    // ── Rule registration count ───────────────────────────────────────

    [Fact]
    public void RuleRegistry_CountIncreasedByFourNewRules()
    {
        var rules = Stash.Analysis.Rules.RuleRegistry.GetAllRules();
        // Was 33 before Analysis & Format spec; now 43 (added 10: SA0901, SA1002, SA1102-SA1108, SA1401, SA1402)
        // Now 46 (added SA1301, SA1302 security rules; NoAccumulatingSpreadRule previously added)
        // Now 47 (added NullFlowRule SA0309)
        // Now 59 (added 12 rules: SA0211, SA0212, SA0311/SA0312, SA0406, SA0407, SA0902, SA1109, SA1110, SA1202, SA1203, SA1303, SA1403)
        // Now 60 (added DeprecatedBuiltInMemberRule SA0830)
        Assert.Equal(60, rules.Count);
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static CheckResult BuildCheckResult(string source, string fileName)
    {
        string dir = Path.Combine(Path.GetTempPath(), "stash-phase6-fmt", Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        string filePath = Path.Combine(dir, fileName);
        File.WriteAllText(filePath, source);

        try
        {
            var opts = CheckOptions.Parse([filePath]);
            var runner = new CheckRunner(opts);
            return runner.Run();
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
