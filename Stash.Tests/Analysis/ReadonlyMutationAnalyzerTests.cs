namespace Stash.Tests.Analysis;

using System.Linq;
using Stash.Analysis;

/// <summary>
/// Tests for SA0847 — Mutation of readonly binding.
/// Verifies that the best-effort static analyzer fires on direct mutations of
/// <c>readonly let</c> / <c>readonly const</c> bindings (field assignment, index
/// assignment, and known in-place stdlib mutator calls), and does NOT fire on aliases,
/// non-readonly bindings, or read-only accesses.
/// </summary>
public class ReadonlyMutationAnalyzerTests : AnalysisTestBase
{
    // =========================================================================
    // SA0847 must fire — field assignment (DotAssignExpr)
    // =========================================================================

    [Fact]
    public void ReadonlyConst_DotAssign_EmitsSA0847()
    {
        var source = """
            readonly const D = { x: 1 };
            D.x = 2;
            """;
        var diagnostics = Validate(source);
        Assert.Contains(diagnostics, d => d.Code == "SA0847");
    }

    [Fact]
    public void ReadonlyLet_DotAssign_EmitsSA0847()
    {
        var source = """
            readonly let D = { x: 1 };
            D.x = 2;
            """;
        var diagnostics = Validate(source);
        Assert.Contains(diagnostics, d => d.Code == "SA0847");
    }

    [Fact]
    public void SA0847_DotAssign_MessageContainsBindingName()
    {
        var source = """
            readonly const Config = { host: "localhost" };
            Config.host = "x";
            """;
        var diagnostics = Validate(source);
        var d = diagnostics.FirstOrDefault(d => d.Code == "SA0847");
        Assert.NotNull(d);
        Assert.Contains("Config", d!.Message);
    }

    [Fact]
    public void SA0847_DotAssign_SeverityIsError()
    {
        var source = """
            readonly const D = { x: 1 };
            D.x = 2;
            """;
        var diagnostics = Validate(source);
        var d = diagnostics.FirstOrDefault(d => d.Code == "SA0847");
        Assert.NotNull(d);
        Assert.Equal(DiagnosticLevel.Error, d!.Level);
    }

    // =========================================================================
    // SA0847 must fire — index assignment (IndexAssignExpr)
    // =========================================================================

    [Fact]
    public void ReadonlyConst_IndexAssign_EmitsSA0847()
    {
        var source = """
            readonly const D = [1, 2, 3];
            D[0] = 99;
            """;
        var diagnostics = Validate(source);
        Assert.Contains(diagnostics, d => d.Code == "SA0847");
    }

    [Fact]
    public void ReadonlyLet_IndexAssign_EmitsSA0847()
    {
        var source = """
            readonly let D = [1, 2, 3];
            D[1] = 42;
            """;
        var diagnostics = Validate(source);
        Assert.Contains(diagnostics, d => d.Code == "SA0847");
    }

    [Fact]
    public void SA0847_IndexAssign_MessageContainsBindingName()
    {
        var source = """
            readonly const Items = [1, 2, 3];
            Items[0] = 99;
            """;
        var diagnostics = Validate(source);
        var d = diagnostics.FirstOrDefault(d => d.Code == "SA0847");
        Assert.NotNull(d);
        Assert.Contains("Items", d!.Message);
    }

    // =========================================================================
    // SA0847 must fire — known in-place stdlib mutators (CallExpr)
    // =========================================================================

    [Fact]
    public void ReadonlyConst_ArrPush_EmitsSA0847()
    {
        var source = """
            readonly const D = [1, 2];
            arr.push(D, 3);
            """;
        var diagnostics = Validate(source);
        Assert.Contains(diagnostics, d => d.Code == "SA0847");
    }

    [Fact]
    public void ReadonlyConst_ArrPop_EmitsSA0847()
    {
        var source = """
            readonly const D = [1, 2];
            arr.pop(D);
            """;
        var diagnostics = Validate(source);
        Assert.Contains(diagnostics, d => d.Code == "SA0847");
    }

    [Fact]
    public void ReadonlyConst_ArrInsert_EmitsSA0847()
    {
        var source = """
            readonly const D = [1, 2];
            arr.insert(D, 0, 99);
            """;
        var diagnostics = Validate(source);
        Assert.Contains(diagnostics, d => d.Code == "SA0847");
    }

    [Fact]
    public void ReadonlyConst_ArrRemoveAt_EmitsSA0847()
    {
        var source = """
            readonly const D = [1, 2];
            arr.removeAt(D, 0);
            """;
        var diagnostics = Validate(source);
        Assert.Contains(diagnostics, d => d.Code == "SA0847");
    }

    [Fact]
    public void ReadonlyConst_ArrRemove_EmitsSA0847()
    {
        var source = """
            readonly const D = [1, 2];
            arr.remove(D, 1);
            """;
        var diagnostics = Validate(source);
        Assert.Contains(diagnostics, d => d.Code == "SA0847");
    }

    [Fact]
    public void ReadonlyConst_ArrClear_EmitsSA0847()
    {
        var source = """
            readonly const D = [1, 2];
            arr.clear(D);
            """;
        var diagnostics = Validate(source);
        Assert.Contains(diagnostics, d => d.Code == "SA0847");
    }

    [Fact]
    public void ReadonlyConst_ArrReverse_EmitsSA0847()
    {
        var source = """
            readonly const D = [1, 2, 3];
            arr.reverse(D);
            """;
        var diagnostics = Validate(source);
        Assert.Contains(diagnostics, d => d.Code == "SA0847");
    }

    [Fact]
    public void ReadonlyConst_ArrSort_EmitsSA0847()
    {
        var source = """
            readonly const D = [3, 1, 2];
            arr.sort(D);
            """;
        var diagnostics = Validate(source);
        Assert.Contains(diagnostics, d => d.Code == "SA0847");
    }

    [Fact]
    public void ReadonlyConst_DictSet_EmitsSA0847()
    {
        var source = """
            readonly const D = { a: 1 };
            dict.set(D, "a", 2);
            """;
        var diagnostics = Validate(source);
        Assert.Contains(diagnostics, d => d.Code == "SA0847");
    }

    [Fact]
    public void ReadonlyConst_DictRemove_EmitsSA0847()
    {
        var source = """
            readonly const D = { a: 1 };
            dict.remove(D, "a");
            """;
        var diagnostics = Validate(source);
        Assert.Contains(diagnostics, d => d.Code == "SA0847");
    }

    [Fact]
    public void ReadonlyConst_DictClear_EmitsSA0847()
    {
        var source = """
            readonly const D = { a: 1 };
            dict.clear(D);
            """;
        var diagnostics = Validate(source);
        Assert.Contains(diagnostics, d => d.Code == "SA0847");
    }

    [Fact]
    public void SA0847_MutatorCall_MessageContainsBindingName()
    {
        var source = """
            readonly const Data = [1, 2, 3];
            arr.push(Data, 4);
            """;
        var diagnostics = Validate(source);
        var d = diagnostics.FirstOrDefault(d => d.Code == "SA0847");
        Assert.NotNull(d);
        Assert.Contains("Data", d!.Message);
    }

    // =========================================================================
    // SA0847 must NOT fire — no false positives
    // =========================================================================

    [Fact]
    public void NonReadonly_Let_DotAssign_NoSA0847()
    {
        // done_when #4: mutation of a non-readonly binding produces no SA0847.
        var source = """
            let d = { x: 1 };
            d.x = 2;
            """;
        var diagnostics = Validate(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0847");
    }

    [Fact]
    public void NonReadonly_Const_DotAssign_NoSA0847()
    {
        // const without readonly is value-mutable (JS-style); no SA0847.
        var source = """
            const D = { x: 1 };
            D.x = 2;
            """;
        var diagnostics = Validate(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0847");
    }

    [Fact]
    public void Alias_DotAssign_NoSA0847()
    {
        // done_when #3: aliasing through let a = D is NOT statically diagnosed.
        // The runtime flag catches it; the analyzer must not produce a false positive.
        var source = """
            readonly const D = { x: 1 };
            let a = D;
            a.x = 2;
            """;
        var diagnostics = Validate(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0847");
    }

    [Fact]
    public void Alias_IndexAssign_NoSA0847()
    {
        var source = """
            readonly const D = [1, 2, 3];
            let alias = D;
            alias[0] = 99;
            """;
        var diagnostics = Validate(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0847");
    }

    [Fact]
    public void Alias_ArrPush_NoSA0847()
    {
        var source = """
            readonly const D = [1, 2];
            let alias = D;
            arr.push(alias, 3);
            """;
        var diagnostics = Validate(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0847");
    }

    [Fact]
    public void ReadonlyDotRead_NoSA0847()
    {
        // Reading a field of a readonly binding is fine.
        var source = """
            readonly const D = { x: 1 };
            let y = D.x;
            """;
        var diagnostics = Validate(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0847");
    }

    [Fact]
    public void NonMutatorCall_OnReadonly_NoSA0847()
    {
        // arr.length (a read) on a readonly binding should not produce SA0847.
        var source = """
            readonly const D = [1, 2, 3];
            let n = arr.length(D);
            """;
        var diagnostics = Validate(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0847");
    }

    [Fact]
    public void NonReadonly_ArrPush_NoSA0847()
    {
        // Non-readonly binding passed to arr.push is fine.
        var source = """
            let D = [1, 2];
            arr.push(D, 3);
            """;
        var diagnostics = Validate(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0847");
    }

    [Fact]
    public void StructInstance_NonReadonly_DotAssign_NoSA0847()
    {
        // Assignment to a field of a non-readonly struct instance should not trigger SA0847.
        var source = """
            struct Point { x: int; y: int; }
            let p = Point { x: 1, y: 2 };
            p.x = 10;
            """;
        var diagnostics = Validate(source);
        Assert.DoesNotContain(diagnostics, d => d.Code == "SA0847");
    }
}
