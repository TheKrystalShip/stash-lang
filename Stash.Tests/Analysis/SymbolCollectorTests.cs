using Stash.Analysis;

namespace Stash.Tests.Analysis;

/// <summary>
/// Verifies that <see cref="SymbolCollector"/> correctly records binding attributes —
/// including the <c>readonly</c> modifier — on <see cref="SymbolInfo"/> entries.
/// </summary>
public class SymbolCollectorTests : AnalysisTestBase
{
    private static SymbolInfo? FindGlobalSymbol(ScopeTree tree, string name)
    {
        return tree.GlobalScope.Symbols.FirstOrDefault(s => s.Name == name);
    }

    // ── readonly modifier recorded on let ──────────────────────────────────

    [Fact]
    public void ReadonlyLetDeclaration_IsReadonly_IsTrue()
    {
        var tree = Analyze("readonly let X = 1;");
        var sym = FindGlobalSymbol(tree, "X");
        Assert.NotNull(sym);
        Assert.True(sym.IsReadonly, "Symbol for 'readonly let X' should have IsReadonly = true.");
    }

    [Fact]
    public void ReadonlyConstDeclaration_IsReadonly_IsTrue()
    {
        var tree = Analyze("readonly const Y = 2;");
        var sym = FindGlobalSymbol(tree, "Y");
        Assert.NotNull(sym);
        Assert.True(sym.IsReadonly, "Symbol for 'readonly const Y' should have IsReadonly = true.");
    }

    [Fact]
    public void PlainLetDeclaration_IsReadonly_IsFalse()
    {
        var tree = Analyze("let Z = 3;");
        var sym = FindGlobalSymbol(tree, "Z");
        Assert.NotNull(sym);
        Assert.False(sym.IsReadonly, "Symbol for a plain 'let Z' should have IsReadonly = false.");
    }

    [Fact]
    public void PlainConstDeclaration_IsReadonly_IsFalse()
    {
        var tree = Analyze("const W = 4;");
        var sym = FindGlobalSymbol(tree, "W");
        Assert.NotNull(sym);
        Assert.False(sym.IsReadonly, "Symbol for a plain 'const W' should have IsReadonly = false.");
    }

    // ── kind is still correct when readonly is present ─────────────────────

    [Fact]
    public void ReadonlyLetDeclaration_Kind_IsVariable()
    {
        var tree = Analyze("readonly let X = 1;");
        var sym = FindGlobalSymbol(tree, "X");
        Assert.NotNull(sym);
        Assert.Equal(SymbolKind.Variable, sym.Kind);
    }

    [Fact]
    public void ReadonlyConstDeclaration_Kind_IsConstant()
    {
        var tree = Analyze("readonly const Y = 2;");
        var sym = FindGlobalSymbol(tree, "Y");
        Assert.NotNull(sym);
        Assert.Equal(SymbolKind.Constant, sym.Kind);
    }
}
