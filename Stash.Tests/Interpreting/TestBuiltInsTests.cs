using Stash.Lexing;
using Stash.Parsing;
using Stash.Interpreting;
using Stash.Testing;

namespace Stash.Tests.Interpreting;

public class TestBuiltInsTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void RunStatements(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        interpreter.Interpret(statements);
    }

    private static object? Run(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        interpreter.Interpret(statements);
        var resultLexer = new Lexer("result");
        var resultTokens = resultLexer.ScanTokens();
        var resultParser = new Parser(resultTokens);
        var resultExpr = resultParser.Parse();
        return interpreter.Interpret(resultExpr);
    }

    private static (TapReporter reporter, string output) RunWithHarness(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        var sw = new StringWriter();
        var reporter = new TapReporter(sw);
        interpreter.TestHarness = reporter;
        interpreter.Interpret(statements);
        reporter.OnRunComplete(reporter.Passed, reporter.Failed, reporter.Skipped);
        return (reporter, sw.ToString());
    }

    private static (TapReporter reporter, string output, Interpreter interpreter) RunWithHarnessAndInterpreter(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        var sw = new StringWriter();
        var reporter = new TapReporter(sw);
        interpreter.TestHarness = reporter;
        interpreter.Interpret(statements);
        reporter.OnRunComplete(reporter.Passed, reporter.Failed, reporter.Skipped);
        return (reporter, sw.ToString(), interpreter);
    }

    // ── 1. Assert passing (no exception) ─────────────────────────────────────

    [Fact]
    public void AssertEqual_SameIntegers_NoError()
    {
        RunStatements("assert.equal(1, 1);");
    }

    [Fact]
    public void AssertEqual_SameStrings_NoError()
    {
        RunStatements("assert.equal(\"hello\", \"hello\");");
    }

    [Fact]
    public void AssertNotEqual_DifferentValues_NoError()
    {
        RunStatements("assert.notEqual(1, 2);");
    }

    [Fact]
    public void AssertTrue_TrueBoolean_NoError()
    {
        RunStatements("assert.true(true);");
    }

    [Fact]
    public void AssertTrue_TruthyInteger_NoError()
    {
        RunStatements("assert.true(1);");
    }

    [Fact]
    public void AssertFalse_FalseBoolean_NoError()
    {
        RunStatements("assert.false(false);");
    }

    [Fact]
    public void AssertFalse_ZeroIsfalsy_NoError()
    {
        RunStatements("assert.false(0);");
    }

    [Fact]
    public void AssertFalse_EmptyStringIsFalsy_NoError()
    {
        RunStatements("assert.false(\"\");");
    }

    [Fact]
    public void AssertNull_NullValue_NoError()
    {
        RunStatements("assert.null(null);");
    }

    [Fact]
    public void AssertNotNull_NonNullValue_NoError()
    {
        RunStatements("assert.notNull(42);");
    }

    [Fact]
    public void AssertGreater_GreaterValue_NoError()
    {
        RunStatements("assert.greater(5, 3);");
    }

    [Fact]
    public void AssertLess_LessValue_NoError()
    {
        RunStatements("assert.less(3, 5);");
    }

    // ── 2. Assert failing (throw AssertionError) ──────────────────────────────

    [Fact]
    public void AssertEqual_DifferentIntegers_ThrowsAssertionError()
    {
        Assert.Throws<AssertionError>(() => RunStatements("assert.equal(1, 2);"));
    }

    [Fact]
    public void AssertEqual_IntegerAndString_ThrowsAssertionError()
    {
        // No type coercion: 5 != "5"
        Assert.Throws<AssertionError>(() => RunStatements("assert.equal(5, \"5\");"));
    }

    [Fact]
    public void AssertNotEqual_EqualValues_ThrowsAssertionError()
    {
        Assert.Throws<AssertionError>(() => RunStatements("assert.notEqual(1, 1);"));
    }

    [Fact]
    public void AssertTrue_FalseBoolean_ThrowsAssertionError()
    {
        Assert.Throws<AssertionError>(() => RunStatements("assert.true(false);"));
    }

    [Fact]
    public void AssertTrue_Null_ThrowsAssertionError()
    {
        Assert.Throws<AssertionError>(() => RunStatements("assert.true(null);"));
    }

    [Fact]
    public void AssertFalse_TrueBoolean_ThrowsAssertionError()
    {
        Assert.Throws<AssertionError>(() => RunStatements("assert.false(true);"));
    }

    [Fact]
    public void AssertNull_NonNullValue_ThrowsAssertionError()
    {
        Assert.Throws<AssertionError>(() => RunStatements("assert.null(42);"));
    }

    [Fact]
    public void AssertNotNull_Null_ThrowsAssertionError()
    {
        Assert.Throws<AssertionError>(() => RunStatements("assert.notNull(null);"));
    }

    [Fact]
    public void AssertGreater_SmallerFirst_ThrowsAssertionError()
    {
        Assert.Throws<AssertionError>(() => RunStatements("assert.greater(3, 5);"));
    }

    [Fact]
    public void AssertLess_GreaterFirst_ThrowsAssertionError()
    {
        Assert.Throws<AssertionError>(() => RunStatements("assert.less(5, 3);"));
    }

    [Fact]
    public void AssertFail_AlwaysThrowsAssertionError()
    {
        var ex = Assert.Throws<AssertionError>(() => RunStatements("assert.fail(\"oops\");"));
        Assert.Equal("oops", ex.Message);
    }

    // ── 3. assert.throws ──────────────────────────────────────────────────────

    [Fact]
    public void AssertThrows_FunctionThatThrows_ReturnsErrorMessage()
    {
        var result = Run("let result = assert.throws(() => { let x = 1 / 0; });");
        Assert.IsType<string>(result);
        Assert.NotEmpty((string)result!);
    }

    [Fact]
    public void AssertThrows_FunctionThatDoesNotThrow_ThrowsAssertionError()
    {
        Assert.Throws<AssertionError>(() =>
            RunStatements("assert.throws(() => { let x = 1; });"));
    }

    // ── 4. test() with harness ────────────────────────────────────────────────

    [Fact]
    public void TestFunction_PassingAssertion_RecordsPass()
    {
        var (reporter, _) = RunWithHarness("""
            test("adds correctly", () => {
                assert.equal(1 + 1, 2);
            });
            """);

        Assert.Equal(1, reporter.Passed);
        Assert.Equal(0, reporter.Failed);
    }

    [Fact]
    public void TestFunction_FailingAssertion_RecordsFail()
    {
        var (reporter, _) = RunWithHarness("""
            test("broken math", () => {
                assert.equal(1, 2);
            });
            """);

        Assert.Equal(0, reporter.Passed);
        Assert.Equal(1, reporter.Failed);
    }

    [Fact]
    public void TestFunction_MultipleTests_AllRecorded()
    {
        var (reporter, _) = RunWithHarness("""
            test("passes", () => { assert.true(true); });
            test("also passes", () => { assert.equal(2, 2); });
            test("fails", () => { assert.equal(1, 99); });
            """);

        Assert.Equal(2, reporter.Passed);
        Assert.Equal(1, reporter.Failed);
    }

    [Fact]
    public void TestFunction_FailingAssertion_DoesNotCrashSubsequentTests()
    {
        var (reporter, _) = RunWithHarness("""
            test("fails first", () => { assert.equal(0, 1); });
            test("still runs", () => { assert.true(true); });
            """);

        Assert.Equal(1, reporter.Passed);
        Assert.Equal(1, reporter.Failed);
    }

    // ── 5. describe() grouping ────────────────────────────────────────────────

    [Fact]
    public void Describe_PrefixesTestNames()
    {
        var (_, output) = RunWithHarness("""
            describe("math", () => {
                test("addition", () => { assert.equal(1 + 1, 2); });
            });
            """);

        Assert.Contains("math > addition", output);
    }

    [Fact]
    public void Describe_NestedDescribes_ChainNames()
    {
        var (_, output) = RunWithHarness("""
            describe("outer", () => {
                describe("inner", () => {
                    test("check", () => { assert.true(true); });
                });
            });
            """);

        Assert.Contains("outer > inner > check", output);
    }

    [Fact]
    public void Describe_MultipleTestsInGroup_AllPrefixed()
    {
        var (reporter, output) = RunWithHarness("""
            describe("strings", () => {
                test("concat", () => { assert.equal("a" + "b", "ab"); });
                test("length", () => { assert.greater(3, 0); });
            });
            """);

        Assert.Equal(2, reporter.Passed);
        Assert.Contains("strings > concat", output);
        Assert.Contains("strings > length", output);
    }

    [Fact]
    public void Describe_RestoresContextAfterGroup()
    {
        // Tests outside describe should not be prefixed
        var (_, output) = RunWithHarness("""
            describe("group", () => {
                test("inside", () => { assert.true(true); });
            });
            test("outside", () => { assert.true(true); });
            """);

        Assert.Contains("group > inside", output);
        Assert.Contains("ok", output);
        // "outside" should appear without a prefix
        Assert.Contains("outside", output);
        Assert.DoesNotContain("group > outside", output);
    }

    // ── 6. TapReporter output format ──────────────────────────────────────────

    [Fact]
    public void TapReporter_PassingTest_OutputsOkLine()
    {
        var (_, output) = RunWithHarness("""
            test("my test", () => { assert.equal(1, 1); });
            """);

        Assert.Contains("ok 1 - unknown > my test", output);
    }

    [Fact]
    public void TapReporter_FailingTest_OutputsNotOkLine()
    {
        var (_, output) = RunWithHarness("""
            test("bad test", () => { assert.equal(1, 2); });
            """);

        Assert.Contains("not ok 1 - unknown > bad test", output);
    }

    [Fact]
    public void TapReporter_FailingTest_OutputsYamlBlock()
    {
        var (_, output) = RunWithHarness("""
            test("bad test", () => { assert.equal(1, 2); });
            """);

        Assert.Contains("---", output);
        Assert.Contains("message:", output);
        Assert.Contains("severity: fail", output);
        Assert.Contains("...", output);
    }

    [Fact]
    public void TapReporter_PlanLine_ReflectsTestCount()
    {
        var (_, output) = RunWithHarness("""
            test("one", () => { assert.true(true); });
            test("two", () => { assert.true(true); });
            test("three", () => { assert.true(true); });
            """);

        Assert.Contains("1..3", output);
    }

    [Fact]
    public void TapReporter_Header_WrittenOnce()
    {
        var (_, output) = RunWithHarness("""
            test("a", () => { assert.true(true); });
            test("b", () => { assert.true(true); });
            """);

        Assert.Equal(1, output.Split("TAP version 14").Length - 1);
    }

    [Fact]
    public void TapReporter_SuiteComment_WrittenForDescribe()
    {
        var (_, output) = RunWithHarness("""
            describe("my suite", () => {
                test("check", () => { assert.true(true); });
            });
            """);

        Assert.Contains("# unknown > my suite", output);
    }

    // ── 7. test() without harness ─────────────────────────────────────────────

    [Theory]
    [InlineData("test(\"t\", () => { assert.equal(1, 2); });")]
    [InlineData("test(\"t\", () => { assert.true(false); });")]
    [InlineData("test(\"t\", () => { assert.fail(\"boom\"); });")]
    public void TestFunction_WithoutHarness_AssertionFailureCrashes(string source)
    {
        Assert.Throws<AssertionError>(() => RunStatements(source));
    }

    [Fact]
    public void TestFunction_WithoutHarness_PassingTestRunsCleanly()
    {
        // Should not throw
        RunStatements("test(\"t\", () => { assert.equal(1, 1); });");
    }

    #region Output Capture Tests

    [Fact]
    public void CaptureOutput_CapturesPrintln()
    {
        var result = Run("""
            let result = captureOutput(() => {
                io.println("hello");
            });
            """);
        Assert.Equal("hello\n", result);
    }

    [Fact]
    public void CaptureOutput_CapturesPrint()
    {
        var result = Run("""
            let result = captureOutput(() => {
                io.print("world");
            });
            """);
        Assert.Equal("world", result);
    }

    [Fact]
    public void CaptureOutput_CapturesMultipleOutputs()
    {
        var result = Run("""
            let result = captureOutput(() => {
                io.println("line1");
                io.print("no-newline");
                io.println("line2");
            });
            """);
        Assert.Equal("line1\nno-newlineline2\n", result);
    }

    [Fact]
    public void CaptureOutput_ReturnsEmptyStringWhenNoOutput()
    {
        var result = Run("""
            let result = captureOutput(() => {
                let x = 42;
            });
            """);
        Assert.Equal("", result);
    }

    [Fact]
    public void CaptureOutput_RestoresOutputAfterException()
    {
        var source = """
            captureOutput(() => {
                io.println("before error");
                assert.fail("intentional");
            });
            """;
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        var originalWriter = new StringWriter();
        interpreter.Output = originalWriter;

        Assert.ThrowsAny<RuntimeError>(() => interpreter.Interpret(statements));
        Assert.Same(originalWriter, interpreter.Output);
    }

    [Fact]
    public void CaptureOutput_RequiresFunctionArgument()
    {
        Assert.Throws<RuntimeError>(() => RunStatements("captureOutput(42);"));
    }

    [Fact]
    public void CaptureOutput_NestedCapture()
    {
        var result = Run("""
            let result = captureOutput(() => {
                io.print("outer-");
                let inner = captureOutput(() => {
                    io.print("inner");
                });
                io.print(inner);
            });
            """);
        Assert.Equal("outer-inner", result);
    }

    [Fact]
    public void InterpreterOutput_DefaultsToConsoleOut()
    {
        var interpreter = new Interpreter();
        Assert.Same(Console.Out, interpreter.Output);
        Assert.Same(Console.Error, interpreter.ErrorOutput);
    }

    [Fact]
    public void InterpreterOutput_CanBeReplaced()
    {
        var source = """io.println("test output");""";
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseProgram();
        var interpreter = new Interpreter();
        var sw = new StringWriter();
        interpreter.Output = sw;
        interpreter.Interpret(statements);
        Assert.Equal("test output\n", sw.ToString());
    }

    #endregion

    // ── 8. skip() — skipped tests ─────────────────────────────────────────

    [Fact]
    public void Skip_RecordsSkippedTest()
    {
        var (reporter, _) = RunWithHarness("""
            skip("work in progress", () => {
                assert.fail("should not run");
            });
            """);

        Assert.Equal(0, reporter.Passed);
        Assert.Equal(0, reporter.Failed);
        Assert.Equal(1, reporter.Skipped);
    }

    [Fact]
    public void Skip_DoesNotExecuteBody()
    {
        // If the body ran, it would throw and fail
        var (reporter, _) = RunWithHarness("""
            skip("not ready", () => {
                assert.equal(1, 2);
            });
            """);

        Assert.Equal(0, reporter.Failed);
        Assert.Equal(1, reporter.Skipped);
    }

    [Fact]
    public void Skip_EmitsTapSkipDirective()
    {
        var (_, output) = RunWithHarness("""
            skip("pending feature", () => {});
            """);

        Assert.Contains("# SKIP", output);
        Assert.Contains("pending feature", output);
    }

    [Fact]
    public void Skip_InsideDescribe_UsesFullName()
    {
        var (_, output) = RunWithHarness("""
            describe("math", () => {
                skip("division by zero", () => {});
            });
            """);

        Assert.Contains("math > division by zero", output);
        Assert.Contains("# SKIP", output);
    }

    [Fact]
    public void Skip_MixedWithTests_AllCounted()
    {
        var (reporter, _) = RunWithHarness("""
            test("passes", () => { assert.true(true); });
            skip("skipped", () => {});
            test("also passes", () => { assert.equal(1, 1); });
            """);

        Assert.Equal(2, reporter.Passed);
        Assert.Equal(0, reporter.Failed);
        Assert.Equal(1, reporter.Skipped);
    }

    [Fact]
    public void Skip_PlanLineIncludesSkippedTests()
    {
        var (_, output) = RunWithHarness("""
            test("one", () => { assert.true(true); });
            skip("two", () => {});
            test("three", () => { assert.true(true); });
            """);

        Assert.Contains("1..3", output);
    }

    [Fact]
    public void Skip_WithoutHarness_DoesNotCrash()
    {
        // skip() without a harness should silently do nothing
        RunStatements("""
            skip("no harness", () => { assert.fail("boom"); });
            """);
    }

    // ── 9. Lifecycle hooks ────────────────────────────────────────────────

    [Fact]
    public void BeforeEach_RunsBeforeEachTest()
    {
        var result = Run("""
            let result = [];
            describe("hooks", () => {
                beforeEach(() => { arr.push(result, "setup"); });
                test("a", () => { arr.push(result, "a"); });
                test("b", () => { arr.push(result, "b"); });
            });
            """);

        var list = (List<object?>)result!;
        Assert.Equal(4, list.Count);
        Assert.Equal("setup", list[0]);
        Assert.Equal("a", list[1]);
        Assert.Equal("setup", list[2]);
        Assert.Equal("b", list[3]);
    }

    [Fact]
    public void AfterEach_RunsAfterEachTest()
    {
        var result = Run("""
            let result = [];
            describe("hooks", () => {
                afterEach(() => { arr.push(result, "cleanup"); });
                test("a", () => { arr.push(result, "a"); });
                test("b", () => { arr.push(result, "b"); });
            });
            """);

        var list = (List<object?>)result!;
        Assert.Equal(4, list.Count);
        Assert.Equal("a", list[0]);
        Assert.Equal("cleanup", list[1]);
        Assert.Equal("b", list[2]);
        Assert.Equal("cleanup", list[3]);
    }

    [Fact]
    public void BeforeAll_RunsOnceBeforeTests()
    {
        var result = Run("""
            let result = [];
            describe("hooks", () => {
                beforeAll(() => { arr.push(result, "init"); });
                test("a", () => { arr.push(result, "a"); });
                test("b", () => { arr.push(result, "b"); });
            });
            """);

        var list = (List<object?>)result!;
        Assert.Equal(3, list.Count);
        Assert.Equal("init", list[0]);
        Assert.Equal("a", list[1]);
        Assert.Equal("b", list[2]);
    }

    [Fact]
    public void AfterAll_RunsOnceAfterAllTests()
    {
        var result = Run("""
            let result = [];
            describe("hooks", () => {
                afterAll(() => { arr.push(result, "done"); });
                test("a", () => { arr.push(result, "a"); });
                test("b", () => { arr.push(result, "b"); });
            });
            """);

        var list = (List<object?>)result!;
        Assert.Equal(3, list.Count);
        Assert.Equal("a", list[0]);
        Assert.Equal("b", list[1]);
        Assert.Equal("done", list[2]);
    }

    [Fact]
    public void BeforeEach_NestedDescribe_InheritsParentHooks()
    {
        var result = Run("""
            let result = [];
            describe("outer", () => {
                beforeEach(() => { arr.push(result, "outer-setup"); });
                describe("inner", () => {
                    beforeEach(() => { arr.push(result, "inner-setup"); });
                    test("check", () => { arr.push(result, "test"); });
                });
            });
            """);

        var list = (List<object?>)result!;
        Assert.Equal(3, list.Count);
        Assert.Equal("outer-setup", list[0]);
        Assert.Equal("inner-setup", list[1]);
        Assert.Equal("test", list[2]);
    }

    [Fact]
    public void AfterEach_NestedDescribe_RunsInnermostFirst()
    {
        var result = Run("""
            let result = [];
            describe("outer", () => {
                afterEach(() => { arr.push(result, "outer-cleanup"); });
                describe("inner", () => {
                    afterEach(() => { arr.push(result, "inner-cleanup"); });
                    test("check", () => { arr.push(result, "test"); });
                });
            });
            """);

        var list = (List<object?>)result!;
        Assert.Equal(3, list.Count);
        Assert.Equal("test", list[0]);
        Assert.Equal("inner-cleanup", list[1]);
        Assert.Equal("outer-cleanup", list[2]);
    }

    [Fact]
    public void Hooks_OutsideDescribe_ThrowsRuntimeError()
    {
        Assert.Throws<RuntimeError>(() =>
            RunStatements("beforeEach(() => {});"));
        Assert.Throws<RuntimeError>(() =>
            RunStatements("afterEach(() => {});"));
        Assert.Throws<RuntimeError>(() =>
            RunStatements("beforeAll(() => {});"));
        Assert.Throws<RuntimeError>(() =>
            RunStatements("afterAll(() => {});"));
    }

    [Fact]
    public void Hooks_RequireFunctionArgument()
    {
        Assert.Throws<RuntimeError>(() =>
            RunStatements("""describe("x", () => { beforeEach(42); });"""));
        Assert.Throws<RuntimeError>(() =>
            RunStatements("""describe("x", () => { afterEach(42); });"""));
        Assert.Throws<RuntimeError>(() =>
            RunStatements("""describe("x", () => { beforeAll(42); });"""));
        Assert.Throws<RuntimeError>(() =>
            RunStatements("""describe("x", () => { afterAll(42); });"""));
    }

    [Fact]
    public void BeforeEach_AfterEach_FullLifecycle()
    {
        var result = Run("""
            let result = [];
            describe("lifecycle", () => {
                beforeAll(() => { arr.push(result, "before-all"); });
                beforeEach(() => { arr.push(result, "before-each"); });
                afterEach(() => { arr.push(result, "after-each"); });
                afterAll(() => { arr.push(result, "after-all"); });
                test("one", () => { arr.push(result, "test-1"); });
                test("two", () => { arr.push(result, "test-2"); });
            });
            """);

        var list = (List<object?>)result!;
        // Expected order: before-all, before-each, test-1, after-each, before-each, test-2, after-each, after-all
        Assert.Equal(8, list.Count);
        Assert.Equal("before-all", list[0]);
        Assert.Equal("before-each", list[1]);
        Assert.Equal("test-1", list[2]);
        Assert.Equal("after-each", list[3]);
        Assert.Equal("before-each", list[4]);
        Assert.Equal("test-2", list[5]);
        Assert.Equal("after-each", list[6]);
        Assert.Equal("after-all", list[7]);
    }

    [Fact]
    public void Hooks_DoNotLeakBetweenDescribeBlocks()
    {
        var result = Run("""
            let result = [];
            describe("first", () => {
                beforeEach(() => { arr.push(result, "first-hook"); });
                test("a", () => { arr.push(result, "a"); });
            });
            describe("second", () => {
                test("b", () => { arr.push(result, "b"); });
            });
            """);

        var list = (List<object?>)result!;
        Assert.Equal(3, list.Count);
        Assert.Equal("first-hook", list[0]);
        Assert.Equal("a", list[1]);
        Assert.Equal("b", list[2]);
    }

    [Fact]
    public void AfterAll_RunsEvenIfTestFails()
    {
        var (reporter, _, interp) = RunWithHarnessAndInterpreter("""
            let cleaned = false;
            describe("cleanup", () => {
                afterAll(() => { cleaned = true; });
                test("fails", () => { assert.equal(1, 2); });
            });
            """);

        Assert.Equal(1, reporter.Failed);
        var resultLexer = new Lexer("cleaned");
        var resultTokens = resultLexer.ScanTokens();
        var resultParser = new Parser(resultTokens);
        var resultExpr = resultParser.Parse();
        var cleaned = interp.Interpret(resultExpr);
        Assert.Equal(true, cleaned);
    }
}
