namespace Stash.Tests.Runtime;

using System.Collections.Generic;
using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Runtime.Errors;
using Stash.Runtime.Types;
using Stash.Stdlib;

/// <summary>
/// Direct C# unit tests for the P3 deep-freeze primitive.
/// These tests exercise the runtime machinery — either in isolation (no Stash pipeline)
/// or via a thin C# wrapper that injects pre-frozen values as globals so the VM write
/// paths can be reached without P4 compiler wiring.
/// </summary>
public class FreezeTests
{
    // =========================================================================
    // VM write-path helpers
    // =========================================================================

    /// <summary>
    /// Compiles and runs a snippet that refers to an injected global, asserting
    /// <see cref="ReadOnlyError"/> is thrown.
    /// </summary>
    private static RuntimeError RunWithFrozenGlobal(string source, string globalName, StashValue globalValue)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var globals = StdlibDefinitions.CreateVMGlobals();
        globals[globalName] = globalValue;
        var vm = new VirtualMachine(globals);
        return Assert.ThrowsAny<RuntimeError>(() => vm.Execute(chunk));
    }
    // =========================================================================
    // StashArray — IsFrozen bit + write guard
    // =========================================================================

    [Fact]
    public void StashArray_InitiallyNotFrozen()
    {
        var arr = new StashArray();
        Assert.False(arr.IsFrozen);
    }

    [Fact]
    public void StashArray_Freeze_SetsIsFrozen()
    {
        var arr = new StashArray();
        arr.Freeze();
        Assert.True(arr.IsFrozen);
    }

    [Fact]
    public void StashArray_Freeze_Idempotent()
    {
        var arr = new StashArray();
        arr.Freeze();
        arr.Freeze(); // should not throw
        Assert.True(arr.IsFrozen);
    }

    [Fact]
    public void StashArray_IsListSubtype_ForPolymorphism()
    {
        // The identity-preserving promise: a StashArray IS-A List<StashValue>
        var arr = new StashArray();
        Assert.IsAssignableFrom<List<StashValue>>(arr);
    }

    // =========================================================================
    // StashDictionary — Freeze + write guards (generalized messages)
    // =========================================================================

    [Fact]
    public void StashDictionary_Freeze_SetsIsFrozen()
    {
        var dict = new StashDictionary();
        dict.Freeze();
        Assert.True(dict.IsFrozen);
    }

    [Fact]
    public void StashDictionary_Set_WhenFrozen_ThrowsReadOnlyError()
    {
        var dict = new StashDictionary();
        dict.Freeze();
        var ex = Assert.Throws<ReadOnlyError>(() => dict.Set("key", StashValue.One));
        Assert.Contains("frozen", ex.Message);
        Assert.DoesNotContain("namespace member", ex.Message);
    }

    [Fact]
    public void StashDictionary_Remove_WhenFrozen_ThrowsReadOnlyError()
    {
        var dict = new StashDictionary();
        dict.Set("key", StashValue.One);
        dict.Freeze();
        var ex = Assert.Throws<ReadOnlyError>(() => dict.Remove("key"));
        Assert.Contains("frozen", ex.Message);
    }

    [Fact]
    public void StashDictionary_Clear_WhenFrozen_ThrowsReadOnlyError()
    {
        var dict = new StashDictionary();
        dict.Set("key", StashValue.One);
        dict.Freeze();
        var ex = Assert.Throws<ReadOnlyError>(() => dict.Clear());
        Assert.Contains("frozen", ex.Message);
    }

    // =========================================================================
    // StashInstance — IsFrozen bit + write guard on SetField
    // =========================================================================

    [Fact]
    public void StashInstance_InitiallyNotFrozen()
    {
        var inst = new StashInstance("Foo", new Dictionary<string, StashValue> { ["x"] = StashValue.One });
        Assert.False(inst.IsFrozen);
    }

    [Fact]
    public void StashInstance_Freeze_SetsIsFrozen()
    {
        var inst = new StashInstance("Foo", new Dictionary<string, StashValue> { ["x"] = StashValue.One });
        inst.Freeze();
        Assert.True(inst.IsFrozen);
    }

    [Fact]
    public void StashInstance_SetField_WhenFrozen_ThrowsReadOnlyError()
    {
        var inst = new StashInstance("Foo", new Dictionary<string, StashValue> { ["x"] = StashValue.One });
        inst.Freeze();
        var ex = Assert.Throws<ReadOnlyError>(() => inst.SetField("x", StashValue.Zero, null));
        Assert.Contains("frozen", ex.Message);
    }

    // =========================================================================
    // DeepFreeze — primitives are no-ops
    // =========================================================================

    [Fact]
    public void DeepFreeze_Null_NoOp()
    {
        RuntimeValues.DeepFreeze(StashValue.Null); // must not throw
    }

    [Fact]
    public void DeepFreeze_Int_NoOp()
    {
        RuntimeValues.DeepFreeze(StashValue.One); // must not throw
    }

    [Fact]
    public void DeepFreeze_Float_NoOp()
    {
        RuntimeValues.DeepFreeze(StashValue.FromFloat(3.14)); // must not throw
    }

    [Fact]
    public void DeepFreeze_Bool_NoOp()
    {
        RuntimeValues.DeepFreeze(StashValue.True); // must not throw
    }

    [Fact]
    public void DeepFreeze_String_NoOp()
    {
        RuntimeValues.DeepFreeze(StashValue.FromObj("hello")); // must not throw
    }

    // =========================================================================
    // DeepFreeze — StashArray
    // =========================================================================

    [Fact]
    public void DeepFreeze_StashArray_SetsIsFrozen()
    {
        var arr = new StashArray { StashValue.One, StashValue.Zero };
        RuntimeValues.DeepFreeze(StashValue.FromObj(arr));
        Assert.True(arr.IsFrozen);
    }

    [Fact]
    public void DeepFreeze_StashArray_NestedArray_AlsoFrozen()
    {
        var inner = new StashArray { StashValue.One };
        var outer = new StashArray { StashValue.FromObj(inner) };
        RuntimeValues.DeepFreeze(StashValue.FromObj(outer));
        Assert.True(outer.IsFrozen);
        Assert.True(inner.IsFrozen);
    }

    // =========================================================================
    // DeepFreeze — StashDictionary
    // =========================================================================

    [Fact]
    public void DeepFreeze_StashDictionary_SetsIsFrozen()
    {
        var dict = new StashDictionary();
        dict.Set("k", StashValue.One);
        RuntimeValues.DeepFreeze(StashValue.FromObj(dict));
        Assert.True(dict.IsFrozen);
    }

    [Fact]
    public void DeepFreeze_StashDictionary_NestedDict_AlsoFrozen()
    {
        var inner = new StashDictionary();
        inner.Set("a", StashValue.One);
        var outer = new StashDictionary();
        outer.Set("nested", StashValue.FromObj(inner));
        RuntimeValues.DeepFreeze(StashValue.FromObj(outer));
        Assert.True(outer.IsFrozen);
        Assert.True(inner.IsFrozen);
    }

    [Fact]
    public void DeepFreeze_StashDictionary_NestedArray_AlsoFrozen()
    {
        var arr = new StashArray { StashValue.One, StashValue.Zero };
        var dict = new StashDictionary();
        dict.Set("arr", StashValue.FromObj(arr));
        RuntimeValues.DeepFreeze(StashValue.FromObj(dict));
        Assert.True(dict.IsFrozen);
        Assert.True(arr.IsFrozen);
    }

    // =========================================================================
    // DeepFreeze — StashInstance
    // =========================================================================

    [Fact]
    public void DeepFreeze_StashInstance_SetsIsFrozen()
    {
        var inst = new StashInstance("Foo", new Dictionary<string, StashValue> { ["x"] = StashValue.One });
        RuntimeValues.DeepFreeze(StashValue.FromObj(inst));
        Assert.True(inst.IsFrozen);
    }

    [Fact]
    public void DeepFreeze_StashInstance_NestedDict_AlsoFrozen()
    {
        var nested = new StashDictionary();
        nested.Set("a", StashValue.Zero);
        var inst = new StashInstance("Foo", new Dictionary<string, StashValue>
        {
            ["nested"] = StashValue.FromObj(nested)
        });
        RuntimeValues.DeepFreeze(StashValue.FromObj(inst));
        Assert.True(inst.IsFrozen);
        Assert.True(nested.IsFrozen);
    }

    // =========================================================================
    // DeepFreeze — cycle safety
    // =========================================================================

    [Fact]
    public void DeepFreeze_CyclicDict_DoesNotStackOverflow()
    {
        var dict = new StashDictionary();
        dict.Set("self", StashValue.FromObj(dict)); // self-reference
        // Must not throw StackOverflowException
        RuntimeValues.DeepFreeze(StashValue.FromObj(dict));
        Assert.True(dict.IsFrozen);
    }

    [Fact]
    public void DeepFreeze_CyclicArray_DoesNotStackOverflow()
    {
        var arr = new StashArray();
        arr.Add(StashValue.FromObj(arr)); // self-reference
        RuntimeValues.DeepFreeze(StashValue.FromObj(arr));
        Assert.True(arr.IsFrozen);
    }

    // =========================================================================
    // DeepFreeze — aliased mutation throws (flag is on value, not binding)
    // =========================================================================

    [Fact]
    public void DeepFreeze_AliasedDict_MutationThrows()
    {
        var dict = new StashDictionary();
        dict.Set("x", StashValue.One);
        // Simulated alias: same reference
        StashDictionary alias = dict;
        RuntimeValues.DeepFreeze(StashValue.FromObj(dict));
        // Now try to mutate through the alias
        Assert.Throws<ReadOnlyError>(() => alias.Set("x", StashValue.Zero));
    }

    [Fact]
    public void DeepFreeze_AliasedNestedArray_FlagIsOnValue()
    {
        // The IsFrozen bit is on the value, not the binding.
        // After deep-freezing a graph, the nested StashArray reports IsFrozen=true
        // through any alias, because they share the same object reference.
        var inner = new StashArray { StashValue.One };
        StashArray innerAlias = inner; // pre-freeze alias
        var dict = new StashDictionary();
        dict.Set("arr", StashValue.FromObj(inner));
        RuntimeValues.DeepFreeze(StashValue.FromObj(dict));
        // The alias sees the frozen flag on the same object
        Assert.True(innerAlias.IsFrozen);
        Assert.True(ReferenceEquals(inner, innerAlias)); // still same ref
    }

    // =========================================================================
    // StashError — DeepFreeze traverses into Properties (no write-guard added)
    // =========================================================================

    [Fact]
    public void DeepFreeze_StashError_DoesNotThrow()
    {
        // StashError has no IsFrozen; DeepFreeze should traverse without error
        var err = new StashError("test", "TestError", null, new Dictionary<string, object?> { ["data"] = "value" });
        RuntimeValues.DeepFreeze(StashValue.FromObj(err)); // must not throw
    }

    [Fact]
    public void DeepFreeze_StashError_WithNestedDict_FreezesDictValue()
    {
        // If a StashError's Properties has a StashDictionary value, DeepFreeze
        // should traverse into it and freeze the nested dict.
        var nestedDict = new StashDictionary();
        nestedDict.Set("k", StashValue.One);
        var err = new StashError("test", "TestError", null, new Dictionary<string, object?> { ["data"] = (object?)nestedDict });
        RuntimeValues.DeepFreeze(StashValue.FromObj(err));
        Assert.True(nestedDict.IsFrozen);
    }

    // =========================================================================
    // ReadOnlyError message — generalized per kind
    // =========================================================================

    [Fact]
    public void ReadOnlyError_Dict_MessageMentionsDict()
    {
        var dict = new StashDictionary();
        dict.Freeze();
        var ex = Assert.Throws<ReadOnlyError>(() => dict.Set("k", StashValue.One));
        Assert.Contains("dict", ex.Message);
    }

    [Fact]
    public void ReadOnlyError_Instance_MessageMentionsStruct()
    {
        var inst = new StashInstance("MyStruct", new Dictionary<string, StashValue> { ["x"] = StashValue.One });
        inst.Freeze();
        var ex = Assert.Throws<ReadOnlyError>(() => inst.SetField("x", StashValue.Zero, null));
        Assert.Contains("struct", ex.Message);
    }

    // =========================================================================
    // VM write-path tests — frozen value injected as a global, VM tries to write
    // =========================================================================

    [Fact]
    public void VM_IndexSet_OnFrozenArray_ThrowsReadOnlyError()
    {
        // Build a frozen StashArray and inject as global; then index-assign via VM.
        var arr = new StashArray { StashValue.One, StashValue.FromInt(2L) };
        arr.Freeze();
        var ex = RunWithFrozenGlobal("frozenArr[0] = 99;", "frozenArr", StashValue.FromObj(arr));
        Assert.IsType<ReadOnlyError>(ex);
        Assert.Contains("frozen", ex.Message);
    }

    [Fact]
    public void VM_IndexSet_OnFrozenDict_ThrowsReadOnlyError()
    {
        var dict = new StashDictionary();
        dict.Set("k", StashValue.One);
        dict.Freeze();
        var ex = RunWithFrozenGlobal("frozenDict[\"k\"] = 99;", "frozenDict", StashValue.FromObj(dict));
        Assert.IsType<ReadOnlyError>(ex);
        Assert.Contains("frozen", ex.Message);
    }

    [Fact]
    public void VM_FieldSet_OnFrozenInstance_ThrowsReadOnlyError()
    {
        var inst = new StashInstance("Foo", new Dictionary<string, StashValue> { ["x"] = StashValue.One });
        inst.Freeze();
        var ex = RunWithFrozenGlobal("frozenInst.x = 99;", "frozenInst", StashValue.FromObj(inst));
        Assert.IsType<ReadOnlyError>(ex);
        Assert.Contains("frozen", ex.Message);
    }

    [Fact]
    public void VM_ArrPush_OnFrozenArray_ThrowsReadOnlyError()
    {
        // arr.push is an in-place mutator — must throw on a frozen StashArray.
        var arr = new StashArray { StashValue.One };
        arr.Freeze();
        var ex = RunWithFrozenGlobal("arr.push(frozenArr, 99);", "frozenArr", StashValue.FromObj(arr));
        Assert.IsType<ReadOnlyError>(ex);
        Assert.Contains("frozen", ex.Message);
    }

    [Fact]
    public void VM_DeepFreeze_NestedArray_IndexSet_ThrowsReadOnlyError()
    {
        // Deep-freeze a dict that contains an array; injection ensures VM gets the
        // frozen graph. Writes to the nested array must throw.
        var inner = new StashArray { StashValue.One, StashValue.FromInt(2L) };
        var outer = new StashDictionary();
        outer.Set("arr", StashValue.FromObj(inner));
        RuntimeValues.DeepFreeze(StashValue.FromObj(outer));
        // Extract the inner frozen array and try to write via index-set in the VM.
        var ex = RunWithFrozenGlobal("frozenInner[0] = 99;", "frozenInner", StashValue.FromObj(inner));
        Assert.IsType<ReadOnlyError>(ex);
    }

    // =========================================================================
    // F02: DeepFreeze traverses into stdlib-produced StashArray values
    // =========================================================================

    [Fact]
    public void DeepFreeze_TraversesStdlibProducedArray_SliceIsStashArray()
    {
        // After the F02 fix, arr.slice returns a StashArray. Verify it carries IsFrozen
        // by constructing an equivalent StashArray in C# and deep-freezing a dict holding it.
        var sliceResult = new StashArray { StashValue.FromInt(1L), StashValue.FromInt(2L) };
        var dict = new StashDictionary();
        dict.Set("items", StashValue.FromObj(sliceResult));
        RuntimeValues.DeepFreeze(StashValue.FromObj(dict));
        Assert.True(sliceResult.IsFrozen);
    }

    [Fact]
    public void DeepFreeze_TraversesStdlibProducedArray_MapIsStashArray()
    {
        // arr.map result is also a StashArray; nested in a dict it must be frozen.
        var mapResult = new StashArray { StashValue.FromInt(2L), StashValue.FromInt(4L) };
        var dict = new StashDictionary();
        dict.Set("xs", StashValue.FromObj(mapResult));
        RuntimeValues.DeepFreeze(StashValue.FromObj(dict));
        Assert.True(mapResult.IsFrozen);
    }

    [Fact]
    public void DeepFreeze_BareLists_NestedCarriersStillFrozen()
    {
        // Safety-net: if a bare List<StashValue> somehow slips through (e.g. from
        // a future un-migrated producer), DeepFreeze should still recurse into it
        // and freeze any nested StashArray/StashDictionary carriers.
        var innerArr = new StashArray { StashValue.One };
        var bareList = new List<StashValue> { StashValue.FromObj(innerArr) };
        var dict = new StashDictionary();
        dict.Set("items", StashValue.FromObj(bareList));
        RuntimeValues.DeepFreeze(StashValue.FromObj(dict));
        // The bare list has no IsFrozen, but the nested StashArray must be frozen.
        Assert.True(innerArr.IsFrozen);
    }

    [Fact]
    public void DeepFreeze_TraversesStdlibProducedArray_ZipInnerPairsAreStashArray()
    {
        // arr.zip inner pair arrays must be StashArray, so they get frozen transitively.
        var innerPair = new StashArray { StashValue.One, StashValue.FromInt(3L) };
        var outerArr = new StashArray { StashValue.FromObj(innerPair) };
        var dict = new StashDictionary();
        dict.Set("pairs", StashValue.FromObj(outerArr));
        RuntimeValues.DeepFreeze(StashValue.FromObj(dict));
        Assert.True(outerArr.IsFrozen);
        Assert.True(innerPair.IsFrozen);
    }

    // =========================================================================
    // F03: StashTypedArray — IsFrozen bit + write guards
    // =========================================================================

    [Fact]
    public void DeepFreeze_OnStashIntArray_FreezesAndBlocksWrites()
    {
        var ta = new StashIntArray(new List<StashValue> { StashValue.One, StashValue.FromInt(2L) });
        RuntimeValues.DeepFreeze(StashValue.FromObj(ta));
        Assert.True(ta.IsFrozen);
        var ex = Assert.Throws<ReadOnlyError>(() => ta.Set(0, StashValue.FromInt(99L)));
        Assert.Contains("frozen", ex.Message);
        Assert.Contains("int", ex.Message);
    }

    [Fact]
    public void DeepFreeze_OnStashByteArray_FreezesAndBlocksWrites()
    {
        var ta = new StashByteArray(new byte[] { 0x01, 0x02 });
        RuntimeValues.DeepFreeze(StashValue.FromObj(ta));
        Assert.True(ta.IsFrozen);
        var ex = Assert.Throws<ReadOnlyError>(() => ta.Set(0, StashValue.FromByte(0xFF)));
        Assert.Contains("frozen", ex.Message);
        Assert.Contains("byte", ex.Message);
    }

    [Fact]
    public void DeepFreeze_OnStashStringArray_FreezesAndBlocksWrites()
    {
        var ta = new StashStringArray(new List<StashValue> { StashValue.FromObj("hello"), StashValue.FromObj("world") });
        RuntimeValues.DeepFreeze(StashValue.FromObj(ta));
        Assert.True(ta.IsFrozen);
        var ex = Assert.Throws<ReadOnlyError>(() => ta.Set(0, StashValue.FromObj("changed")));
        Assert.Contains("frozen", ex.Message);
        Assert.Contains("string", ex.Message);
    }

    [Fact]
    public void StashTypedArray_Add_WhenFrozen_ThrowsReadOnlyError()
    {
        var ta = new StashIntArray(new List<StashValue> { StashValue.One });
        ta.Freeze();
        var ex = Assert.Throws<ReadOnlyError>(() => ta.Add(StashValue.FromInt(2L)));
        Assert.Contains("frozen", ex.Message);
    }

    [Fact]
    public void StashTypedArray_Clear_WhenFrozen_ThrowsReadOnlyError()
    {
        var ta = new StashIntArray(new List<StashValue> { StashValue.One });
        ta.Freeze();
        var ex = Assert.Throws<ReadOnlyError>(() => ta.Clear());
        Assert.Contains("frozen", ex.Message);
    }

    [Fact]
    public void StashTypedArray_RemoveAt_WhenFrozen_ThrowsReadOnlyError()
    {
        var ta = new StashIntArray(new List<StashValue> { StashValue.One });
        ta.Freeze();
        var ex = Assert.Throws<ReadOnlyError>(() => ta.RemoveAt(0));
        Assert.Contains("frozen", ex.Message);
    }

    [Fact]
    public void StashTypedArray_Insert_WhenFrozen_ThrowsReadOnlyError()
    {
        var ta = new StashIntArray(new List<StashValue> { StashValue.One });
        ta.Freeze();
        var ex = Assert.Throws<ReadOnlyError>(() => ta.Insert(0, StashValue.FromInt(2L)));
        Assert.Contains("frozen", ex.Message);
    }

    [Fact]
    public void StashByteArray_GetBackingArray_WhenFrozen_ThrowsReadOnlyError()
    {
        var ba = new StashByteArray(new byte[] { 0x01, 0x02 });
        ba.Freeze();
        var ex = Assert.Throws<ReadOnlyError>(() => ba.GetBackingArray(out int _));
        Assert.Contains("frozen", ex.Message);
    }

    [Fact]
    public void StashTypedArray_Freeze_Idempotent()
    {
        var ta = new StashIntArray(new List<StashValue> { StashValue.One });
        ta.Freeze();
        ta.Freeze(); // must not throw
        Assert.True(ta.IsFrozen);
    }

    [Fact]
    public void VM_IndexSet_OnFrozenIntArray_ThrowsReadOnlyError()
    {
        var ta = new StashIntArray(new List<StashValue> { StashValue.One, StashValue.FromInt(2L) });
        ta.Freeze();
        var ex = RunWithFrozenGlobal("frozenArr[0] = 99;", "frozenArr", StashValue.FromObj(ta));
        Assert.IsType<ReadOnlyError>(ex);
        Assert.Contains("frozen", ex.Message);
    }

    [Fact]
    public void VM_IndexSet_OnFrozenByteArray_ThrowsReadOnlyError()
    {
        var ba = new StashByteArray(new byte[] { 0x68, 0x69 }); // "hi"
        ba.Freeze();
        var ex = RunWithFrozenGlobal("frozenBuf[0] = 0;", "frozenBuf", StashValue.FromObj(ba));
        Assert.IsType<ReadOnlyError>(ex);
        Assert.Contains("frozen", ex.Message);
    }
}
