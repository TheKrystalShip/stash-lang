using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Stdlib;

namespace Stash.Tests.Bytecode;

/// <summary>
/// Verifies that <see cref="Stash.Runtime.Types.StashTypedArray"/> still works for
/// the stdlib-produced paths that survive the type-hint erasure refactor.
///
/// After the erasure refactor, <c>let arr: int[] = [...]</c> no longer produces a
/// typed array (it produces a plain <c>array</c>). The only remaining way to obtain
/// a typed array is via stdlib factories: <c>arr.typed(...)</c>, <c>arr.new(...)</c>,
/// or operations like <c>arr.slice</c>/<c>arr.filter</c> that preserve element typing
/// from a typed-array input. These tests guard that surviving surface.
/// </summary>
public class StashTypedArrayStdlibTests : BytecodeTestBase
{
    // Override Execute to inject stdlib globals (arr, json, typeof, conv, ...).
    protected new static object? Execute(string source)
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

    // ── arr.typed — explicit construction ──────────────────────────────────────

    [Fact]
    public void ArrTyped_FromIntList_CreatesIntArray()
    {
        Assert.Equal("int[]", Execute("let xs = arr.typed([1, 2, 3], \"int\"); return typeof(xs);"));
    }

    [Fact]
    public void ArrTyped_FromStringList_CreatesStringArray()
    {
        Assert.Equal("string[]", Execute("let xs = arr.typed([\"a\", \"b\"], \"string\"); return typeof(xs);"));
    }

    [Fact]
    public void ArrTyped_InvalidElements_Throws()
    {
        Assert.Throws<RuntimeError>(() => Execute("arr.typed([1, \"two\", 3], \"int\");"));
    }

    [Fact]
    public void ArrTyped_PushWrongType_Throws()
    {
        Assert.Throws<RuntimeError>(() =>
            Execute("let xs = arr.typed([1, 2, 3], \"int\"); arr.push(xs, \"hi\");"));
    }

    // ── arr.new — zero-initialized typed array ─────────────────────────────────

    [Fact]
    public void ArrNew_Int_CreatesZeroes()
    {
        Assert.Equal("int[]", Execute("let xs = arr.new(\"int\", 5); return typeof(xs);"));
    }

    [Fact]
    public void ArrNew_Byte_CreatesZeroes()
    {
        Assert.Equal("byte[]", Execute("let xs = arr.new(\"byte\", 4); return typeof(xs);"));
    }

    // ── arr.elementType — introspection ────────────────────────────────────────

    [Fact]
    public void ArrElementType_TypedArray_ReturnsName()
    {
        Assert.Equal("int", Execute("let xs = arr.typed([1, 2], \"int\"); return arr.elementType(xs);"));
    }

    [Fact]
    public void ArrElementType_GenericArray_ReturnsNull()
    {
        Assert.Null(Execute("let xs = [1, 2, 3]; return arr.elementType(xs);"));
    }

    // ── Element-type-preserving operations ─────────────────────────────────────

    [Fact]
    public void ArrSlice_OnTypedArray_PreservesType()
    {
        Assert.Equal("int[]", Execute(
            "let xs = arr.typed([1, 2, 3, 4], \"int\"); return typeof(arr.slice(xs, 0, 2));"));
    }

    [Fact]
    public void ArrFilter_OnTypedArray_PreservesType()
    {
        Assert.Equal("int[]", Execute(
            "let xs = arr.typed([1, 2, 3], \"int\"); return typeof(arr.filter(xs, (x) => x > 1));"));
    }

    [Fact]
    public void ArrUnique_OnTypedArray_PreservesType()
    {
        Assert.Equal("int[]", Execute(
            "let xs = arr.typed([1, 2, 2, 3], \"int\"); return typeof(arr.unique(xs));"));
    }

    // ── is / typeof on stdlib-produced typed arrays ────────────────────────────

    [Fact]
    public void TypedArray_IsOwnType_ReturnsTrue()
    {
        Assert.Equal(true, Execute(
            "let xs = arr.typed([1, 2, 3], \"int\"); return xs is int[];"));
    }

    [Fact]
    public void TypedArray_IsArray_ReturnsTrue()
    {
        Assert.Equal(true, Execute(
            "let xs = arr.typed([1, 2, 3], \"int\"); return xs is array;"));
    }

    [Fact]
    public void TypedArray_IsWrongElementType_ReturnsFalse()
    {
        Assert.Equal(false, Execute(
            "let xs = arr.typed([1, 2, 3], \"int\"); return xs is string[];"));
    }

    // ── arr.untyped — round-trip back to generic ───────────────────────────────

    [Fact]
    public void ArrUntyped_ConvertsToGeneric()
    {
        Assert.Equal("array", Execute(
            "let xs = arr.typed([1, 2, 3], \"int\"); return typeof(arr.untyped(xs));"));
    }
}
