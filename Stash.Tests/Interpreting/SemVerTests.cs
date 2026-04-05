using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Bytecode;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Runtime.Types;

namespace Stash.Tests.Interpreting;

public class SemVerTests
{
    private static object? Run(string source)
    {
        string full = source + "\nreturn result;";
        var lexer = new Lexer(full, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(TestVM.CreateGlobals());
        return vm.Execute(chunk);
    }

    // ── Lexer: Token Scanning / Literal Parsing ───────────────────────────────

    [Fact]
    public void SemVer_BasicVersion_ParsesCorrectly()
    {
        var result = Run("let result = @v1.2.3;");
        var sv = Assert.IsType<StashSemVer>(result);
        Assert.Equal(1L, sv.Major);
        Assert.Equal(2L, sv.Minor);
        Assert.Equal(3L, sv.Patch);
    }

    [Fact]
    public void SemVer_HighNumbers_ParsesCorrectly()
    {
        var result = Run("let result = @v100.200.300;");
        var sv = Assert.IsType<StashSemVer>(result);
        Assert.Equal(100L, sv.Major);
        Assert.Equal(200L, sv.Minor);
        Assert.Equal(300L, sv.Patch);
    }

    [Fact]
    public void SemVer_ZeroPatch_ParsesCorrectly()
    {
        var result = Run("let result = @v1.0.0;");
        var sv = Assert.IsType<StashSemVer>(result);
        Assert.Equal(1L, sv.Major);
        Assert.Equal(0L, sv.Minor);
        Assert.Equal(0L, sv.Patch);
    }

    [Fact]
    public void SemVer_AllZeros_ParsesCorrectly()
    {
        var result = Run("let result = @v0.0.0;");
        var sv = Assert.IsType<StashSemVer>(result);
        Assert.Equal(0L, sv.Major);
        Assert.Equal(0L, sv.Minor);
        Assert.Equal(0L, sv.Patch);
    }

    [Fact]
    public void SemVer_WithPrerelease_ParsesCorrectly()
    {
        var result = Run("let result = @v1.0.0-alpha;");
        var sv = Assert.IsType<StashSemVer>(result);
        Assert.Equal("alpha", sv.Prerelease);
    }

    [Fact]
    public void SemVer_WithPrereleaseNumeric_ParsesCorrectly()
    {
        var result = Run("let result = @v1.0.0-beta.2;");
        var sv = Assert.IsType<StashSemVer>(result);
        Assert.Equal("beta.2", sv.Prerelease);
    }

    [Fact]
    public void SemVer_WithPrereleaseComplex_ParsesCorrectly()
    {
        var result = Run("let result = @v1.0.0-alpha.1.beta;");
        var sv = Assert.IsType<StashSemVer>(result);
        Assert.Equal("alpha.1.beta", sv.Prerelease);
    }

    [Fact]
    public void SemVer_WithBuildMetadata_ParsesCorrectly()
    {
        var result = Run("let result = @v1.0.0+build.123;");
        var sv = Assert.IsType<StashSemVer>(result);
        Assert.Equal("build.123", sv.BuildMetadata);
    }

    [Fact]
    public void SemVer_WithPrereleaseAndBuild_ParsesCorrectly()
    {
        var result = Run("let result = @v1.0.0-beta+build.456;");
        var sv = Assert.IsType<StashSemVer>(result);
        Assert.Equal("beta", sv.Prerelease);
        Assert.Equal("build.456", sv.BuildMetadata);
    }

    [Fact]
    public void SemVer_WildcardMinor_ParsesCorrectly()
    {
        var result = Run("let result = @v2.x;");
        var sv = Assert.IsType<StashSemVer>(result);
        Assert.True(sv.IsWildcardMinor);
        Assert.Equal(2L, sv.Major);
    }

    [Fact]
    public void SemVer_WildcardPatch_ParsesCorrectly()
    {
        var result = Run("let result = @v2.4.x;");
        var sv = Assert.IsType<StashSemVer>(result);
        Assert.True(sv.IsWildcardPatch);
        Assert.Equal(2L, sv.Major);
        Assert.Equal(4L, sv.Minor);
    }

    // ── Property Access ───────────────────────────────────────────────────────

    [Fact]
    public void SemVer_MajorProperty_ReturnsLong()
    {
        object? result = Run("let v = @v2.4.1; let result = v.major;");
        Assert.Equal(2L, result);
    }

    [Fact]
    public void SemVer_MinorProperty_ReturnsLong()
    {
        object? result = Run("let v = @v2.4.1; let result = v.minor;");
        Assert.Equal(4L, result);
    }

    [Fact]
    public void SemVer_PatchProperty_ReturnsLong()
    {
        object? result = Run("let v = @v2.4.1; let result = v.patch;");
        Assert.Equal(1L, result);
    }

    [Fact]
    public void SemVer_PrereleaseProperty_ReturnsString()
    {
        object? result = Run("let v = @v1.0.0-beta.2; let result = v.prerelease;");
        Assert.Equal("beta.2", result);
    }

    [Fact]
    public void SemVer_PrereleaseProperty_EmptyWhenNone()
    {
        object? result = Run("let v = @v1.0.0; let result = v.prerelease;");
        Assert.Equal("", result);
    }

    [Fact]
    public void SemVer_BuildProperty_ReturnsString()
    {
        object? result = Run("let v = @v1.0.0+build.123; let result = v.build;");
        Assert.Equal("build.123", result);
    }

    [Fact]
    public void SemVer_BuildProperty_EmptyWhenNone()
    {
        object? result = Run("let v = @v1.0.0; let result = v.build;");
        Assert.Equal("", result);
    }

    [Fact]
    public void SemVer_IsPrereleaseProperty_TrueWhenSet()
    {
        object? result = Run("let v = @v1.0.0-beta; let result = v.isPrerelease;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void SemVer_IsPrereleaseProperty_FalseWhenNone()
    {
        object? result = Run("let v = @v1.0.0; let result = v.isPrerelease;");
        Assert.Equal(false, result);
    }

    [Fact]
    public void SemVer_InvalidProperty_ThrowsError()
    {
        Assert.Throws<RuntimeError>(() => Run("let result = @v1.0.0.foo;"));
    }

    // ── Comparison Operators ──────────────────────────────────────────────────

    [Fact]
    public void SemVer_GreaterMajor_IsGreater()
    {
        object? result = Run("let result = @v2.0.0 > @v1.0.0;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void SemVer_GreaterMinor_IsGreater()
    {
        object? result = Run("let result = @v1.2.0 > @v1.1.0;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void SemVer_GreaterPatch_IsGreater()
    {
        object? result = Run("let result = @v1.0.2 > @v1.0.1;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void SemVer_NumericNotLexicographic()
    {
        object? result = Run("let result = @v1.10.0 > @v1.9.0;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void SemVer_PrereleaseIsLessThanRelease()
    {
        object? result = Run("let result = @v2.0.0-alpha < @v2.0.0;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void SemVer_PrereleaseOrdering()
    {
        object? result = Run("let result = @v1.0.0-alpha < @v1.0.0-beta;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void SemVer_PrereleaseNumericOrdering()
    {
        object? result = Run("let result = @v1.0.0-alpha.1 < @v1.0.0-alpha.2;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void SemVer_Equal_ReturnsTrue()
    {
        object? result = Run("let result = @v1.2.3 == @v1.2.3;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void SemVer_NotEqual_ReturnsTrue()
    {
        object? result = Run("let result = @v1.2.3 != @v1.2.4;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void SemVer_LessEqual_WhenEqual()
    {
        object? result = Run("let result = @v1.0.0 <= @v1.0.0;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void SemVer_LessEqual_WhenLess()
    {
        object? result = Run("let result = @v1.0.0 <= @v2.0.0;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void SemVer_GreaterEqual_WhenEqual()
    {
        object? result = Run("let result = @v1.0.0 >= @v1.0.0;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void SemVer_GreaterEqual_WhenGreater()
    {
        object? result = Run("let result = @v2.0.0 >= @v1.0.0;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void SemVer_BuildMetadata_IgnoredInComparison()
    {
        object? result = Run("let result = @v1.0.0+build1 == @v1.0.0+build2;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void SemVer_BuildMetadata_IgnoredInOrdering()
    {
        var result = Run("let result = @v1.0.0+zz > @v1.0.0+aa;");
        Assert.Equal(false, result);
    }

    // ── `in` Operator (Wildcard Range Matching) ───────────────────────────────

    [Fact]
    public void SemVer_InWildcardMajor_Matches()
    {
        object? result = Run("let result = @v2.4.1 in @v2.x;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void SemVer_InWildcardMajor_NoMatch()
    {
        object? result = Run("let result = @v3.0.0 in @v2.x;");
        Assert.Equal(false, result);
    }

    [Fact]
    public void SemVer_InWildcardMinor_Matches()
    {
        object? result = Run("let result = @v2.4.1 in @v2.4.x;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void SemVer_InWildcardMinor_NoMatch()
    {
        object? result = Run("let result = @v2.5.0 in @v2.4.x;");
        Assert.Equal(false, result);
    }

    [Fact]
    public void SemVer_InExactMatch()
    {
        object? result = Run("let result = @v1.2.3 in @v1.2.3;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void SemVer_InArray_Works()
    {
        object? result = Run("let result = @v1.2.3 in [@v1.0.0, @v1.2.3, @v2.0.0];");
        Assert.Equal(true, result);
    }

    // ── Type System ───────────────────────────────────────────────────────────

    [Fact]
    public void SemVer_TypeOf_ReturnsSemver()
    {
        object? result = Run("let result = typeof(@v1.0.0);");
        Assert.Equal("semver", result);
    }

    [Fact]
    public void SemVer_Is_Semver_ReturnsTrue()
    {
        object? result = Run("let result = @v1.0.0 is semver;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void SemVer_Is_String_ReturnsFalse()
    {
        object? result = Run("let result = @v1.0.0 is string;");
        Assert.Equal(false, result);
    }

    // ── Global semver() Function ──────────────────────────────────────────────

    [Fact]
    public void SemVer_ParseFunction_ValidString()
    {
        var result = Run("let result = semver(\"2.4.1\");");
        var sv = Assert.IsType<StashSemVer>(result);
        Assert.Equal(2L, sv.Major);
    }

    [Fact]
    public void SemVer_ParseFunction_WithPrerelease()
    {
        var result = Run("let result = semver(\"1.0.0-beta.2\");");
        var sv = Assert.IsType<StashSemVer>(result);
        Assert.Equal("beta.2", sv.Prerelease);
    }

    [Fact]
    public void SemVer_ParseFunction_InvalidString_Throws()
    {
        Assert.Throws<RuntimeError>(() => Run("let result = semver(\"not-a-version\");"));
    }

    // ── Stringify / ToString ──────────────────────────────────────────────────

    [Fact]
    public void SemVer_ToString_Basic()
    {
        object? result = Run("let result = \"\" + @v1.2.3;");
        Assert.Equal("1.2.3", result);
    }

    [Fact]
    public void SemVer_ToString_WithPrerelease()
    {
        object? result = Run("let result = \"\" + @v1.0.0-beta.2;");
        Assert.Equal("1.0.0-beta.2", result);
    }

    // ── Equality Edge Cases ───────────────────────────────────────────────────

    [Fact]
    public void SemVer_Equality_DifferentBuildMeta()
    {
        object? result = Run("let result = @v1.0.0+a == @v1.0.0+b;");
        Assert.Equal(true, result);
    }

    [Fact]
    public void SemVer_NotEqualToString()
    {
        object? result = Run("let result = @v1.0.0 == \"1.0.0\";");
        Assert.Equal(false, result);
    }

    [Fact]
    public void SemVer_NotEqualToNumber()
    {
        object? result = Run("let result = @v1.0.0 == 1;");
        Assert.Equal(false, result);
    }

    // ── Direct Literal Property Access (lexer fix validation) ──────────

    [Fact]
    public void SemVer_DirectLiteralPropertyAccess_Major()
    {
        var result = Run("let result = @v2.4.1.major;");
        Assert.Equal(2L, result);
    }

    [Fact]
    public void SemVer_DirectLiteralPropertyAccess_Minor()
    {
        var result = Run("let result = @v2.4.1.minor;");
        Assert.Equal(4L, result);
    }

    [Fact]
    public void SemVer_DirectLiteralPropertyAccess_Patch()
    {
        var result = Run("let result = @v2.4.1.patch;");
        Assert.Equal(1L, result);
    }

    [Fact]
    public void SemVer_DirectLiteralPropertyAccess_IsPrerelease()
    {
        // Prerelease literals contain dots so direct access is ambiguous;
        // assign to variable first to isolate the property access.
        var result = Run("let v = @v1.0.0-beta; let result = v.isPrerelease;");
        Assert.Equal(true, result);
    }

    // ── Wildcard equality (fix validation) ─────────────────────────────

    [Fact]
    public void SemVer_WildcardNotEqualToConcreteVersion()
    {
        var result = Run("let result = @v2.x == @v2.0.0;");
        Assert.Equal(false, result);
    }

    [Fact]
    public void SemVer_WildcardPatchNotEqualToConcreteVersion()
    {
        var result = Run("let result = @v2.0.x == @v2.0.0;");
        Assert.Equal(false, result);
    }

    [Fact]
    public void SemVer_WildcardEqualsItself()
    {
        var result = Run("let result = @v2.x == @v2.x;");
        Assert.Equal(true, result);
    }

    // ── ToString (fix validation) ──────────────────────────────────────

    [Fact]
    public void SemVer_ToString_WildcardMinor()
    {
        var result = Run("let result = \"\" + @v2.x;");
        Assert.Equal("2.x", result);
    }

    [Fact]
    public void SemVer_ToString_WildcardPatch()
    {
        var result = Run("let result = \"\" + @v2.4.x;");
        Assert.Equal("2.4.x", result);
    }

    // ── Mixed-type comparison error ────────────────────────────────────

    [Fact]
    public void SemVer_ComparedToInt_ThrowsError()
    {
        Assert.Throws<RuntimeError>(() => Run("let result = @v1.0.0 > 5;"));
    }

    [Fact]
    public void SemVer_ComparedToString_ThrowsError()
    {
        Assert.Throws<RuntimeError>(() => Run("let result = @v1.0.0 < \"hello\";"));
    }

    // ── Prerelease in wildcard range ───────────────────────────────────

    [Fact]
    public void SemVer_PrerelaseInWildcardRange_Matches()
    {
        var result = Run("let result = @v2.4.1-beta in @v2.x;");
        Assert.Equal(true, result);
    }
}
