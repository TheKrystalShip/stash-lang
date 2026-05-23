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
using Stash.Stdlib.Abstractions;
using Stash.Stdlib.Models;
using Stash.Stdlib.Registration;
using Xunit;

/// <summary>
/// Regression tests confirming that every in-place mutating helper in the <c>dict.*</c>
/// namespace rejects a frozen dictionary input upfront with the standard frozen-write error.
///
/// Audited dict.* mutating helpers (full audit — no others exist):
///   dict.set    — sets a key-value pair in place
///   dict.remove — removes a key in place
///   dict.clear  — removes all entries in place
///
/// All other DictBuiltIns methods (dict.get, dict.has, dict.keys, dict.values, dict.size,
/// dict.pairs, dict.forEach, dict.merge, dict.map, dict.filter, dict.fromPairs, dict.pick,
/// dict.omit, dict.defaults, dict.any, dict.every, dict.find) operate on new allocations or
/// are read-only accessors and do not require a frozen-write guard.
/// </summary>
public class DictFrozenInputTests
{
    // =========================================================================
    // Test fixture helpers
    // =========================================================================

    /// <summary>
    /// Builds a VM with a "ns" namespace whose "d" member returns a frozen
    /// <see cref="StashDictionary"/> pre-populated with one entry.
    /// </summary>
    private static RuntimeError RunExpectingFrozenError(string source)
    {
        var builder = new NamespaceBuilder("ns");
        builder.Member(
            "d",
            _ =>
            {
                var dict = new StashDictionary();
                dict.Set("key", StashValue.FromInt(1L));
                dict.Freeze();
                return StashValue.FromObj(dict);
            },
            Stability.Live,
            "dict",
            "frozen dict member");
        var def = builder.Build();

        var globals = StdlibDefinitions.CreateVMGlobals();
        globals["ns"] = StashValue.FromObj(def.Namespace);

        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(globals);
        return Assert.ThrowsAny<RuntimeError>(() => vm.Execute(chunk));
    }

    // =========================================================================
    // Frozen-helper audit: dict.* mutators reject frozen dict input
    //
    // Audited dict.* mutating helpers:
    //   dict.set, dict.remove, dict.clear
    // =========================================================================

    [Fact]
    public void FrozenHelper_DictSet_RaisesFrozenWriteError()
    {
        var error = RunExpectingFrozenError("""
            dict.set(ns.d, "newKey", 99);
        """);
        Assert.Contains("Cannot mutate", error.Message);
    }

    [Fact]
    public void FrozenHelper_DictRemove_RaisesFrozenWriteError()
    {
        var error = RunExpectingFrozenError("""
            dict.remove(ns.d, "key");
        """);
        Assert.Contains("Cannot mutate", error.Message);
    }

    [Fact]
    public void FrozenHelper_DictClear_RaisesFrozenWriteError()
    {
        var error = RunExpectingFrozenError("""
            dict.clear(ns.d);
        """);
        Assert.Contains("Cannot mutate", error.Message);
    }

    // =========================================================================
    // Guard ordering: frozen-write error precedes type-error (IsFrozen check first)
    // =========================================================================

    [Fact]
    public void FrozenHelper_DictSet_NullKey_FrozenErrorWinsOverTypeError()
    {
        // Even with a null key (which would normally be a TypeError), the frozen-write
        // guard must fire first — consistent with the P5 arr.* ordering contract.
        var error = RunExpectingFrozenError("""
            dict.set(ns.d, null, 99);
        """);
        Assert.Contains("Cannot mutate", error.Message);
    }

    [Fact]
    public void FrozenHelper_DictRemove_NullKey_FrozenErrorWinsOverTypeError()
    {
        var error = RunExpectingFrozenError("""
            dict.remove(ns.d, null);
        """);
        Assert.Contains("Cannot mutate", error.Message);
    }
}
