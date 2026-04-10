using Stash.Analysis;
using Xunit;

namespace Stash.Tests.Analysis;

/// <summary>
/// Tests for performance analysis rules: SA1201.
/// </summary>
public class PerformanceRuleTests : AnalysisTestBase
{

    // ── SA1201 — NoAccumulatingSpread ────────────────────────────────

    [Fact]
    public void SA1201_ArraySpreadInForInLoop_ReportsWarning()
    {
        string source = """
            let items = [1, 2, 3];
            let result = [];
            for (let item in items) {
                result = [...result, item];
            }
            """;
        var diagnostics = Validate(source);
        Assert.Contains(diagnostics, d => d.Code == "SA1201");
    }

    [Fact]
    public void SA1201_DictSpreadInWhileLoop_ReportsWarning()
    {
        string source = """
            let items = [{"a": 1}];
            let merged = {};
            for (let item in items) {
                merged = {...merged, ...item};
            }
            """;
        var diagnostics = Validate(source);
        Assert.Contains(diagnostics, d => d.Code == "SA1201");
    }

    [Fact]
    public void SA1201_ArraySpreadInForLoop_ReportsWarning()
    {
        string source = """
            let result = [];
            for (let i = 0; i < 10; i = i + 1) {
                result = [...result, i];
            }
            """;
        var diagnostics = Validate(source);
        Assert.Contains(diagnostics, d => d.Code == "SA1201");
    }

    [Fact]
    public void SA1201_ArraySpreadInDoWhileLoop_ReportsWarning()
    {
        string source = """
            let result = [];
            let i = 0;
            do {
                result = [...result, i];
                i = i + 1;
            } while (i < 3);
            """;
        var diagnostics = Validate(source);
        Assert.Contains(diagnostics, d => d.Code == "SA1201");
    }

    [Fact]
    public void SA1201_SpreadOutsideLoop_NoReport()
    {
        string source = """
            let items = [1, 2, 3];
            let result = [...items, 4];
            """;
        var diagnostics = Validate(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA1201");
    }

    [Fact]
    public void SA1201_SpreadOfDifferentVariable_NoReport()
    {
        string source = """
            let items = [1, 2, 3];
            let other = [0];
            let result = [];
            for (let item in items) {
                result = [...other, item];
            }
            """;
        var diagnostics = Validate(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA1201");
    }

    [Fact]
    public void SA1201_RegularArrayPushInLoop_NoReport()
    {
        string source = """
            let items = [1, 2, 3];
            let result = [];
            for (let item in items) {
                arr.push(result, item);
            }
            """;
        var diagnostics = Validate(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA1201");
    }

    [Fact]
    public void SA1201_DictSpreadOfDifferentVariable_NoReport()
    {
        string source = """
            let base = {"x": 1};
            let merged = {};
            let items = [{"a": 1}];
            for (let item in items) {
                merged = {...base, ...item};
            }
            """;
        var diagnostics = Validate(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA1201");
    }
}
