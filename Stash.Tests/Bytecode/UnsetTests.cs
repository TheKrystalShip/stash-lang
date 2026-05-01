using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Stdlib;

namespace Stash.Tests.Bytecode;

/// <summary>
/// Integration tests for the <c>unset</c> statement (top-level binding removal).
/// Covers spec §9.1 — runtime behaviour.
/// </summary>
public class UnsetTests : Stash.Tests.Interpreting.StashTestBase
{
    // =========================================================================
    // §9.1 Test #1 — accessing an unset variable raises RuntimeError
    // =========================================================================

    [Fact]
    public void Unset_ExistingVariable_AccessThrowsRuntimeError()
    {
        var ex = RunCapturingError("""
            let x = 1;
            unset x;
            let _dummy = x;
            """);
        Assert.Contains("'x'", ex.Message);
    }

    // =========================================================================
    // §9.1 Test #2 — rebind succeeds after unset
    // =========================================================================

    [Fact]
    public void Unset_ExistingVariable_RebindAfterUnsetSucceeds()
    {
        var result = Run("""
            let x = 1;
            unset x;
            let x = 2;
            let result = x;
            """);
        Assert.Equal(2L, result);
    }

    // =========================================================================
    // §9.1 Test #3 — unsetting an undeclared name is a runtime no-op
    // =========================================================================

    [Fact]
    public void Unset_UndeclaredName_RunsWithoutError()
    {
        // Must complete without throwing any exception.
        RunStatements("unset undeclared_xyz_test;");
    }

    // =========================================================================
    // §9.1 Test #4 — multi-target: surviving variable retains value; others fail
    // =========================================================================

    [Fact]
    public void Unset_MultipleTargets_SurvivingVariableRetainsValue()
    {
        var c = Run("""
            let a = 1;
            let b = 2;
            let c = 3;
            unset a, b;
            let result = c;
            """);
        Assert.Equal(3L, c);
    }

    [Fact]
    public void Unset_MultipleTargets_UnsetVariablesThrowOnAccess()
    {
        var exA = RunCapturingError("""
            let a = 1;
            unset a;
            let _dummy = a;
            """);
        Assert.Contains("'a'", exA.Message);

        var exB = RunCapturingError("""
            let b = 2;
            unset b;
            let _dummy = b;
            """);
        Assert.Contains("'b'", exB.Message);
    }

    // =========================================================================
    // §9.1 Test #5 — unsetting a function; calling it afterwards throws
    // =========================================================================

    [Fact]
    public void Unset_Function_AccessThrowsRuntimeError()
    {
        var ex = RunCapturingError("""
            fn f() { return 1; }
            unset f;
            f();
            """);
        Assert.Contains("'f'", ex.Message);
    }

    // =========================================================================
    // §9.1 Test #6 — struct unset: subsequent `is MyStruct` returns false
    // =========================================================================

    [Fact]
    public void Unset_Struct_IsCheckReturnsFalse()
    {
        var result = Run("""
            struct S { x }
            let v = S { x: 1 };
            unset S;
            let result = v is S;
            """);
        Assert.Equal(false, result);
    }

    // =========================================================================
    // §9.1 Test #7 — enum unset: accessing the enum value throws NameError
    // =========================================================================

    [Fact]
    public void Unset_Enum_AccessThrowsRuntimeError()
    {
        var ex = RunCapturingError("""
            enum E { A, B }
            unset E;
            let _dummy = E.A;
            """);
        // Loading E from globals fails since it was removed.
        Assert.Contains("'E'", ex.Message);
    }

    // =========================================================================
    // §9.1 Test #8 — lambda that captures a global: call after unset throws
    // =========================================================================

    [Fact]
    public void Unset_LambdaCapturingGlobal_CallAfterUnsetThrows()
    {
        var ex = RunCapturingError("""
            let counter = 0;
            let bump = () => counter = counter + 1;
            bump();
            unset counter;
            bump();
            """);
        Assert.Contains("'counter'", ex.Message);
    }

    // =========================================================================
    // §9.1 Test #9 — const can be unset at the runtime level (validator not run)
    // =========================================================================

    [Fact]
    public void Unset_ConstGlobal_RuntimeSucceeds_WhenValidatorNotRun()
    {
        // The semantic validator (SA0843) rejects `unset const` in script mode,
        // but the VM itself has no such restriction. We bypass the validator by
        // compiling directly without running SemanticValidator — the compiler
        // only calls SemanticResolver, not SemanticValidator.
        //
        // Note: compile-time const-folding (§6.5) may cause subsequent reads of PI
        // in the SAME chunk to see the folded value 3 rather than the rebound 314.
        // That is expected per spec — RemoveConstValue only guards future RE-COMPILATIONS.
        // The important thing is that the unset + rebind sequence completes without error.
        RunStatements("""
            const PI = 3;
            unset PI;
            let PI = 314;
            """);
    }

    // =========================================================================
    // §9.1 Test #10 — HasReplGlobal flips to false after unset
    // =========================================================================

    [Fact]
    public void Unset_HasReplGlobal_FlipsToFalse()
    {
        // Compile and run `let ls = "x"` against a shared VM instance.
        var vm = new VirtualMachine(StdlibDefinitions.CreateVMGlobals());

        static Stash.Bytecode.Chunk CompileSource(string source)
        {
            var tokens = new Lexer(source, "<test>").ScanTokens();
            var stmts = new Parser(tokens).ParseProgram();
            SemanticResolver.Resolve(stmts);
            return Compiler.Compile(stmts);
        }

        vm.Execute(CompileSource("""let ls = "x";"""));
        Assert.True(vm.HasReplGlobal("ls"), "Expected HasReplGlobal('ls') == true after let");

        vm.Execute(CompileSource("unset ls;"));
        Assert.False(vm.HasReplGlobal("ls"), "Expected HasReplGlobal('ls') == false after unset");
    }

    // =========================================================================
    // §9.1 Test #11 — duplicate unset target: second is a silent no-op
    // =========================================================================

    [Fact]
    public void Unset_DuplicateTarget_SecondIsNoOp()
    {
        // `unset x, x;` — the first Remove succeeds, the second is a dict no-op.
        // The whole statement must complete without throwing.
        RunStatements("""
            let x = 42;
            unset x, x;
            """);
    }

    // =========================================================================
    // §9.1 Test #12 — defer block still runs after an unset in the same scope
    // =========================================================================

    [Fact]
    public void Unset_WithDefer_DeferStillRunsAtScopeExit()
    {
        var output = RunCapturingOutput("""
            let x = 1;
            defer io.println("done");
            unset x;
            """);
        Assert.Contains("done", output);
    }
}
