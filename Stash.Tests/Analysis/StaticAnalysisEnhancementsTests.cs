using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Stash.Analysis;
using Xunit;

namespace Stash.Tests.Analysis;

/// <summary>
/// Tests for newly implemented analysis rules:
/// SA0211, SA0212, SA0311, SA0312, SA0406, SA0407, SA0902,
/// SA1109, SA1110, SA1202, SA1203, SA1403, and suppression reason field.
/// </summary>
public class StaticAnalysisEnhancementsTests : AnalysisTestBase
{
    // ── SA0211 — Function Defined Inside Loop Body ────────────────────────────

    [Fact]
    public void SA0211_FunctionInWhileLoop_ReportsWarning()
    {
        var diagnostics = Validate("while (true) { fn helper() { } }");
        Assert.Contains(diagnostics, d => d.Code == "SA0211");
    }

    [Fact]
    public void SA0211_FunctionInForLoop_ReportsWarning()
    {
        var diagnostics = Validate("for (let i = 0; i < 10; i++) { fn helper() { } }");
        Assert.Contains(diagnostics, d => d.Code == "SA0211");
    }

    [Fact]
    public void SA0211_FunctionAtTopLevel_NoWarning()
    {
        var diagnostics = Validate("fn helper() { }");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0211");
    }

    [Fact]
    public void SA0211_LambdaInLoop_NoWarning()
    {
        var diagnostics = Validate("for (let i = 0; i < 10; i++) { let f = fn() { }; }");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0211");
    }

    [Fact]
    public void SA0211_NestedFunctionInsideFunctionInLoop_OnlyOuterFires()
    {
        // SA0211 fires for 'outer' (loopDepth=1, functionDepth=0)
        // but NOT for 'inner' (functionDepth=1 — inside another function)
        var diagnostics = Validate("while (true) { fn outer() { fn inner() { } } }");
        var sa211 = diagnostics.Where(d => d.Code == "SA0211").ToList();
        Assert.Single(sa211); // Only outer fires
    }

    // ── SA0212 — Declaration Shadows Built-in Namespace ───────────────────────

    [Fact]
    public void SA0212_VariableNamedStr_ReportsWarning()
    {
        var diagnostics = Validate("let str = \"hello\";");
        Assert.Contains(diagnostics, d => d.Code == "SA0212");
    }

    [Fact]
    public void SA0212_VariableNamedArr_ReportsWarning()
    {
        var diagnostics = Validate("let arr = [];");
        Assert.Contains(diagnostics, d => d.Code == "SA0212");
    }

    [Fact]
    public void SA0212_VariableNamedStrings_NoWarning()
    {
        var diagnostics = Validate("let strings = [];");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0212");
    }

    [Fact]
    public void SA0212_FunctionParamNamedStr_ReportsWarning()
    {
        var diagnostics = Validate("fn process(str) { }");
        Assert.Contains(diagnostics, d => d.Code == "SA0212");
    }

    [Fact]
    public void SA0212_ConstNamedDict_ReportsWarning()
    {
        var diagnostics = Validate("const dict = {};");
        Assert.Contains(diagnostics, d => d.Code == "SA0212");
    }

    [Fact]
    public void SA0212_FunctionNamedStr_ReportsWarning()
    {
        var diagnostics = Validate("fn str() { }");
        Assert.Contains(diagnostics, d => d.Code == "SA0212");
    }

    // ── SA0311 — Invalid Regex Pattern ────────────────────────────────────────

    [Fact]
    public void SA0311_InvalidRegexInStrMatch_ReportsError()
    {
        var diagnostics = Validate("let r = str.match(input, \"[invalid\");");
        Assert.Contains(diagnostics, d => d.Code == "SA0311");
    }

    [Fact]
    public void SA0311_ValidRegexInStrMatch_NoError()
    {
        var diagnostics = Validate("let r = str.match(input, \"[a-z]+\");");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0311");
    }

    [Fact]
    public void SA0311_InvalidRegexInStrIsMatch_ReportsError()
    {
        var diagnostics = Validate("str.isMatch(input, \"(unclosed\");");
        Assert.Contains(diagnostics, d => d.Code == "SA0311");
    }

    [Fact]
    public void SA0311_EmptyPatternIsValid_NoError()
    {
        var diagnostics = Validate("let r = str.match(input, \"\");");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0311");
    }

    // ── SA0312 — Catastrophic Regex Backtracking ──────────────────────────────

    [Fact]
    public void SA0312_NestedQuantifiers_ReportsWarning()
    {
        var diagnostics = Validate("let r = str.match(input, \"(a+)+\");");
        Assert.Contains(diagnostics, d => d.Code == "SA0312");
    }

    [Fact]
    public void SA0312_SimpleQuantifier_NoWarning()
    {
        var diagnostics = Validate("let r = str.match(input, \"a+b*\");");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0312");
    }

    // ── SA0406 — Async Call Result Not Awaited ────────────────────────────────

    [Fact]
    public void SA0406_AsyncCallNotAwaited_ReportsWarning()
    {
        var source = """
            async fn fetchData() {
                return 42;
            }
            fetchData();
            """;
        var diagnostics = Validate(source);
        Assert.Contains(diagnostics, d => d.Code == "SA0406");
    }

    [Fact]
    public void SA0406_AsyncCallAwaited_NoWarning()
    {
        var source = """
            async fn fetchData() {
                return 42;
            }
            async fn main() {
                await fetchData();
            }
            """;
        var diagnostics = Validate(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0406");
    }

    [Fact]
    public void SA0406_SyncCallNotAwaited_NoWarning()
    {
        var source = """
            fn getData() {
                return 42;
            }
            getData();
            """;
        var diagnostics = Validate(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0406");
    }

    [Fact]
    public void SA0406_ResultCaptured_NoWarning()
    {
        var source = """
            async fn fetchData() {
                return 42;
            }
            let result = fetchData();
            """;
        var diagnostics = Validate(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0406");
    }

    // ── SA0407 — Async Function Without Await ─────────────────────────────────

    [Fact]
    public void SA0407_AsyncFunctionWithNoAwait_ReportsWarning()
    {
        var source = """
            async fn processData() {
                let x = 1 + 2;
                return x;
            }
            """;
        var diagnostics = Validate(source);
        Assert.Contains(diagnostics, d => d.Code == "SA0407");
    }

    [Fact]
    public void SA0407_AsyncFunctionWithAwait_NoWarning()
    {
        var source = """
            async fn fetchData() { return 1; }
            async fn processData() {
                let x = await fetchData();
                return x;
            }
            """;
        var diagnostics = Validate(source);
        // SA0407 should not fire for processData (it has await)
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0407" && d.Message.Contains("processData"));
    }

    [Fact]
    public void SA0407_SyncFunction_NoWarning()
    {
        var source = """
            fn processData() {
                return 42;
            }
            """;
        var diagnostics = Validate(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0407");
    }

    [Fact]
    public void SA0407_AsyncFunctionWithAwaitInTryCatch_NoWarning()
    {
        var source = """
            async fn fetchData() { return 1; }
            async fn safeFetch() {
                try {
                    return await fetchData();
                } catch (e) {
                    return null;
                }
            }
            """;
        var diagnostics = Validate(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0407" && d.Message.Contains("safeFetch"));
    }

    // ── SA0902 — Function Body Too Long ───────────────────────────────────────

    [Fact]
    public void SA0902_FunctionExceedingDefaultThreshold_ReportsInfo()
    {
        var lines = new System.Text.StringBuilder();
        lines.AppendLine("fn bigFunction() {");
        for (int i = 0; i < 63; i++)
        {
            lines.AppendLine($"    let x{i} = {i};");
        }
        lines.AppendLine("}");

        var diagnostics = Validate(lines.ToString());
        Assert.Contains(diagnostics, d => d.Code == "SA0902");
    }

    [Fact]
    public void SA0902_ShortFunction_NoInfo()
    {
        var source = "fn shortFunc() { let x = 1; return x; }";
        var diagnostics = Validate(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0902");
    }

    // ── SA1109 — Assignment Used as Condition ─────────────────────────────────

    [Fact]
    public void SA1109_AssignInIfCondition_ReportsWarning()
    {
        var diagnostics = Validate("let x = 0; if (x = 5) { }");
        Assert.Contains(diagnostics, d => d.Code == "SA1109");
    }

    [Fact]
    public void SA1109_AssignInWhileCondition_ReportsWarning()
    {
        var diagnostics = Validate("let x = 0; while (x = 1) { }");
        Assert.Contains(diagnostics, d => d.Code == "SA1109");
    }

    [Fact]
    public void SA1109_ComparisonInIf_NoWarning()
    {
        var diagnostics = Validate("let x = 0; if (x == 5) { }");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA1109");
    }

    [Fact]
    public void SA1109_AssignInGroupedCondition_ReportsWarning()
    {
        var diagnostics = Validate("let x = 0; if ((x = 5)) { }");
        Assert.Contains(diagnostics, d => d.Code == "SA1109");
    }

    // ── SA1110 — Magic Number ─────────────────────────────────────────────────

    [Fact]
    public void SA1110_RepeatedMagicNumber_ReportsInfo()
    {
        var source = """
            let a = 42;
            let b = 42;
            """;
        var diagnostics = Validate(source);
        Assert.Contains(diagnostics, d => d.Code == "SA1110");
    }

    [Fact]
    public void SA1110_ExemptedNumber_NoInfo()
    {
        // 0, 1, -1, 2, 100 are exempt
        var source = """
            let a = 0;
            let b = 0;
            let c = 1;
            let d = 1;
            """;
        var diagnostics = Validate(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA1110");
    }

    [Fact]
    public void SA1110_NumberAppearsOnce_NoInfo()
    {
        var source = "let a = 42;";
        var diagnostics = Validate(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA1110");
    }

    [Fact]
    public void SA1110_NumberInConstDecl_NoInfo()
    {
        // Number appears in a const (the fix) and once outside — total outside const is 1 occurrence
        var source = """
            const TIMEOUT = 3600;
            let a = 3600;
            """;
        var diagnostics = Validate(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA1110");
    }

    // ── SA1202 — String Concatenation in Loop ─────────────────────────────────

    [Fact]
    public void SA1202_StringConcatInForLoop_ReportsWarning()
    {
        var source = """
            let s = "";
            for (let i = 0; i < 10; i++) {
                s = s + "item";
            }
            """;
        var diagnostics = Validate(source);
        Assert.Contains(diagnostics, d => d.Code == "SA1202");
    }

    [Fact]
    public void SA1202_StringConcatInWhileLoop_ReportsWarning()
    {
        var source = """
            let s = "";
            let i = 0;
            while (i < 10) {
                s = s + "x";
                i = i + 1;
            }
            """;
        var diagnostics = Validate(source);
        Assert.Contains(diagnostics, d => d.Code == "SA1202");
    }

    [Fact]
    public void SA1202_StringConcatOutsideLoop_NoWarning()
    {
        var source = """
            let s = "";
            s = s + "item";
            """;
        var diagnostics = Validate(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA1202");
    }

    [Fact]
    public void SA1202_DifferentVariableConcat_NoWarning()
    {
        var source = """
            let s = "";
            let other = "";
            for (let i = 0; i < 10; i++) {
                other = s + "x";
            }
            """;
        var diagnostics = Validate(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA1202");
    }

    // ── SA1203 — Repeated Function Call in Loop Condition ─────────────────────

    [Fact]
    public void SA1203_LenCallInForCondition_ReportsInfo()
    {
        var source = """
            let items = [1, 2, 3];
            for (let i = 0; i < items.len(); i++) {
                io.println(items[i]);
            }
            """;
        var diagnostics = Validate(source);
        Assert.Contains(diagnostics, d => d.Code == "SA1203");
    }

    [Fact]
    public void SA1203_LenCallInWhileCondition_ReportsInfo()
    {
        var source = """
            let items = [1, 2, 3];
            let i = 0;
            while (i < items.len()) {
                i = i + 1;
            }
            """;
        var diagnostics = Validate(source);
        Assert.Contains(diagnostics, d => d.Code == "SA1203");
    }

    [Fact]
    public void SA1203_ReceiverMutatedInLoop_NoInfo()
    {
        var source = """
            let items = [1, 2, 3];
            while (items.len() > 0) {
                items = [];
            }
            """;
        var diagnostics = Validate(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA1203");
    }

    // ── SA1403 — Prefer String Interpolation ──────────────────────────────────

    [Fact]
    public void SA1403_StringLiteralPlusVar_ReportsInfo()
    {
        var diagnostics = Validate("let name = \"world\"; let s = \"Hello \" + name;");
        Assert.Contains(diagnostics, d => d.Code == "SA1403");
    }

    [Fact]
    public void SA1403_TwoLiterals_NoInfo()
    {
        var diagnostics = Validate("let s = \"Hello \" + \"world\";");
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA1403");
    }

    [Fact]
    public void SA1403_StringConcatInLoop_NoInfo()
    {
        // SA1403 should not fire in loops — SA1202 already covers that case
        var source = """
            let name = "world";
            let s = "";
            for (let i = 0; i < 10; i++) {
                s = s + name;
            }
            """;
        var diagnostics = Validate(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA1403");
    }

    // ── Suppression Reason Field ──────────────────────────────────────────────

    [Fact]
    public void SuppressionDirective_WithQuotedReason_DoesNotEmitSA0002()
    {
        // A quoted reason after the code should be silently accepted, not treated as a malformed code
        var source = """
            // stash-disable-next-line SA0201 "kept for side-effect"
            let _unused = 42;
            """;
        var engine = new AnalysisEngine(NullLogger<AnalysisEngine>.Instance);
        var uri = new Uri("file:///test.stash");
        var result = engine.Analyze(uri, source, noImports: true);
        Assert.DoesNotContain(result.SemanticDiagnostics, d => d.Code == "SA0002");
    }
}
