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

    // =========================================================================
    // 14. Q4 upvalue path: nested function forces set.upval (not set.global)
    //     These tests ensure the UpvalueDescriptor.IsReadonly/IsConst bits and
    //     the ExecuteSetUpval guard are actually exercised.
    // =========================================================================

    [Fact]
    public void ReadonlyLet_NestedClosure_UpvalueRebind_RefreezesAndThrows()
    {
        // S is a local inside outer(); g() captures it as an upvalue (set.upval).
        // After g() stores a new dict, S is frozen; S.x = 9 must throw.
        string source = @"
fn outer() {
  readonly let S = { x: 0 };
  fn g() { S = { x: 1 }; }
  g();
  S.x = 9;
}
outer();
";
        Assert.Throws<ReadOnlyError>(() => Execute(source));
    }

    [Fact]
    public void ReadonlyLet_NestedClosure_UpvalueRebindSucceeds_ValueReadable()
    {
        // Rebind via upvalue succeeds; result is accessible after g() returns.
        string source = @"
fn outer() {
  readonly let S = { x: 0 };
  fn g() { S = { x: 42 }; }
  g();
  return S.x;
}
return outer();
";
        object? result = Execute(source);
        Assert.Equal(42L, result);
    }

    [Fact]
    public void Const_NestedClosure_UpvalueRebind_Throws()
    {
        // Regression guard (Q4): const local captured as upvalue, rebind inside
        // nested fn → must throw (the backlog bug: previously silently succeeded).
        string source = @"
fn outer() {
  const c = 1;
  fn g() { c = 2; }
  g();
}
outer();
";
        Assert.ThrowsAny<RuntimeError>(() => Execute(source));
    }

    // =========================================================================
    // 15. F02: deep-freeze transitivity through stdlib-produced arrays
    // =========================================================================

    [Fact]
    public void ReadonlyConst_ArrSliceProducedArray_PushThrows()
    {
        // arr.slice returns a StashArray (not a bare List<StashValue>), so
        // DeepFreeze reaches it and the frozen flag sticks.
        Assert.Throws<ReadOnlyError>(() =>
            ExecuteWithStdlib(
                "readonly const D = { items: arr.slice([1,2,3], 0, 2) }; arr.push(D.items, 99);"));
    }

    [Fact]
    public void ReadonlyConst_ArrSliceProducedArray_IndexSetThrows()
    {
        // Mutation via index-assign on a stdlib-produced array should also throw.
        Assert.Throws<ReadOnlyError>(() =>
            ExecuteWithStdlib(
                "readonly const D = { items: arr.slice([1,2,3], 0, 2) }; D.items[0] = 99;"));
    }

    [Fact]
    public void ReadonlyConst_ArrMapProducedArray_MutationThrows()
    {
        // arr.map produces a StashArray; nested inside a readonly dict it must be frozen.
        Assert.Throws<ReadOnlyError>(() =>
            ExecuteWithStdlib(
                "readonly const D = { xs: arr.map([1,2,3], (x) => x*2) }; D.xs[0] = 99;"));
    }

    [Fact]
    public void ReadonlyConst_ArrChunkInnerList_MutationThrows()
    {
        // arr.chunk inner lists must also be StashArray so freeze reaches them.
        // D.chunks[0] is a chunk sub-array; mutation of it must throw.
        Assert.Throws<ReadOnlyError>(() =>
            ExecuteWithStdlib(
                "readonly const D = { chunks: arr.chunk([1,2,3,4], 2) }; D.chunks[0][0] = 99;"));
    }

    [Fact]
    public void ReadonlyConst_ArrZipInnerPair_MutationThrows()
    {
        // arr.zip inner pair arrays must be StashArray.
        Assert.Throws<ReadOnlyError>(() =>
            ExecuteWithStdlib(
                "readonly const D = { pairs: arr.zip([1,2], [3,4]) }; D.pairs[0][0] = 99;"));
    }

    // =========================================================================
    // 16. F03: typed arrays (StashTypedArray subclasses) are frozen and write-guarded
    // =========================================================================

    [Fact]
    public void ReadonlyConst_TypedIntArray_IndexSetThrows()
    {
        // arr.typed([1,2,3], "int") produces a StashIntArray; after freeze, index-set must throw.
        Assert.Throws<ReadOnlyError>(() =>
            ExecuteWithStdlib(
                "readonly const xs = arr.typed([1,2,3], \"int\"); xs[0] = 99;"));
    }

    [Fact]
    public void ReadonlyConst_TypedByteArray_IndexSetThrows()
    {
        // buf.from produces a StashByteArray; after freeze, index-set must throw.
        Assert.Throws<ReadOnlyError>(() =>
            ExecuteWithStdlib(
                "readonly const buf_data = buf.from(\"hi\"); buf_data[0] = 0;"));
    }

    [Fact]
    public void ReadonlyConst_TypedStringArray_IndexSetThrows()
    {
        // arr.typed(["a","b"], "string") produces a StashStringArray; after freeze must throw.
        Assert.Throws<ReadOnlyError>(() =>
            ExecuteWithStdlib(
                "readonly const ss = arr.typed([\"a\",\"b\"], \"string\"); ss[0] = \"c\";"));
    }

    [Fact]
    public void ReadonlyConst_TypedArray_IsStillReadable()
    {
        // After freeze the values must still be readable.
        object? result = ExecuteWithStdlib(
            "readonly const xs = arr.typed([10, 20, 30], \"int\"); return xs[1];");
        Assert.Equal(20L, result);
    }

    [Fact]
    public void ReadonlyConst_TypedIntArray_ArrPopThrows()
    {
        // arr.pop is RemoveLast — must throw on a frozen typed array.
        Assert.Throws<ReadOnlyError>(() =>
            ExecuteWithStdlib(
                "readonly const xs = arr.typed([1,2,3], \"int\"); arr.pop(xs);"));
    }

    // =========================================================================
    // 17. F08: deep-freeze through stdlib namespaces OTHER than arr.*
    //     Each producer family must return StashArray, not a bare List<StashValue>.
    // =========================================================================

    [Theory]
    [InlineData("readonly const D = json.parse(\"[1, 2, 3]\"); D[0] = 99;")]
    [InlineData("readonly const D = json.parse(\"[1, 2, 3]\"); arr.push(D, 4);")]
    [InlineData("readonly const D = { lines: str.split(\"a,b,c\", \",\") }; D.lines[0] = \"X\";")]
    [InlineData("readonly const D = { lines: str.split(\"a,b,c\", \",\") }; arr.push(D.lines, \"x\");")]
    [InlineData("readonly const D = { lines: str.lines(\"a\\nb\\nc\") }; D.lines[0] = \"X\";")]
    [InlineData("readonly const D = { chars: str.chars(\"abc\") }; D.chars[0] = \"X\";")]
    [InlineData("readonly const D = { words: str.words(\"hello world\") }; D.words[0] = \"X\";")]
    [InlineData("readonly const D = { parts: str.shellSplit(\"a b c\") }; D.parts[0] = \"X\";")]
    [InlineData("readonly const D = { ms: str.matchAll(\"aaa\", \"a\") }; arr.push(D.ms, \"x\");")]
    public void Readonly_StdlibProducerArray_MutationThrowsReadOnlyError(string src)
    {
        Assert.Throws<ReadOnlyError>(() => ExecuteWithStdlib(src));
    }

    [Fact]
    public void ReadonlyConst_JsonParseNestedArray_DeepFreezeWorks()
    {
        // Nested JSON arrays must also be StashArray and frozen transitively.
        Assert.Throws<ReadOnlyError>(() =>
            ExecuteWithStdlib("readonly const D = json.parse(\"{\\\"nums\\\":[1,2,3]}\"); D.nums[0] = 99;"));
    }

    [Fact]
    public void ReadonlyConst_StrSplitProducedArray_StillReadable()
    {
        // After freeze, the array must remain readable (no regression on read path).
        object? result = ExecuteWithStdlib(
            "readonly const D = str.split(\"a,b,c\", \",\"); return D[1];");
        Assert.Equal("b", result);
    }

    [Fact]
    public void ReadonlyConst_JsonParseTopLevelArray_StillReadable()
    {
        // After freeze, json.parse result must remain readable.
        // json.parse returns integer-valued numbers as long.
        object? result = ExecuteWithStdlib(
            "readonly const D = json.parse(\"[10, 20, 30]\"); return D[1];");
        Assert.Equal(20L, result);
    }

    [Fact]
    public void ReadonlyConst_XmlParseChildrenField_MutationThrowsReadOnlyError()
    {
        // XmlNode.children is a List<StashValue> stored via FromObj(children) in MakeXmlNode.
        // This is the struct-field-carrier case that the marshaller path misses but
        // the FromObj normalization catches. Confirmed fix covers it.
        Assert.Throws<ReadOnlyError>(() =>
            ExecuteWithStdlib(
                "readonly const D = xml.parse(\"<a><b/><c/></a>\"); arr.push(D.children, \"x\");"));
    }

    [Fact]
    public void ReadonlyConst_TomlParseArrayField_MutationThrowsReadOnlyError()
    {
        // toml.parse returns arrays as List<StashValue> via ConvertArray.
        // After readonly freeze, mutation must throw.
        Assert.Throws<ReadOnlyError>(() =>
            ExecuteWithStdlib(
                "readonly const D = toml.parse(\"nums = [1, 2, 3]\"); D.nums[0] = 99;"));
    }

    // =========================================================================
    // 18. Choke-point invariant: FromObj/FromObject normalize bare lists
    //     Locks the bug-class shut — red if normalization is removed.
    // =========================================================================

    [Fact]
    public void FromObj_BareList_NormalizesToStashArray()
    {
        // StashValue.FromObj must wrap bare List<StashValue> in StashArray so that
        // every array reaching a StashValue carries the IsFrozen bit.
        var list = new System.Collections.Generic.List<StashValue> { StashValue.FromInt(1) };
        object? obj = StashValue.FromObj(list).AsObj;
        Assert.IsType<StashArray>(obj);
    }

    [Fact]
    public void FromObject_BareList_NormalizesToStashArray()
    {
        // StashValue.FromObject must also normalize bare lists coming through
        // the object? boundary (e.g. IStashCallable returns, StashFuture results).
        var list = new System.Collections.Generic.List<StashValue> { StashValue.FromInt(1) };
        object? obj = StashValue.FromObject(list).AsObj;
        Assert.IsType<StashArray>(obj);
    }

    [Fact]
    public void FromObj_StashArray_IsNotDoubleWrapped()
    {
        // A StashArray passed to FromObj must not be wrapped again — it is already
        // a StashArray and should remain the same reference.
        var arr = new StashArray { StashValue.FromInt(1) };
        object? obj = StashValue.FromObj(arr).AsObj;
        Assert.Same(arr, obj);
    }
}
