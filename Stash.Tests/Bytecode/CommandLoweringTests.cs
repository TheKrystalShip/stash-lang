using System.Collections.Generic;
using System.IO;
using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Runtime.Errors;
using Stash.Runtime.Types;
using Stash.Stdlib;
using Xunit;

namespace Stash.Tests.Bytecode;

/// <summary>
/// End-to-end integration tests for Phase B of "Safe Shell Interpolation".
/// Verifies that <c>$(…)</c>, <c>$!(…)</c>, <c>$&lt;(…)</c>, pipe chains, and
/// redirect expressions are correctly lowered to <c>process.exec</c> /
/// <c>process.pipeline</c> calls.
///
/// All POSIX-only tests are guarded with <c>if (OperatingSystem.IsWindows()) return;</c>.
/// </summary>
public class CommandLoweringTests : BytecodeTestBase
{
    // Helper: compile + run with full stdlib globals (process, arr, str, …).
    private static object? Exec(string source)
    {
        var lexer = new Lexer(source, "<test>");
        List<Token> tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        List<Stmt> stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        Chunk chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        return Normalize(vm.Execute(chunk));
    }

    // =========================================================================
    // 1. Basic capture
    // =========================================================================

    [Fact]
    public void Basic_EchoHi_CapturesStdout()
    {
        if (OperatingSystem.IsWindows()) return;

        var result = (StashInstance?)Exec("return $(echo hi);");
        Assert.NotNull(result);
        Assert.True(result!.VMTryGetField("stdout", out StashValue sv, null));
        Assert.Equal("hi\n", sv.AsObj as string);
    }

    // =========================================================================
    // 2. Injection safety — user-controlled value is never parsed as new tokens
    // =========================================================================

    [Fact]
    public void InjectionSafe_SemicolonInInterp_PassedVerbatim()
    {
        if (OperatingSystem.IsWindows()) return;

        // If the interp were passed to a shell, "; rm -rf ~" would be interpreted.
        // With process.exec lowering, it must appear as a single literal argument.
        var result = (StashInstance?)Exec("""
            let p = "; rm -rf ~";
            return $(echo ${p});
            """);
        Assert.NotNull(result);
        result!.VMTryGetField("stdout", out StashValue sv, null);
        Assert.Equal("; rm -rf ~\n", sv.AsObj as string);
    }

    // =========================================================================
    // 3. No glob on interpolated variable (no-expand)
    // =========================================================================

    [Fact]
    public void NoGlob_InterpVariable_PassedLiteral()
    {
        if (OperatingSystem.IsWindows()) return;

        // "*.cs" passed via interpolation must NOT be glob-expanded.
        var result = (StashInstance?)Exec("""
            let p = "*.cs";
            return $(echo ${p});
            """);
        Assert.NotNull(result);
        result!.VMTryGetField("stdout", out StashValue sv, null);
        Assert.Equal("*.cs\n", sv.AsObj as string);
    }

    // =========================================================================
    // 4. No tilde-expansion on interpolated variable
    // =========================================================================

    [Fact]
    public void NoTilde_InterpVariable_PassedLiteral()
    {
        if (OperatingSystem.IsWindows()) return;

        var result = (StashInstance?)Exec("""
            let p = "~/foo";
            return $(echo ${p});
            """);
        Assert.NotNull(result);
        result!.VMTryGetField("stdout", out StashValue sv, null);
        Assert.Equal("~/foo\n", sv.AsObj as string);
    }

    // =========================================================================
    // 5. Glob expansion on literal tokens (unquoted)
    // =========================================================================

    [Fact]
    public void Glob_LiteralPattern_ExpandsMatches()
    {
        if (OperatingSystem.IsWindows()) return;

        // *.csproj in the workspace root should expand to real files.
        var result = (StashInstance?)Exec("return $(echo *.csproj);");
        Assert.NotNull(result);
        result!.VMTryGetField("stdout", out StashValue sv, null);
        Assert.Contains(".csproj", sv.AsObj as string);
    }

    // =========================================================================
    // 6. Tilde expansion on literal token
    // =========================================================================

    [Fact]
    public void Tilde_LiteralToken_ExpandsToHome()
    {
        if (OperatingSystem.IsWindows()) return;

        var result = (StashInstance?)Exec("return $(echo ~);");
        Assert.NotNull(result);
        result!.VMTryGetField("stdout", out StashValue sv, null);
        string home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        Assert.StartsWith(home, ((string)sv.AsObj!).Trim());
    }

    // =========================================================================
    // 7. Quoted string → single argv entry (space preserved)
    // =========================================================================

    [Fact]
    public void Quoted_DoubleQuote_SingleArgument()
    {
        if (OperatingSystem.IsWindows()) return;

        var result = (StashInstance?)Exec("""return $(echo "hello world");""");
        Assert.NotNull(result);
        result!.VMTryGetField("stdout", out StashValue sv, null);
        Assert.Equal("hello world\n", sv.AsObj as string);
    }

    // =========================================================================
    // 8. Array interpolation → splat
    // =========================================================================

    [Fact]
    public void ArrayInterp_Splats_IntoArgv()
    {
        if (OperatingSystem.IsWindows()) return;

        var result = (StashInstance?)Exec("""
            let items = ["a", "b", "c"];
            return $(echo ${items});
            """);;
        Assert.NotNull(result);
        result!.VMTryGetField("stdout", out StashValue sv, null);
        Assert.Equal("a b c\n", sv.AsObj as string);
    }

    // =========================================================================
    // 9. No-match glob literal → passed verbatim (no error)
    // =========================================================================

    [Fact]
    public void Glob_NoMatch_FallsBackToLiteral()
    {
        if (OperatingSystem.IsWindows()) return;

        // When a glob pattern matches nothing, the literal token is passed verbatim
        // (bash nullglob-off behaviour). Phase B: no RuntimeError thrown.
        var result = (StashInstance?)Exec("return $(echo *.xyz);");
        Assert.NotNull(result);
        result!.VMTryGetField("stdout", out StashValue sv, null);
        Assert.Equal("*.xyz\n", sv.AsObj as string);
    }

    // =========================================================================
    // 10. Glue — literal + interpolation without whitespace between them
    // =========================================================================

    [Fact]
    public void Glue_LiteralAndInterp_SingleArgument()
    {
        if (OperatingSystem.IsWindows()) return;

        var result = (StashInstance?)Exec("""
            let user = "alice";
            return $(echo --name=${user});
            """);
        Assert.NotNull(result);
        result!.VMTryGetField("stdout", out StashValue sv, null);
        Assert.Equal("--name=alice\n", sv.AsObj as string);
    }

    // =========================================================================
    // 11. Strict mode ($!) → throws CommandError on non-zero exit
    // =========================================================================

    [Fact]
    public void Strict_NonZeroExit_ThrowsCommandError()
    {
        if (OperatingSystem.IsWindows()) return;

        Assert.ThrowsAny<CommandError>(() => Exec("return $!(false);"));
    }

    // =========================================================================
    // 12. Streaming mode ($<) → returns StreamingProcess
    // =========================================================================

    [Fact]
    public void Streaming_ReturnsStreamingProcess_CanIterate()
    {
        if (OperatingSystem.IsWindows()) return;

        var result = Exec("""
            let s = $<(echo hi);
            let lines = [];
            for (let line in s) {
                arr.push(lines, line);
            }
            return lines;
            """);
        var list = Assert.IsType<List<object?>>(result);
        Assert.Contains("hi", list);
    }

    // =========================================================================
    // 13. Pipeline → process.pipeline
    // =========================================================================

    [Fact]
    public void Pipeline_EchoTrH_CapturesTransformedOutput()
    {
        if (OperatingSystem.IsWindows()) return;

        var result = (StashInstance?)Exec("return $(echo hi | tr h H);");
        Assert.NotNull(result);
        result!.VMTryGetField("stdout", out StashValue sv, null);
        Assert.Equal("Hi\n", sv.AsObj as string);
    }

    // =========================================================================
    // 14. Redirect → output written to file
    // =========================================================================

    [Fact]
    public void Redirect_StdoutToFile_WritesContent()
    {
        if (OperatingSystem.IsWindows()) return;

        string tmpFile = Path.Combine(Path.GetTempPath(), $"stash_lower_test_{System.Guid.NewGuid():N}.txt");
        try
        {
            Exec($"$(echo hi) > \"{tmpFile}\";");
            string contents = File.ReadAllText(tmpFile);
            Assert.Equal("hi\n", contents);
        }
        finally
        {
            if (File.Exists(tmpFile)) File.Delete(tmpFile);
        }
    }

    // =========================================================================
    // 15. Array program slot — array interp as program: first element is program,
    //     rest prepended to args
    // =========================================================================

    [Fact]
    public void ArrayProgram_ArrayInterp_SplatsAndFirstIsProgram()
    {
        if (OperatingSystem.IsWindows()) return;

        // When an interpolation slot resolves to an array at runtime, process.exec
        // treats the first element as the program and the rest as leading argv entries.
        var result = (StashInstance?)Exec("""
            let cmd = ["echo", "-n", "yo"];
            return $(${cmd});
            """);
        Assert.NotNull(result);
        result!.VMTryGetField("stdout", out StashValue sv, null);
        Assert.Equal("yo", sv.AsObj as string);
    }

    // =========================================================================
    // 16. Explicit spread ${...arr} — single element
    // =========================================================================

    [Fact]
    public void ExplicitSpread_SingleElement_PassedAsArg()
    {
        if (OperatingSystem.IsWindows()) return;

        var result = (StashInstance?)Exec("""
            let arr = ["-la"];
            return $(printf "%s\n" ${...arr});
            """);
        Assert.NotNull(result);
        result!.VMTryGetField("stdout", out StashValue sv, null);
        Assert.Equal("-la\n", sv.AsObj as string);
    }

    // =========================================================================
    // 17. Explicit spread ${...arr} — multiple elements
    // =========================================================================

    [Fact]
    public void ExplicitSpread_MultipleElements_EachPassedSeparately()
    {
        if (OperatingSystem.IsWindows()) return;

        var result = (StashInstance?)Exec("""
            let arr = ["a", "b", "c"];
            return $(printf "%s_" ${...arr});
            """);
        Assert.NotNull(result);
        result!.VMTryGetField("stdout", out StashValue sv, null);
        Assert.Equal("a_b_c_", sv.AsObj as string);
    }

    // =========================================================================
    // 18. Explicit spread ${...arr} matches implicit ${arr} semantics
    // =========================================================================

    [Fact]
    public void ExplicitSpread_MatchesImplicitSpread_SameOutput()
    {
        if (OperatingSystem.IsWindows()) return;

        var resultImplicit = (StashInstance?)Exec("""
            let items = ["x", "y"];
            return $(printf "%s " ${items});
            """);
        var resultExplicit = (StashInstance?)Exec("""
            let items = ["x", "y"];
            return $(printf "%s " ${...items});
            """);

        resultImplicit!.VMTryGetField("stdout", out StashValue svI, null);
        resultExplicit!.VMTryGetField("stdout", out StashValue svE, null);
        Assert.Equal(svI.AsObj as string, svE.AsObj as string);
    }

    // =========================================================================
    // 19. Explicit spread in glued slot → compile-time error
    // =========================================================================

    [Fact]
    public void ExplicitSpread_GluedSlot_ThrowsCompileError()
    {
        if (OperatingSystem.IsWindows()) return;

        // "prefix${...arr}" — the spread is glued to preceding literal text.
        var ex = Assert.Throws<CompileError>(() => Exec("""
            let arr = ["x"];
            return $(prefix${...arr});
            """));
        Assert.Contains("glued slot", ex.Message);
    }
}
