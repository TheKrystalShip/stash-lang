using Stash.Lexing;
using Stash.Parsing;
using Stash.Interpreting;
using Stash.Tap;

namespace Stash.Tests.Interpreting;

public class TestDiscoveryTests
{
    // Helper for discovery mode
    private static (TapReporter reporter, string output) RunDiscovery(string source, string? currentFile = null)
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
        interpreter.DiscoveryMode = true;
        interpreter.Interpret(statements);
        reporter.OnRunComplete(reporter.PassedCount, reporter.FailedCount, reporter.SkippedCount);
        return (reporter, sw.ToString());
    }

    // Helper for running with harness and optional file
    private static (TapReporter reporter, string output) RunWithHarness(string source, string? currentFile = null)
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
        interpreter.Interpret(statements);
        reporter.OnRunComplete(reporter.PassedCount, reporter.FailedCount, reporter.SkippedCount);
        return (reporter, sw.ToString());
    }

    // ── 1A. Fully qualified names include filename ───────────────────────────

    [Fact]
    public void FullyQualifiedName_TopLevelTest_IncludesFilename()
    {
        var (_, output) = RunWithHarness("""
            test.it("addition", () => { assert.true(true); });
            """, "math.test.stash");

        Assert.Contains("ok 1 - math.test.stash > addition", output);
    }

    [Fact]
    public void FullyQualifiedName_DescribeTest_IncludesFilename()
    {
        var (_, output) = RunWithHarness("""
            test.describe("math", () => {
                test.it("addition", () => { assert.true(true); });
            });
            """, "math.test.stash");

        Assert.Contains("ok 1 - math.test.stash > math > addition", output);
    }

    [Fact]
    public void FullyQualifiedName_NestedDescribes_IncludesFilename()
    {
        var (_, output) = RunWithHarness("""
            test.describe("outer", () => {
                test.describe("inner", () => {
                    test.it("check", () => { assert.true(true); });
                });
            });
            """, "test.stash");

        Assert.Contains("ok 1 - test.stash > outer > inner > check", output);
    }

    [Fact]
    public void FullyQualifiedName_NoFile_UsesUnknown()
    {
        var (_, output) = RunWithHarness("""
            test.it("check", () => { assert.true(true); });
            """);

        Assert.Contains("ok 1 - unknown > check", output);
    }

    [Fact]
    public void FullyQualifiedName_AbsolutePath_UsesOnlyFilename()
    {
        var (_, output) = RunWithHarness("""
            test.it("check", () => { assert.true(true); });
            """, "/home/user/project/tests/math.test.stash");

        Assert.Contains("ok 1 - math.test.stash > check", output);
    }

    [Fact]
    public void FullyQualifiedName_SuiteComment_IncludesFilename()
    {
        var (_, output) = RunWithHarness("""
            test.describe("math", () => {
                test.it("check", () => { assert.true(true); });
            });
            """, "test.stash");

        Assert.Contains("# test.stash > math", output);
    }

    // ── 1C. Discovery mode ───────────────────────────────────────────────────

    [Fact]
    public void Discovery_EmitsDiscoveredComments()
    {
        var (_, output) = RunDiscovery("""
            test.it("addition", () => { assert.equal(1+1, 2); });
            test.it("subtraction", () => { assert.equal(3-1, 2); });
            """, "math.test.stash");

        Assert.Contains("# discovered: math.test.stash > addition", output);
        Assert.Contains("# discovered: math.test.stash > subtraction", output);
    }

    [Fact]
    public void Discovery_DoesNotExecuteTestBodies()
    {
        // If test bodies executed, the assertion would fail. In discovery mode, they don't.
        var (reporter, _) = RunDiscovery("""
            test.it("will fail if run", () => { assert.equal(1, 2); });
            """, "test.stash");

        // No tests should have passed or failed — bodies weren't executed
        Assert.Equal(0, reporter.PassedCount);
        Assert.Equal(0, reporter.FailedCount);
    }

    [Fact]
    public void Discovery_PlanLineIsZero()
    {
        var (_, output) = RunDiscovery("""
            test.it("check", () => { assert.true(true); });
            """, "test.stash");

        Assert.Contains("1..0", output);
    }

    [Fact]
    public void Discovery_DescribeBlocksStillExecute()
    {
        // test.describe() bodies execute so nested tests are discovered
        var (_, output) = RunDiscovery("""
            test.describe("math", () => {
                test.it("add", () => { assert.true(true); });
                test.it("sub", () => { assert.true(true); });
            });
            """, "test.stash");

        Assert.Contains("# discovered: test.stash > math > add", output);
        Assert.Contains("# discovered: test.stash > math > sub", output);
    }

    [Fact]
    public void Discovery_NestedDescribes_FullyQualified()
    {
        var (_, output) = RunDiscovery("""
            test.describe("outer", () => {
                test.describe("inner", () => {
                    test.it("check", () => { assert.true(true); });
                });
            });
            """, "test.stash");

        Assert.Contains("# discovered: test.stash > outer > inner > check", output);
    }

    [Fact]
    public void Discovery_TapHeader_Written()
    {
        var (_, output) = RunDiscovery("""
            test.it("check", () => { assert.true(true); });
            """, "test.stash");

        Assert.Contains("TAP version 14", output);
    }

    [Fact]
    public void Discovery_DynamicTests_Discovered()
    {
        // Dynamic test generation via loop — discovery mode still discovers them
        // because test.describe() bodies execute, and test.it() still registers names
        var (_, output) = RunDiscovery("""
            let items = ["a", "b", "c"];
            for (let item in items) {
                test.it(item, () => { assert.true(true); });
            }
            """, "test.stash");

        Assert.Contains("# discovered: test.stash > a", output);
        Assert.Contains("# discovered: test.stash > b", output);
        Assert.Contains("# discovered: test.stash > c", output);
    }

    [Fact]
    public void Discovery_WithFilter_OnlyMatchingDiscovered()
    {
        // When both discovery mode and filter are active, only matching tests are discovered
        var lexer = new Lexer("""
            test.it("alpha", () => { assert.true(true); });
            test.it("beta", () => { assert.true(true); });
            """);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        interpreter.CurrentFile = "test.stash";
        var sw = new StringWriter();
        var reporter = new TapReporter(sw);
        interpreter.TestHarness = reporter;
        interpreter.DiscoveryMode = true;
        interpreter.TestFilter = new[] { "test.stash > alpha" };
        interpreter.Interpret(statements);
        reporter.OnRunComplete(reporter.PassedCount, reporter.FailedCount, reporter.SkippedCount);
        var output = sw.ToString();

        Assert.Contains("# discovered: test.stash > alpha", output);
        Assert.DoesNotContain("beta", output);
    }

    // ── Interpreter properties ───────────────────────────────────────────────

    [Fact]
    public void TestFilter_DefaultsToNull()
    {
        var interpreter = new Interpreter();
        Assert.Null(interpreter.TestFilter);
    }

    [Fact]
    public void DiscoveryMode_DefaultsToFalse()
    {
        var interpreter = new Interpreter();
        Assert.False(interpreter.DiscoveryMode);
    }

    [Fact]
    public void TestFilter_CanBeSetAndRead()
    {
        var interpreter = new Interpreter();
        interpreter.TestFilter = new[] { "filter1", "filter2" };
        Assert.Equal(new[] { "filter1", "filter2" }, interpreter.TestFilter);
    }

    [Fact]
    public void DiscoveryMode_CanBeSetAndRead()
    {
        var interpreter = new Interpreter();
        interpreter.DiscoveryMode = true;
        Assert.True(interpreter.DiscoveryMode);
    }
}
