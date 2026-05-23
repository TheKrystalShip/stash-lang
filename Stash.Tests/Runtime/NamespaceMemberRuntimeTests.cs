namespace Stash.Tests.Runtime;

using System;
using System.Collections.Generic;
using System.Threading;
using Stash.Bytecode;
using Stash.Common;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Resolution;
using Stash.Runtime;
using Stash.Runtime.Types;
using Stash.Stdlib;
using Stash.Stdlib.Abstractions;
using Stash.Stdlib.Models;
using Stash.Stdlib.Registration;
using Xunit;

/// <summary>
/// Tests for P2 of stdlib-namespace-members: the runtime read path in
/// <c>StashNamespace.VMGetField</c> (and the VM IC layer) is declaration-kind-aware.
/// </summary>
public class NamespaceMemberRuntimeTests
{
    // =========================================================================
    // Test fixture helpers
    // =========================================================================

    /// <summary>
    /// Compiles <paramref name="source"/> with a test namespace registered as a global.
    /// </summary>
    private static (Chunk chunk, VirtualMachine vm) CompileWithNamespace(
        string source,
        string namespaceName,
        Action<NamespaceBuilder> configure)
    {
        var builder = new NamespaceBuilder(namespaceName);
        configure(builder);
        NamespaceDefinition def = builder.Build();

        var globals = StdlibDefinitions.CreateVMGlobals();
        globals[namespaceName] = StashValue.FromObj(def.Namespace);

        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(globals);
        return (chunk, vm);
    }

    private static object? Run(string source, string namespaceName, Action<NamespaceBuilder> configure)
    {
        string full = source + "\nreturn result;";
        var (chunk, vm) = CompileWithNamespace(full, namespaceName, configure);
        return Normalize(vm.Execute(chunk));
    }

    private static void RunStatements(string source, string namespaceName, Action<NamespaceBuilder> configure)
    {
        var (chunk, vm) = CompileWithNamespace(source, namespaceName, configure);
        vm.Execute(chunk);
    }

    private static RuntimeError RunExpectingError(string source, string namespaceName, Action<NamespaceBuilder> configure)
    {
        var (chunk, vm) = CompileWithNamespace(source, namespaceName, configure);
        return Assert.ThrowsAny<RuntimeError>(() => vm.Execute(chunk));
    }

    private static object? Normalize(object? value)
    {
        if (value is List<StashValue> svList)
        {
            var result = new List<object?>(svList.Count);
            foreach (var sv in svList) result.Add(Normalize(sv.ToObject()));
            return result;
        }
        if (value is StashFrozenArray fa)
        {
            var result = new List<object?>(fa.Count);
            foreach (var sv in fa.Items) result.Add(Normalize(sv.ToObject()));
            return result;
        }
        if (value is StashValue sv2)
            return Normalize(sv2.ToObject());
        return value;
    }

    // =========================================================================
    // DataMember — Live reads mutable host-side cell (done_when #1)
    // =========================================================================

    [Fact]
    public void DataMember_Live_ReadsFromMutableCell_TwoSuccessiveReadsReturnDifferentValues()
    {
        // A Live DataMember must re-invoke the getter on each access, so two reads
        // return different values when the backing cell is mutated between them.
        //
        // NOTE: adjacent `let a = ns.x; let b = ns.x;` in the same basic block may be
        // CSE-d by the compiler into one GetFieldIC (sharing the register), so the test
        // uses two separate loop iterations to force distinct field-access instructions.
        int callCount = 0;
        long counter = 0L;
        Func<IInterpreterContext, StashValue> countingGetter = _ =>
        {
            callCount++;
            return StashValue.FromInt(++counter);
        };

        var (chunk, vm) = CompileWithNamespace(
            "let vals = [0, 0];\nvals[0] = ns.x;\nvals[1] = ns.x;\nreturn vals;",
            "ns",
            b => b.Member("x", countingGetter, Stability.Live, "int", "live counter"));

        var result = vm.Execute(chunk) as List<StashValue>;
        Assert.NotNull(result);
        Assert.Equal(2, callCount);                // getter invoked once per iteration
        Assert.Equal(1L, result![0].AsInt);        // first iteration: counter was 1
        Assert.Equal(2L, result[1].AsInt);         // second iteration: counter was 2
    }

    // =========================================================================
    // Function entry — bare read returns BuiltInFunction (done_when #2)
    // =========================================================================

    [Fact]
    public void FunctionEntry_BareRead_ReturnsBuiltInFunction()
    {
        // ns.greet accessed bare (not called) should push the BuiltInFunction value.
        // ("fn" is a Stash reserved keyword — cannot appear after a dot.)
        object? result = Run(
            "result = typeof(ns.greet);",
            "ns",
            b => b.Function("greet",
                [],
                static (ctx, args) => StashValue.FromInt(42L),
                "int", false, "test greet"));

        Assert.Equal("function", result);
    }

    // =========================================================================
    // Constant — bare read returns snapshot (done_when #3)
    // =========================================================================

    [Fact]
    public void Constant_BareRead_ReturnsStoredSnapshot()
    {
        object? result = Run(
            "result = ns.PI;",
            "ns",
            b => b.Constant("PI", 3.14, "float", "3.14", "Pi constant"));

        Assert.Equal(3.14, result);
    }

    // =========================================================================
    // Dynamic receiver path (done_when #4)
    // =========================================================================

    [Fact]
    public void DataMember_DynamicReceiver_DispatchesByKind()
    {
        // Assign namespace to a variable (dynamic receiver) and access via dot.
        int callCount = 0;
        Func<IInterpreterContext, StashValue> getter = _ =>
        {
            callCount++;
            return StashValue.FromInt(42L);
        };

        RunStatements(
            "let n = ns;\nlet v = n.x;",
            "ns",
            b => b.Member("x", getter, Stability.Live, "int", "dynamic test"));

        Assert.Equal(1, callCount);
    }

    [Fact]
    public void DataMember_DynamicReceiver_ReturnsCorrectValue()
    {
        object? result = Run(
            "let n = ns;\nresult = n.x;",
            "ns",
            b => b.Member("x",
                _ => StashValue.FromInt(99L),
                Stability.Live, "int", "dynamic value test"));

        Assert.Equal(99L, result);
    }

    // =========================================================================
    // Cached stability — getter invoked exactly once (done_when #5)
    // =========================================================================

    [Fact]
    public void DataMember_Cached_GetterInvokedOnce()
    {
        int callCount = 0;
        Func<IInterpreterContext, StashValue> getter = _ =>
        {
            Interlocked.Increment(ref callCount);
            return StashValue.FromInt(100L);
        };

        RunStatements(
            "let a = ns.x;\nlet b = ns.x;\nlet c = ns.x;",
            "ns",
            b => b.Member("x", getter, Stability.Cached, "int", "cached counter"));

        // NamespaceMemberPayload.Invoke handles Cached semantics:
        // the getter runs at most once regardless of how many times the member is accessed.
        Assert.Equal(1, callCount);
    }

    [Fact]
    public void DataMember_Cached_SubsequentReadsReturnSameLength()
    {
        // For reference-typed Cached returns, the same frozen array is returned each time.
        int callCount = 0;
        Func<IInterpreterContext, StashValue> getter = _ =>
        {
            Interlocked.Increment(ref callCount);
            return StashValue.FromObj(new List<StashValue> { StashValue.FromInt(1L), StashValue.FromInt(2L) });
        };

        var (chunk, vm) = CompileWithNamespace(
            "let a = ns.items;\nlet b = ns.items;\nreturn [len(a), len(b)];",
            "ns",
            b => b.Member("items", getter, Stability.Cached, "array", "cached array"));

        var result = vm.Execute(chunk) as List<StashValue>;
        Assert.NotNull(result);
        Assert.Equal(2L, result![0].AsInt);
        Assert.Equal(2L, result[1].AsInt);
        Assert.Equal(1, callCount); // getter ran once despite two accesses
    }

    // =========================================================================
    // Live stability — getter invoked on every access (done_when #6)
    // =========================================================================

    [Fact]
    public void DataMember_Live_GetterInvokedOnEveryAccess()
    {
        // Uses a loop of 3 iterations so the compiler cannot CSE adjacent ns.x reads.
        // Note: adjacent `let a = ns.x; let b = ns.x;` statements in the same basic block
        // may be CSE-d by the compiler into a single GetFieldIC, reducing the call count.
        // A loop guarantees one field-access instruction executed N times.
        int callCount = 0;
        Func<IInterpreterContext, StashValue> getter = _ =>
        {
            Interlocked.Increment(ref callCount);
            return StashValue.FromInt((long)callCount);
        };

        RunStatements(
            "for (let i in range(0, 3)) { let _ = ns.x; }",
            "ns",
            b => b.Member("x", getter, Stability.Live, "int", "live counter"));

        Assert.Equal(3, callCount); // getter ran once per loop iteration
    }

    // =========================================================================
    // Freeze boundary — reference-typed returns frozen (done_when #7)
    // =========================================================================

    [Fact]
    public void DataMember_ArrayReturn_IsFrozenAtBoundary()
    {
        // A getter returning List<StashValue> should produce a StashFrozenArray at the Stash level.
        // Attempting to index-assign into it must raise a RuntimeError.
        Func<IInterpreterContext, StashValue> getter = _ =>
            StashValue.FromObj(new List<StashValue> { StashValue.FromInt(1L), StashValue.FromInt(2L), StashValue.FromInt(3L) });

        var error = RunExpectingError(
            "ns.items[0] = 99;",
            "ns",
            b => b.Member("items", getter, Stability.Live, "array", "frozen array test"));

        Assert.Contains("Cannot mutate", error.Message);
    }

    [Fact]
    public void DataMember_ArrayReturn_ReadAccessWorks()
    {
        // Even though the array is frozen, reads should succeed normally.
        object? result = Run(
            "result = ns.items[1];",
            "ns",
            b => b.Member("items",
                _ => StashValue.FromObj(new List<StashValue> { StashValue.FromInt(10L), StashValue.FromInt(20L) }),
                Stability.Live, "array", "frozen array read test"));

        Assert.Equal(20L, result);
    }

    [Fact]
    public void DataMember_DictReturn_IsFrozenAtBoundary()
    {
        // A getter returning a StashDictionary should have it frozen; writes must throw.
        Func<IInterpreterContext, StashValue> getter = _ =>
        {
            var dict = new StashDictionary();
            dict.Set("key", StashValue.FromInt(42L));
            return StashValue.FromObj(dict);
        };

        var error = RunExpectingError(
            "ns.d[\"newKey\"] = 99;",
            "ns",
            b => b.Member("d", getter, Stability.Live, "dict", "frozen dict test"));

        Assert.Contains("Cannot mutate", error.Message);
    }

    [Fact]
    public void DataMember_IntReturn_NotAffectedByFreeze()
    {
        // Primitive (int) returns must pass through unmodified — no wrapping or error.
        object? result = Run(
            "result = ns.count + 1;",
            "ns",
            b => b.Member("count", _ => StashValue.FromInt(41L), Stability.Live, "int", "int test"));

        Assert.Equal(42L, result);
    }

    [Fact]
    public void DataMember_StringReturn_NotAffectedByFreeze()
    {
        object? result = Run(
            "result = ns.name;",
            "ns",
            b => b.Member("name", _ => StashValue.FromObj("hello"), Stability.Live, "string", "string test"));

        Assert.Equal("hello", result);
    }

    // =========================================================================
    // IC non-caching for DataMember (proof via Live counter test)
    // =========================================================================

    [Fact]
    public void DataMember_Live_ICDoesNotCacheValue()
    {
        // N accesses in a loop must each invoke the getter (IC fast path must not cache).
        // Uses a loop so the compiler cannot CSE the repeated ns.x access.
        int callCount = 0;
        Func<IInterpreterContext, StashValue> getter = _ =>
        {
            Interlocked.Increment(ref callCount);
            return StashValue.FromInt((long)callCount);
        };

        // GetFieldIC opcode is triggered by the compiled dot-access inside the loop body.
        RunStatements(
            "for (let i in range(0, 5)) { let _ = ns.x; }",
            "ns",
            b => b.Member("x", getter, Stability.Live, "int", "IC bypass test"));

        Assert.Equal(5, callCount);
    }

    // =========================================================================
    // Mixed namespace — Function and Constant are still IC-cacheable
    // =========================================================================

    [Fact]
    public void FunctionMember_InLoop_Works()
    {
        // Calling a function member many times should still work (IC may cache BuiltInFunction).
        int callCount = 0;
        var (chunk, vm) = CompileWithNamespace(
            "let sum = 0;\nfor (let i in range(0, 5)) { sum = sum + ns.add(i); }\nreturn sum;",
            "ns",
            b => b.Function("add",
                [new BuiltInParam("n", "int")],
                (ctx, args) =>
                {
                    callCount++;
                    return StashValue.FromInt(args[0].AsInt);
                },
                "int", false, "add fn"));

        object? result = vm.Execute(chunk);
        Assert.Equal(10L, result); // 0+1+2+3+4 = 10
        Assert.Equal(5, callCount);
    }
}
