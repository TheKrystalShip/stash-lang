using Stash.Lexing;
using Stash.Parsing;
using Stash.Interpreting;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Testing;

namespace Stash.Tests.Interpreting;

public class TestFilterTests
{
    // Helper that runs with harness AND filter, optionally with a CurrentFile
    private static (TapReporter reporter, string output) RunWithFilter(string source, string[] filter, string? currentFile = null)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        if (currentFile is not null)
        {
            interpreter.CurrentFile = currentFile;
        }

        var sw = new StringWriter();
        var reporter = new TapReporter(sw);
        interpreter.TestHarness = reporter;
        interpreter.TestFilter = filter;
        interpreter.Interpret(statements);
        reporter.OnRunComplete(reporter.Passed, reporter.Failed, reporter.Skipped);
        return (reporter, sw.ToString());
    }

    // ── Exact match ──────────────────────────────────────────────────────────

    [Fact]
    public void Filter_ExactMatch_RunsOnlyMatchingTest()
    {
        var (reporter, output) = RunWithFilter("""
            test.it("add", () => { assert.true(true); });
            test.it("subtract", () => { assert.true(true); });
            """,
            ["test.stash > add"],
            "test.stash");

        Assert.Equal(1, reporter.Passed);
        Assert.Contains("test.stash > add", output);
        Assert.DoesNotContain("subtract", output);
    }

    [Fact]
    public void Filter_ExactMatch_WithDescribe()
    {
        var (reporter, output) = RunWithFilter("""
            test.describe("math", () => {
                test.it("add", () => { assert.true(true); });
                test.it("sub", () => { assert.true(true); });
            });
            """,
            ["test.stash > math > add"],
            "test.stash");

        Assert.Equal(1, reporter.Passed);
        Assert.Contains("math > add", output);
        Assert.DoesNotContain("sub", output);
    }

    // ── Prefix match (describe block filter) ─────────────────────────────────

    [Fact]
    public void Filter_PrefixMatch_RunsEntireBlock()
    {
        var (reporter, _) = RunWithFilter("""
            test.describe("math", () => {
                test.it("add", () => { assert.true(true); });
                test.it("sub", () => { assert.true(true); });
            });
            test.describe("strings", () => {
                test.it("concat", () => { assert.true(true); });
            });
            """,
            ["test.stash > math"],
            "test.stash");

        Assert.Equal(2, reporter.Passed);
    }

    // ── Multiple filters (semicolon-separated -> array) ──────────────────────

    [Fact]
    public void Filter_MultiplePatterns_RunsAllMatching()
    {
        var (reporter, output) = RunWithFilter("""
            test.it("alpha", () => { assert.true(true); });
            test.it("beta", () => { assert.true(true); });
            test.it("gamma", () => { assert.true(true); });
            """,
            ["test.stash > alpha", "test.stash > gamma"],
            "test.stash");

        Assert.Equal(2, reporter.Passed);
        Assert.DoesNotContain("beta", output);
    }

    // ── Filtered-out tests are silent ────────────────────────────────────────

    [Fact]
    public void Filter_NoMatch_ProducesNoOutput()
    {
        var (reporter, output) = RunWithFilter("""
            test.it("existing", () => { assert.true(true); });
            """,
            ["test.stash > nonexistent"],
            "test.stash");

        Assert.Equal(0, reporter.Passed);
        Assert.Equal(0, reporter.Failed);
        Assert.Contains("1..0", output); // Plan line should show 0 tests
    }

    // ── Describe block is skipped entirely when no internal tests match ──────

    [Fact]
    public void Filter_DescribeSkipped_WhenNoChildrenMatch()
    {
        var (_, output) = RunWithFilter("""
            test.describe("math", () => {
                test.it("add", () => { assert.true(true); });
            });
            test.describe("strings", () => {
                test.it("concat", () => { assert.true(true); });
            });
            """,
            ["test.stash > strings > concat"],
            "test.stash");

        // "math" describe block should be entirely skipped
        Assert.DoesNotContain("# test.stash > math", output);
        Assert.Contains("# test.stash > strings", output);
    }

    // ── Nested describes with filter ─────────────────────────────────────────

    [Fact]
    public void Filter_NestedDescribes_PrefixMatchIncludesAll()
    {
        var (reporter, _) = RunWithFilter("""
            test.describe("outer", () => {
                test.describe("inner", () => {
                    test.it("a", () => { assert.true(true); });
                    test.it("b", () => { assert.true(true); });
                });
            });
            """,
            ["test.stash > outer"],
            "test.stash");

        Assert.Equal(2, reporter.Passed);
    }

    // ── Filter with failing test ─────────────────────────────────────────────

    [Fact]
    public void Filter_MatchingFailingTest_RecordsFail()
    {
        var (reporter, _) = RunWithFilter("""
            test.it("bad", () => { assert.equal(1, 2); });
            test.it("good", () => { assert.true(true); });
            """,
            ["test.stash > bad"],
            "test.stash");

        Assert.Equal(0, reporter.Passed);
        Assert.Equal(1, reporter.Failed);
    }

    // ── Default (no filter) ──────────────────────────────────────────────────

    [Fact]
    public void NoFilter_AllTestsRun()
    {
        // When TestFilter is null, all tests should run
        var lexer = new Lexer("""
            test.it("a", () => { assert.true(true); });
            test.it("b", () => { assert.true(true); });
            test.it("c", () => { assert.true(true); });
            """);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        var sw = new StringWriter();
        var reporter = new TapReporter(sw);
        interpreter.TestHarness = reporter;
        // TestFilter is NOT set — should be null by default
        interpreter.Interpret(statements);
        reporter.OnRunComplete(reporter.Passed, reporter.Failed, reporter.Skipped);

        Assert.Equal(3, reporter.Passed);
    }
}
