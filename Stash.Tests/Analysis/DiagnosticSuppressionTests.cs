using Stash.Lexing;
using Stash.Parsing;
using Stash.Analysis;
using Stash.Common;

namespace Stash.Tests.Analysis;

public class DiagnosticSuppressionTests : AnalysisTestBase
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static List<SemanticDiagnostic> ValidateWithSuppression(string source)
    {
        var lexer = new Lexer(source, "<test>", preserveTrivia: true);
        var tokens = lexer.ScanTokens();
        var parserTokens = tokens.Where(t => t.Type is not (
            TokenType.DocComment or
            TokenType.SingleLineComment or
            TokenType.BlockComment or
            TokenType.Shebang)).ToList();
        var parser = new Parser(parserTokens);
        var stmts = parser.ParseProgram();
        var collector = new SymbolCollector();
        var scopeTree = collector.Collect(stmts);
        var validator = new SemanticValidator(scopeTree);
        var diagnostics = validator.Validate(stmts);
        var suppressionMap = SuppressionDirectiveParser.Parse(tokens);
        return suppressionMap.Filter(diagnostics);
    }

    private static SuppressionMap ParseSuppressions(string source)
    {
        var lexer = new Lexer(source, "<test>", preserveTrivia: true);
        var tokens = lexer.ScanTokens();
        return SuppressionDirectiveParser.Parse(tokens);
    }

    // ── 1. Diagnostic Codes on Existing Diagnostics ──────────────────────────

    [Fact]
    public void BreakOutsideLoop_HasCode_SA0101()
    {
        var diagnostics = Validate("break;");
        var d = Assert.Single(diagnostics);
        Assert.Equal("SA0101", d.Code);
    }

    [Fact]
    public void ContinueOutsideLoop_HasCode_SA0102()
    {
        var diagnostics = Validate("continue;");
        var d = Assert.Single(diagnostics);
        Assert.Equal("SA0102", d.Code);
    }

    [Fact]
    public void ReturnOutsideFunction_HasCode_SA0103()
    {
        var diagnostics = Validate("return 1;");
        var d = Assert.Single(diagnostics);
        Assert.Equal("SA0103", d.Code);
    }

    [Fact]
    public void UnreachableCode_HasCode_SA0104()
    {
        var diagnostics = Validate("fn test() { return 1; let x = 2; }");
        Assert.Contains(diagnostics, d => d.Code == "SA0104");
    }

    [Fact]
    public void UnusedVariable_HasCode_SA0201()
    {
        var diagnostics = Validate("fn test() { let x = 5; }");
        Assert.Contains(diagnostics, d => d.Code == "SA0201");
    }

    [Fact]
    public void UndefinedReference_HasCode_SA0202()
    {
        var diagnostics = Validate("fn test() { io.println(x); }");
        Assert.Contains(diagnostics, d => d.Code == "SA0202" && d.Message.Contains("'x'"));
    }

    [Fact]
    public void ConstantReassignment_HasCode_SA0203()
    {
        var diagnostics = Validate("fn test() { const x = 5; x = 10; }");
        Assert.Contains(diagnostics, d => d.Code == "SA0203");
    }

    [Fact]
    public void UnknownType_HasCode_SA0303()
    {
        var diagnostics = Validate("fn test() { let x: FooBar = 5; }");
        Assert.Contains(diagnostics, d => d.Code == "SA0303");
    }

    // ── 2. Suppression Directive Parsing ─────────────────────────────────────

    [Fact]
    public void DisableNextLine_SuppressesSingleDiagnostic()
    {
        var source = """
            // stash-disable-next-line SA0101
            break;
            """;
        var diagnostics = ValidateWithSuppression(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0101");
    }

    [Fact]
    public void DisableNextLine_MultipleSeparateDirectives_SuppressesBoth()
    {
        var source = """
            // stash-disable-next-line SA0101
            break;
            // stash-disable-next-line SA0102
            continue;
            """;
        var diagnostics = ValidateWithSuppression(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0101");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0102");
    }

    [Fact]
    public void DisableNextLine_CommaSeparated_SuppressesBoth()
    {
        var source = """
            // stash-disable-next-line SA0101, SA0102
            break;
            """;
        var diagnostics = ValidateWithSuppression(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0101");
    }

    [Fact]
    public void DisableNextLine_NoCode_SuppressesAll()
    {
        var source = """
            // stash-disable-next-line
            break;
            """;
        var diagnostics = ValidateWithSuppression(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0101");
    }

    [Fact]
    public void DisableNextLine_WrongCode_DoesNotSuppress()
    {
        var source = """
            // stash-disable-next-line SA0202
            break;
            """;
        var diagnostics = ValidateWithSuppression(source);
        Assert.Contains(diagnostics, d => d.Code == "SA0101");
    }

    [Fact]
    public void DisableNextLine_DoesNotBleedPastTarget()
    {
        var source = """
            // stash-disable-next-line SA0101
            break;
            continue;
            """;
        var diagnostics = ValidateWithSuppression(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0101");
        Assert.Contains(diagnostics, d => d.Code == "SA0102");
    }

    [Fact]
    public void DisableLine_SameLineComment_Suppresses()
    {
        var source = "break; // stash-disable-line SA0101";
        var diagnostics = ValidateWithSuppression(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0101");
    }

    [Fact]
    public void DisableLine_SameLineComment_NoCode_SuppressesAll()
    {
        var source = "break; // stash-disable-line";
        var diagnostics = ValidateWithSuppression(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0101");
    }

    [Fact]
    public void DisableBlock_SuppressesRange()
    {
        var source = """
            // stash-disable SA0101
            break;
            break;
            // stash-restore SA0101
            """;
        var diagnostics = ValidateWithSuppression(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0101");
    }

    [Fact]
    public void DisableBlock_WithoutRestore_SuppressesToEnd()
    {
        var source = """
            // stash-disable SA0101
            break;
            break;
            break;
            """;
        var diagnostics = ValidateWithSuppression(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0101");
    }

    [Fact]
    public void DisableBlock_RestoreReopens()
    {
        var source = """
            // stash-disable SA0101
            break;
            // stash-restore SA0101
            break;
            """;
        var diagnostics = ValidateWithSuppression(source);
        // The second break (after restore) should NOT be suppressed
        Assert.Contains(diagnostics, d => d.Code == "SA0101");
    }

    [Fact]
    public void DisableBlock_NoCode_SuppressesAllInRange()
    {
        var source = """
            // stash-disable
            break;
            continue;
            // stash-restore
            """;
        var diagnostics = ValidateWithSuppression(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0101");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0102");
    }

    // ── 3. Directive Validation (SA0001, SA0002) ──────────────────────────────

    [Fact]
    public void UnknownCode_EmitsSA0001()
    {
        var source = """
            // stash-disable-next-line SA9999
            break;
            """;
        var diagnostics = ValidateWithSuppression(source);
        Assert.Contains(diagnostics, d => d.Code == "SA0001" && d.Message.Contains("SA9999"));
    }

    [Fact]
    public void MalformedCode_EmitsSA0002()
    {
        var source = """
            // stash-disable-next-line SAXXXX
            break;
            """;
        var diagnostics = ValidateWithSuppression(source);
        Assert.Contains(diagnostics, d => d.Code == "SA0002" && d.Message.Contains("SAXXXX"));
    }

    [Fact]
    public void UnknownCode_DoesNotSuppressOtherDiagnostics()
    {
        // Unknown code SA9999 produces SA0001 warning but does NOT suppress other diagnostics
        var source = """
            // stash-disable-next-line SA9999
            break;
            """;
        var diagnostics = ValidateWithSuppression(source);
        // SA0101 is NOT suppressed — SA9999 is unknown, so the directive suppresses nothing
        Assert.Contains(diagnostics, d => d.Code == "SA0101");
        Assert.Contains(diagnostics, d => d.Code == "SA0001");
    }

    [Fact]
    public void BlockComment_NotRecognizedAsDirective()
    {
        // Block comments should NOT be parsed as suppression directives
        var source = """
            /* stash-disable SA0101 */
            break;
            """;
        var diagnostics = ValidateWithSuppression(source);
        Assert.Contains(diagnostics, d => d.Code == "SA0101");
    }

    [Fact]
    public void ParseSuppressions_NoDirectives_EmptyMap()
    {
        var map = ParseSuppressions("let x = 5;");
        Assert.Empty(map.DirectiveDiagnostics);
    }

    // ── 4. ProjectConfig Tests ────────────────────────────────────────────────

    [Fact]
    public void ProjectConfig_DisableCode_SuppressesDiagnostic()
    {
        var config = ProjectConfig.ParseContent("disable = SA0201");
        Assert.True(config.DisabledCodes.Contains("SA0201"));
    }

    [Fact]
    public void ProjectConfig_SeverityOverride_PromotesToError()
    {
        var config = ProjectConfig.ParseContent("severity.SA0303 = error");
        var diag = new SemanticDiagnostic("SA0303", "Unknown type 'Foo'.", DiagnosticLevel.Warning,
            new SourceSpan("<test>", 1, 1, 1, 5));
        var result = config.Apply(new List<SemanticDiagnostic> { diag });
        var d = Assert.Single(result);
        Assert.Equal(DiagnosticLevel.Error, d.Level);
    }

    [Fact]
    public void ProjectConfig_SeverityOverride_DemotesToInformation()
    {
        var config = ProjectConfig.ParseContent("severity.SA0101 = info");
        var diag = new SemanticDiagnostic("SA0101", "'break' used outside of a loop.", DiagnosticLevel.Error,
            new SourceSpan("<test>", 1, 1, 1, 5));
        var result = config.Apply(new List<SemanticDiagnostic> { diag });
        var d = Assert.Single(result);
        Assert.Equal(DiagnosticLevel.Information, d.Level);
    }

    [Fact]
    public void ProjectConfig_SeverityOff_RemovesDiagnostic()
    {
        var config = ProjectConfig.ParseContent("severity.SA0303 = off");
        var diag = new SemanticDiagnostic("SA0303", "Unknown type 'Foo'.", DiagnosticLevel.Warning,
            new SourceSpan("<test>", 1, 1, 1, 5));
        var result = config.Apply(new List<SemanticDiagnostic> { diag });
        Assert.Empty(result);
    }

    [Fact]
    public void ProjectConfig_Empty_ReturnsAllDiagnostics()
    {
        var config = ProjectConfig.ParseContent("");
        var diag = new SemanticDiagnostic("SA0101", "'break' used outside of a loop.", DiagnosticLevel.Error,
            new SourceSpan("<test>", 1, 1, 1, 5));
        var result = config.Apply(new List<SemanticDiagnostic> { diag });
        Assert.Single(result);
    }

    [Fact]
    public void ProjectConfig_CommentsIgnored()
    {
        var config = ProjectConfig.ParseContent("# This is a comment\ndisable = SA0201");
        Assert.True(config.DisabledCodes.Contains("SA0201"));
    }

    [Fact]
    public void ProjectConfig_MultipleCodes_AllDisabled()
    {
        var config = ProjectConfig.ParseContent("disable = SA0201, SA0202");
        Assert.True(config.DisabledCodes.Contains("SA0201"));
        Assert.True(config.DisabledCodes.Contains("SA0202"));
    }

    [Fact]
    public void ProjectConfig_RequireReason_DefaultFalse()
    {
        var config = ProjectConfig.ParseContent("");
        Assert.False(config.RequireSuppressionReason);
    }

    [Fact]
    public void ProjectConfig_RequireReason_True()
    {
        var config = ProjectConfig.ParseContent("require-suppression-reason = true");
        Assert.True(config.RequireSuppressionReason);
    }

    [Fact]
    public void ProjectConfig_Apply_PreservesCode()
    {
        var config = ProjectConfig.ParseContent("severity.SA0202 = warning");
        var diag = new SemanticDiagnostic("SA0202", "'x' is not defined.", DiagnosticLevel.Warning,
            new SourceSpan("<test>", 1, 1, 1, 5));
        var result = config.Apply(new List<SemanticDiagnostic> { diag });
        var d = Assert.Single(result);
        Assert.Equal("SA0202", d.Code);
    }

    [Fact]
    public void ProjectConfig_Apply_NullCode_NotAffectedByDisable()
    {
        var config = ProjectConfig.ParseContent("disable = SA0201");
        // Legacy diagnostic with no code
        var diag = new SemanticDiagnostic("some message", DiagnosticLevel.Warning,
            new SourceSpan("<test>", 1, 1, 1, 5));
        var result = config.Apply(new List<SemanticDiagnostic> { diag });
        Assert.Single(result);
    }

    // ── 5. DiagnosticDescriptor Registry Tests ────────────────────────────────

    [Fact]
    public void AllByCode_ContainsAllRegistered()
    {
        Assert.True(DiagnosticDescriptors.AllByCode.ContainsKey("SA0001"));
        Assert.True(DiagnosticDescriptors.AllByCode.ContainsKey("SA0002"));
        Assert.True(DiagnosticDescriptors.AllByCode.ContainsKey("SA0101"));
        Assert.True(DiagnosticDescriptors.AllByCode.ContainsKey("SA0102"));
        Assert.True(DiagnosticDescriptors.AllByCode.ContainsKey("SA0103"));
        Assert.True(DiagnosticDescriptors.AllByCode.ContainsKey("SA0104"));
        Assert.True(DiagnosticDescriptors.AllByCode.ContainsKey("SA0201"));
        Assert.True(DiagnosticDescriptors.AllByCode.ContainsKey("SA0202"));
        Assert.True(DiagnosticDescriptors.AllByCode.ContainsKey("SA0203"));
        Assert.True(DiagnosticDescriptors.AllByCode.ContainsKey("SA0301"));
        Assert.True(DiagnosticDescriptors.AllByCode.ContainsKey("SA0302"));
        Assert.True(DiagnosticDescriptors.AllByCode.ContainsKey("SA0303"));
        Assert.True(DiagnosticDescriptors.AllByCode.ContainsKey("SA0304"));
        Assert.True(DiagnosticDescriptors.AllByCode.ContainsKey("SA0401"));
        Assert.True(DiagnosticDescriptors.AllByCode.ContainsKey("SA0701"));
    }

    [Fact]
    public void DiagnosticDescriptor_FormatMessage_WithArgs()
    {
        var descriptor = DiagnosticDescriptors.SA0202;
        var message = descriptor.FormatMessage("myVar");
        Assert.Equal("'myVar' is not defined.", message);
    }

    [Fact]
    public void DiagnosticDescriptor_FormatMessage_NoArgs()
    {
        var descriptor = DiagnosticDescriptors.SA0101;
        var message = descriptor.FormatMessage();
        Assert.Equal("'break' used outside of a loop.", message);
    }

    [Fact]
    public void DiagnosticDescriptor_FormatMessage_MultipleArgs()
    {
        var descriptor = DiagnosticDescriptors.SA0401;
        var message = descriptor.FormatMessage(2, 3);
        Assert.Equal("Expected 2 arguments but got 3.", message);
    }

    [Fact]
    public void DiagnosticDescriptor_Properties_Set()
    {
        var descriptor = DiagnosticDescriptors.SA0101;
        Assert.Equal("SA0101", descriptor.Code);
        Assert.Equal(DiagnosticLevel.Error, descriptor.DefaultLevel);
        Assert.Equal("Control flow", descriptor.Category);
        Assert.False(string.IsNullOrEmpty(descriptor.Title));
    }

    [Fact]
    public void AllByCode_LookupReturnsCorrectDescriptor()
    {
        var descriptor = DiagnosticDescriptors.AllByCode["SA0201"];
        Assert.Equal("SA0201", descriptor.Code);
        Assert.Equal(DiagnosticLevel.Information, descriptor.DefaultLevel);
    }

    // ── 6. SuppressionMap Unit Tests ──────────────────────────────────────────

    [Fact]
    public void SuppressionMap_LineSuppression_IsSuppressed()
    {
        var map = new SuppressionMap();
        map.AddLineSuppression(5, new HashSet<string> { "SA0101" });
        Assert.True(map.IsSuppressed("SA0101", 5));
        Assert.False(map.IsSuppressed("SA0101", 6));
        Assert.False(map.IsSuppressed("SA0201", 5));
    }

    [Fact]
    public void SuppressionMap_NullCodes_SuppressesAll()
    {
        var map = new SuppressionMap();
        map.AddLineSuppression(5, null);
        Assert.True(map.IsSuppressed("SA0101", 5));
        Assert.True(map.IsSuppressed("SA0201", 5));
        Assert.False(map.IsSuppressed("SA0101", 4));
    }

    [Fact]
    public void SuppressionMap_RangeSuppression_Works()
    {
        var map = new SuppressionMap();
        map.AddRangeSuppression(5, 10, new HashSet<string> { "SA0101" });
        Assert.False(map.IsSuppressed("SA0101", 4));
        Assert.True(map.IsSuppressed("SA0101", 5));
        Assert.True(map.IsSuppressed("SA0101", 7));
        Assert.True(map.IsSuppressed("SA0101", 10));
        Assert.False(map.IsSuppressed("SA0101", 11));
    }

    [Fact]
    public void SuppressionMap_RangeSuppression_DoesNotAffectOtherCodes()
    {
        var map = new SuppressionMap();
        map.AddRangeSuppression(5, 10, new HashSet<string> { "SA0101" });
        Assert.False(map.IsSuppressed("SA0201", 7));
    }

    [Fact]
    public void SuppressionMap_OpenRange_SuppressesToEnd()
    {
        var map = new SuppressionMap();
        map.AddRangeSuppression(5, null, new HashSet<string> { "SA0101" });
        Assert.True(map.IsSuppressed("SA0101", 100));
        Assert.True(map.IsSuppressed("SA0101", 10000));
        Assert.False(map.IsSuppressed("SA0101", 4));
    }

    [Fact]
    public void SuppressionMap_OpenRange_NullCodes_SuppressesAllToEnd()
    {
        var map = new SuppressionMap();
        map.AddRangeSuppression(1, null, null);
        Assert.True(map.IsSuppressed("SA0101", 1));
        Assert.True(map.IsSuppressed("SA0999", 999));
    }

    [Fact]
    public void SuppressionMap_Filter_RemovesSuppressed()
    {
        var map = new SuppressionMap();
        map.AddLineSuppression(5, new HashSet<string> { "SA0101" });
        var diagnostics = new List<SemanticDiagnostic>
        {
            new("SA0101", "break outside", DiagnosticLevel.Error, new SourceSpan("<test>", 5, 1, 5, 6)),
            new("SA0102", "continue outside", DiagnosticLevel.Error, new SourceSpan("<test>", 5, 1, 5, 6)),
            new("SA0101", "break outside", DiagnosticLevel.Error, new SourceSpan("<test>", 10, 1, 10, 6)),
        };
        var result = map.Filter(diagnostics);
        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result, d => d.Code == "SA0101" && d.Span.StartLine == 5);
        Assert.Contains(result, d => d.Code == "SA0102");
        Assert.Contains(result, d => d.Code == "SA0101" && d.Span.StartLine == 10);
    }

    [Fact]
    public void SuppressionMap_Filter_IncludesDirectiveDiagnostics()
    {
        var map = new SuppressionMap();
        map.AddDirectiveDiagnostic(new SemanticDiagnostic("SA0001", "Unknown code.", DiagnosticLevel.Warning,
            new SourceSpan("<test>", 1, 1, 1, 30)));
        var diagnostics = new List<SemanticDiagnostic>();
        var result = map.Filter(diagnostics);
        Assert.Single(result);
        Assert.Equal("SA0001", result[0].Code);
    }

    [Fact]
    public void SuppressionMap_LineSuppression_AccumulateCodes()
    {
        // Adding two suppressions for the same line should merge codes
        var map = new SuppressionMap();
        map.AddLineSuppression(3, new HashSet<string> { "SA0101" });
        map.AddLineSuppression(3, new HashSet<string> { "SA0102" });
        Assert.True(map.IsSuppressed("SA0101", 3));
        Assert.True(map.IsSuppressed("SA0102", 3));
        Assert.False(map.IsSuppressed("SA0201", 3));
    }

    [Fact]
    public void SuppressionMap_Empty_NothingSuppressed()
    {
        var map = new SuppressionMap();
        Assert.False(map.IsSuppressed("SA0101", 1));
        Assert.False(map.IsSuppressed(null, 1));
    }
}
