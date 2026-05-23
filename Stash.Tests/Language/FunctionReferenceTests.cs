namespace Stash.Tests.Language;

using Stash.Tests.Interpreting;

/// <summary>
/// Regression tests for the first-class function reference behavior documented in the
/// Language Specification §11 (Function References).
///
/// These tests pin the contract: bare access to a Function-kind namespace entry yields
/// a callable value; the value can be captured, passed, and invoked; typeof returns
/// "function". No parentheses are needed to obtain the reference.
/// </summary>
public class FunctionReferenceTests : StashTestBase
{
    // ── 1. io namespace ──────────────────────────────────────────────────────

    [Fact]
    public void FunctionReference_IoPrintln_CaptureAndCall_Prints()
    {
        string output = RunCapturingOutput(
            "let p = io.println; p(\"hello\");");
        Assert.Equal("hello\n", output);
    }

    [Fact]
    public void FunctionReference_IoPrintln_TypeofIsFunction()
    {
        var result = Run("let result = typeof(io.println);");
        Assert.Equal("function", result);
    }

    [Fact]
    public void FunctionReference_IoPrintln_BareAccessDoesNotInvoke()
    {
        // Accessing io.println bare should not produce output — it only returns
        // the function reference without calling it.
        string output = RunCapturingOutput("io.println;");
        Assert.Equal(string.Empty, output);
    }

    // ── 2. str namespace ─────────────────────────────────────────────────────

    [Fact]
    public void FunctionReference_StrUpper_CaptureAndCall_ReturnsUppercase()
    {
        var result = Run("let upper = str.upper; let result = upper(\"abc\");");
        Assert.Equal("ABC", result);
    }

    [Fact]
    public void FunctionReference_StrUpper_TypeofIsFunction()
    {
        var result = Run("let result = typeof(str.upper);");
        Assert.Equal("function", result);
    }

    [Fact]
    public void FunctionReference_StrTrim_CaptureAndCall_TrimsWhitespace()
    {
        var result = Run("let trim = str.trim; let result = trim(\"  hi  \");");
        Assert.Equal("hi", result);
    }

    // ── 3. math namespace ────────────────────────────────────────────────────

    [Fact]
    public void FunctionReference_MathAbs_CaptureAndCall_ReturnsPositive()
    {
        var result = Run("let abs = math.abs; let result = abs(-42);");
        Assert.Equal(42L, result);
    }

    [Fact]
    public void FunctionReference_MathAbs_TypeofIsFunction()
    {
        var result = Run("let result = typeof(math.abs);");
        Assert.Equal("function", result);
    }

    [Fact]
    public void FunctionReference_MathMax_CaptureAndCall_ReturnsMax()
    {
        var result = Run("let mx = math.max; let result = mx(3, 7);");
        Assert.Equal(7L, result);
    }

    // ── 4. Cross-namespace dispatch ──────────────────────────────────────────

    [Fact]
    public void FunctionReference_CrossNamespace_PassedAsArgument()
    {
        // Capture str.upper and pass it to a user function.
        var result = Run(
            "fn apply(f, s) { return f(s); }" +
            "let result = apply(str.upper, \"world\");");
        Assert.Equal("WORLD", result);
    }

    [Fact]
    public void FunctionReference_MultipleNamespaces_AllCallable()
    {
        // Three captures from three namespaces, all invoked successfully.
        string output = RunCapturingOutput(
            "let p = io.println;" +
            "let u = str.upper;" +
            "let a = math.abs;" +
            "p(u(\"hello\"));" +
            "p(a(-99));");
        Assert.Equal("HELLO\n99\n", output);
    }

    // ── 5. DataMember entries are NOT function references ────────────────────

    [Fact]
    public void FunctionReference_DataMember_TypeofIsNotFunction()
    {
        // cli.argc is a DataMember: bare access returns an int, not "function".
        var result = Run("let result = typeof(cli.argc);");
        Assert.NotEqual("function", result);
    }

    [Fact]
    public void FunctionReference_DataMember_BareAccessYieldsValue()
    {
        // cli.argc is an int member, not a callable.
        // It returns the count of ScriptArgs (may be 0 in the test harness).
        var result = Run("let result = cli.argc;");
        Assert.IsType<long>(result);
    }
}
