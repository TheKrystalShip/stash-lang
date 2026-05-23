namespace Stash.Tests.Stdlib;

using System.Collections.Generic;
using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Runtime.Errors;
using Stash.Runtime.Types;
using Stash.Stdlib;
using Stash.Tests.Interpreting;
using Xunit;

/// <summary>
/// Tests for the P5 v1 migration of cli.argc and cli.argv as [StashMember] data members.
///
/// Enumeration of audited helpers (frozen-write regression):
///   arr.push, arr.pop, arr.insert, arr.removeAt, arr.remove, arr.clear,
///   arr.reverse, arr.sort
///   dict.set, dict.remove, dict.clear
/// </summary>
public class CliMembersTests : StashTestBase
{
    // =========================================================================
    // cli.argc — typeof returns "int" (not "function")
    // =========================================================================

    [Fact]
    public void Argc_TypeofReturnInt_NotFunction()
    {
        var result = RunWithArgs("""
            let result = typeof(cli.argc);
        """, ["x", "y"]);
        Assert.Equal("int", result);
    }

    // =========================================================================
    // cli.argv — typeof returns "array" (not "function")
    // =========================================================================

    [Fact]
    public void Argv_TypeofReturnArray_NotFunction()
    {
        var result = RunWithArgs("""
            let result = typeof(cli.argv);
        """, ["x"]);
        Assert.Equal("array", result);
    }

    // =========================================================================
    // Cached: identity-stable across accesses
    // =========================================================================

    [Fact]
    public void Argv_Cached_TwoReadsReturnSameLength()
    {
        // Cached stability: both reads should return the same logical content.
        var result = RunWithArgs("""
            let a = cli.argv;
            let b = cli.argv;
            let result = len(a) == len(b);
        """, ["x", "y"]);
        Assert.Equal(true, result);
    }

    [Fact]
    public void Argc_Cached_TwoReadsReturnSameValue()
    {
        var result = RunWithArgs("""
            let a = cli.argc;
            let b = cli.argc;
            let result = a == b;
        """, ["x", "y"]);
        Assert.Equal(true, result);
    }

    // =========================================================================
    // Frozen-write: cli.argv[0] = "x" raises error (index-set on frozen array)
    // =========================================================================

    [Fact]
    public void Argv_IndexAssign_RaisesFrozenWriteError()
    {
        var error = RunCapturingError("""
            cli.argv[0] = "x";
        """);
        Assert.Contains("Cannot mutate", error.Message);
    }

    // =========================================================================
    // Frozen-helper audit: arr.* mutators reject cli.argv
    //
    // Audited arr.* mutating helpers:
    //   arr.push, arr.pop, arr.insert, arr.removeAt, arr.remove,
    //   arr.clear, arr.reverse, arr.sort
    //
    // Audited dict.* mutating helpers:
    //   dict.set, dict.remove, dict.clear
    //   (these go through StashDictionary.Set/Remove/Clear which already guard _frozen)
    // =========================================================================

    [Fact]
    public void FrozenHelper_ArrPush_RaisesFrozenWriteError()
    {
        var error = RunWithArgsCapturingError("""
            arr.push(cli.argv, "extra");
        """, ["a"]);
        Assert.Contains("Cannot mutate", error.Message);
    }

    [Fact]
    public void FrozenHelper_ArrPop_RaisesFrozenWriteError()
    {
        var error = RunWithArgsCapturingError("""
            arr.pop(cli.argv);
        """, ["a"]);
        Assert.Contains("Cannot mutate", error.Message);
    }

    [Fact]
    public void FrozenHelper_ArrInsert_RaisesFrozenWriteError()
    {
        var error = RunWithArgsCapturingError("""
            arr.insert(cli.argv, 0, "x");
        """, ["a"]);
        Assert.Contains("Cannot mutate", error.Message);
    }

    [Fact]
    public void FrozenHelper_ArrRemoveAt_RaisesFrozenWriteError()
    {
        var error = RunWithArgsCapturingError("""
            arr.removeAt(cli.argv, 0);
        """, ["a"]);
        Assert.Contains("Cannot mutate", error.Message);
    }

    [Fact]
    public void FrozenHelper_ArrRemove_RaisesFrozenWriteError()
    {
        var error = RunWithArgsCapturingError("""
            arr.remove(cli.argv, "a");
        """, ["a"]);
        Assert.Contains("Cannot mutate", error.Message);
    }

    [Fact]
    public void FrozenHelper_ArrClear_RaisesFrozenWriteError()
    {
        var error = RunWithArgsCapturingError("""
            arr.clear(cli.argv);
        """, ["a"]);
        Assert.Contains("Cannot mutate", error.Message);
    }

    [Fact]
    public void FrozenHelper_ArrReverse_RaisesFrozenWriteError()
    {
        var error = RunWithArgsCapturingError("""
            arr.reverse(cli.argv);
        """, ["a", "b"]);
        Assert.Contains("Cannot mutate", error.Message);
    }

    [Fact]
    public void FrozenHelper_ArrSort_RaisesFrozenWriteError()
    {
        var error = RunWithArgsCapturingError("""
            arr.sort(cli.argv);
        """, ["b", "a"]);
        Assert.Contains("Cannot mutate", error.Message);
    }

    // =========================================================================
    // Non-mutating arr.* helpers accept frozen cli.argv (F01 regression)
    //
    // Each test calls a non-mutating arr.* helper on cli.argv and asserts the
    // expected return value — not TypeError. These cover three dispatch paths:
    //   arr.slice  — Raw handler, uses SvArgs.StashList
    //   arr.map    — typed handler, uses SvArgs.StashList via generated marshal
    //   arr.join   — typed handler, non-list-returning path
    // =========================================================================

    [Fact]
    public void FrozenRead_ArrSlice_ReturnsPortion()
    {
        var result = RunWithArgs("""
            let result = arr.slice(cli.argv, 0, 2);
        """, ["a", "b", "c"]);
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(new List<object?> { "a", "b" }, list);
    }

    [Fact]
    public void FrozenRead_ArrMap_TransformsElements()
    {
        var result = RunWithArgs("""
            fn upper(x) { return str.upper(x); }
            let result = arr.map(cli.argv, upper);
        """, ["a", "b", "c"]);
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(new List<object?> { "A", "B", "C" }, list);
    }

    [Fact]
    public void FrozenRead_ArrJoin_ConcatenatesElements()
    {
        var result = RunWithArgs("""
            let result = arr.join(cli.argv, ",");
        """, ["a", "b", "c"]);
        Assert.Equal("a,b,c", result);
    }

    // =========================================================================
    // SA0846: old call form cli.argc() / cli.argv() is a compile-time error
    // =========================================================================

    [Fact]
    public void Argc_CallForm_RaisesCompileTimeSA0846()
    {
        var diagnostics = GetCompileDiagnostics("cli.argc();");
        Assert.Contains(diagnostics, d => d.Code == "SA0846");
    }

    [Fact]
    public void Argv_CallForm_RaisesCompileTimeSA0846()
    {
        var diagnostics = GetCompileDiagnostics("cli.argv();");
        Assert.Contains(diagnostics, d => d.Code == "SA0846");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static RuntimeError RunWithArgsCapturingError(string source, string[] args)
    {
        // Build+compile the source, supply args, assert RuntimeError.
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());
        vm.ScriptArgs = args;
        return Assert.ThrowsAny<RuntimeError>(() => vm.Execute(chunk));
    }

    private static List<Stash.Analysis.SemanticDiagnostic> GetCompileDiagnostics(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        var collector = new Stash.Analysis.SymbolCollector();
        var scopeTree = collector.Collect(stmts);
        var validator = new Stash.Analysis.SemanticValidator(scopeTree);
        return validator.Validate(stmts);
    }
}
