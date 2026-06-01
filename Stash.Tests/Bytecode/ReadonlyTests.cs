using Stash.Runtime.Errors;
using Stash.Runtime.Types;
using Stash.Runtime;

namespace Stash.Tests.Bytecode;

/// <summary>
/// End-to-end tests for the <c>readonly</c> modifier (P4): compiler wires DeepFreeze into
/// declaration init and rebind paths so that readonly-bound values are transitively immutable.
///
/// Every test runs through the full parse → compile → VM pipeline via <see cref="BytecodeTestBase"/>.
/// </summary>
public class ReadonlyTests : BytecodeTestBase
{
    // =========================================================================
    // 1. Basic const + readonly: direct mutation throws ReadOnlyError
    // =========================================================================

    [Fact]
    public void ReadonlyConst_DirectFieldMutation_ThrowsReadOnlyError()
    {
        Assert.Throws<ReadOnlyError>(() =>
            Execute("readonly const D = { x: 1 }; D.x = 2;"));
    }

    [Fact]
    public void ReadonlyConst_NestedArrayMutation_ThrowsReadOnlyError()
    {
        // deep freeze: nested array is frozen too — push requires stdlib
        Assert.Throws<ReadOnlyError>(() =>
            ExecuteWithStdlib("readonly const D = { ports: [80] }; D.ports.push(22);"));
    }

    // =========================================================================
    // 2. Aliasing — value-side flag catches it
    // =========================================================================

    [Fact]
    public void ReadonlyConst_AliasThroughLet_ThrowsReadOnlyError()
    {
        Assert.Throws<ReadOnlyError>(() =>
            Execute("readonly const D = { a: 1 }; let a = D; a.a = 2;"));
    }

    // =========================================================================
    // 3. readonly let: rebind re-freezes
    // =========================================================================

    [Fact]
    public void ReadonlyLet_Rebind_RefreezesNewValue()
    {
        // After rebind, the new value is frozen — mutation should throw.
        Assert.Throws<ReadOnlyError>(() =>
            Execute("readonly let S = { x: 0 }; S = { x: 1 }; S.x = 2;"));
    }

    [Fact]
    public void ReadonlyLet_RebindAllowed_NewValueIsFrozen()
    {
        // Rebind itself (the assignment S = {...}) must succeed; the frozen guard is only
        // triggered on mutation of the resulting value.
        object? result = Execute(
            "readonly let S = { x: 1 }; S = { x: 99 }; return S.x;");
        Assert.Equal(99L, result);
    }

    // =========================================================================
    // 4. Primitives — readonly is a no-op for binding axis
    // =========================================================================

    [Fact]
    public void ReadonlyLet_Primitive_NoOpFreeze_BindingRemainsRebindable()
    {
        // readonly let n = 42;  n = 99;  should NOT throw for primitives
        object? result = Execute("readonly let n = 42; n = 99; return n;");
        Assert.Equal(99L, result);
    }

    // =========================================================================
    // 5. const binding axis preserved — rebind throws even without readonly
    // =========================================================================

    [Fact]
    public void ReadonlyConst_RebindThrows()
    {
        // The const axis still rejects rebind; readonly doesn't change that.
        Assert.ThrowsAny<RuntimeError>(() =>
            Execute("readonly const C = { x: 1 }; C = { x: 2 };"));
    }

    // =========================================================================
    // 6. Q4: closure upvalue rebind re-freezes (readonly let)
    // =========================================================================

    [Fact]
    public void ReadonlyLet_ClosureRebind_RefreezesAndThrowsOnMutation()
    {
        // readonly let S; closure rebinds S; then S.x = 1 should throw.
        string source = @"
fn makeMutable() { return { x: 0 }; }
readonly let S = { x: 0 };
fn g() { S = makeMutable(); }
g();
S.x = 1;
";
        Assert.Throws<ReadOnlyError>(() => ExecuteWithStdlib(source));
    }

    [Fact]
    public void ReadonlyLet_ClosureRebind_RebindSucceeds_NewValueFrozen()
    {
        // Rebind via closure should succeed; result is accessible.
        string source = @"
fn makeVal(v) { return { x: v }; }
readonly let S = { x: 0 };
fn g() { S = makeVal(42); }
g();
return S.x;
";
        object? result = ExecuteWithStdlib(source);
        Assert.Equal(42L, result);
    }

    // =========================================================================
    // 7. Regression: const-through-closure now throws (backlog bug fix)
    // =========================================================================

    [Fact]
    public void Const_ClosureRebind_NowThrows()
    {
        // Before P4: silently succeeded. After P4: throws.
        string source = @"
const c = 1;
fn g() { c = 2; }
g();
";
        Assert.ThrowsAny<RuntimeError>(() => ExecuteWithStdlib(source));
    }

    // =========================================================================
    // 8. Identity: freeze is in-place — value identity preserved
    // =========================================================================

    [Fact]
    public void ReadonlyLet_IdentityPreserved_SameTypeAfterFreeze()
    {
        // After freeze, the value is still a dict (not wrapped/converted).
        // typeof() needs stdlib, so use ExecuteWithStdlib.
        object? result = ExecuteWithStdlib(@"
let D = { x: 1 };
readonly let a = D;
return typeof(a);
");
        Assert.Equal("dict", result);
    }

    [Fact]
    public void ReadonlyLet_IdentityPreserved_FrozenValueIsAccessible()
    {
        // After freeze, the value remains readable.
        object? result = Execute(@"
readonly let a = { x: 42 };
return a.x;
");
        Assert.Equal(42L, result);
    }

    // =========================================================================
    // 9. Deep freeze: nested dict/array/struct all frozen
    // =========================================================================

    [Fact]
    public void ReadonlyConst_NestedDict_Frozen()
    {
        Assert.Throws<ReadOnlyError>(() =>
            Execute("readonly const D = { inner: { y: 2 } }; D.inner.y = 3;"));
    }

    [Fact]
    public void ReadonlyConst_TopLevelArray_ElementMutationThrows()
    {
        Assert.Throws<ReadOnlyError>(() =>
            Execute("readonly const A = [1, 2, 3]; A[0] = 99;"));
    }

    [Fact]
    public void ReadonlyConst_TopLevelArray_PushThrows()
    {
        // arr.push is a stdlib mutator — needs stdlib globals registered.
        Assert.Throws<ReadOnlyError>(() =>
            ExecuteWithStdlib("readonly const A = [1, 2]; A.push(3);"));
    }

    [Fact]
    public void ReadonlyConst_NestedArray_PushThrows_E2E()
    {
        // done_when scenario: readonly const D = { ports: [80] }; D.ports.push(22);
        Assert.Throws<ReadOnlyError>(() =>
            ExecuteWithStdlib("readonly const D = { ports: [80] }; D.ports.push(22);"));
    }

    // =========================================================================
    // 10. ReadonlyError is catchable
    // =========================================================================

    [Fact]
    public void ReadonlyConst_MutationThrowsReadOnlyError_IsCatchable()
    {
        object? result = Execute(@"
readonly const D = { x: 1 };
let caught = false;
try {
    D.x = 2;
} catch (ReadOnlyError e) {
    caught = true;
}
return caught;
");
        Assert.Equal(true, result);
    }

    // =========================================================================
    // 11. Aliasing footgun: pre-existing shared object becomes frozen
    // =========================================================================

    [Fact]
    public void ReadonlyConst_FreezeReachesPreExistingAlias_Throws()
    {
        // let shared = { count: 0 };
        // readonly const snap = { data: shared };
        // shared.count = 1;   ← throws — shared was frozen as collateral
        Assert.Throws<ReadOnlyError>(() =>
            Execute("let shared = { count: 0 }; readonly const snap = { data: shared }; shared.count = 1;"));
    }

    // =========================================================================
    // 12. Non-readonly let/const are unaffected (zero-overhead guard)
    // =========================================================================

    [Fact]
    public void NonReadonly_Let_MutationSucceeds()
    {
        object? result = Execute("let D = { x: 1 }; D.x = 2; return D.x;");
        Assert.Equal(2L, result);
    }

    [Fact]
    public void NonReadonly_Const_MutationSucceeds()
    {
        object? result = Execute("const D = { x: 1 }; D.x = 2; return D.x;");
        Assert.Equal(2L, result);
    }

    // =========================================================================
    // 13. done_when #3 specifically: inline top-level rebind scenario
    // =========================================================================

    [Fact]
    public void ReadonlyLet_TopLevel_RebindThenMutate_Throws()
    {
        // done_when: readonly let S = makeSnapshot(); S = makeSnapshot(); S.x = 1; raises ReadOnlyError
        Assert.Throws<ReadOnlyError>(() =>
            Execute("readonly let S = { x: 0 }; S = { x: 1 }; S.x = 99;"));
    }
}
