using Stash.Analysis;

namespace Stash.Tests.Analysis;

public class ScopeTreeTests : AnalysisTestBase
{
    [Fact]
    public void GlobalScope_ContainsTopLevelDeclarations()
    {
        var tree = Analyze("let x = 1; const Y = 2; fn foo() {}");

        Assert.Equal(3, tree.GlobalScope.Symbols.Count);
        Assert.Contains(tree.GlobalScope.Symbols, s => s.Name == "x" && s.Kind == SymbolKind.Variable);
        Assert.Contains(tree.GlobalScope.Symbols, s => s.Name == "Y" && s.Kind == SymbolKind.Constant);
        Assert.Contains(tree.GlobalScope.Symbols, s => s.Name == "foo" && s.Kind == SymbolKind.Function);
    }

    [Fact]
    public void FunctionScope_ContainsParameters()
    {
        var tree = Analyze("fn add(a, b) { return a + b; }");

        Assert.Single(tree.GlobalScope.Symbols);
        Assert.Equal("add", tree.GlobalScope.Symbols[0].Name);
        Assert.Equal(SymbolKind.Function, tree.GlobalScope.Symbols[0].Kind);

        Assert.Single(tree.GlobalScope.Children);
        var fnScope = tree.GlobalScope.Children[0];
        Assert.Equal(ScopeKind.Function, fnScope.Kind);
        Assert.Equal(2, fnScope.Symbols.Count);
        Assert.Contains(fnScope.Symbols, s => s.Name == "a" && s.Kind == SymbolKind.Parameter);
        Assert.Contains(fnScope.Symbols, s => s.Name == "b" && s.Kind == SymbolKind.Parameter);
    }

    [Fact]
    public void FunctionScope_ContainsLocalVariables()
    {
        var tree = Analyze("fn work() { let x = 1; let y = 2; }");

        Assert.Single(tree.GlobalScope.Children);
        var fnScope = tree.GlobalScope.Children[0];
        Assert.Contains(fnScope.Symbols, s => s.Name == "x" && s.Kind == SymbolKind.Variable);
        Assert.Contains(fnScope.Symbols, s => s.Name == "y" && s.Kind == SymbolKind.Variable);
    }

    [Fact]
    public void BlockScope_CreatedByIfBranches()
    {
        // Line 1: let x = 1;
        // Line 2: if (true) {
        // Line 3:     let y = 2;
        // Line 4: }
        const string src = "let x = 1;\nif (true) {\n    let y = 2;\n}";
        var tree = Analyze(src);

        Assert.Single(tree.GlobalScope.Symbols);
        Assert.Equal("x", tree.GlobalScope.Symbols[0].Name);

        Assert.Single(tree.GlobalScope.Children);
        var blockScope = tree.GlobalScope.Children[0];
        Assert.Equal(ScopeKind.Block, blockScope.Kind);
        Assert.Contains(blockScope.Symbols, s => s.Name == "y" && s.Kind == SymbolKind.Variable);

        Assert.DoesNotContain(tree.GlobalScope.Symbols, s => s.Name == "y");
    }

    [Fact]
    public void LoopScope_ForInVariable()
    {
        var tree = Analyze("for (let item in [1, 2, 3]) { let inner = item; }");

        Assert.Empty(tree.GlobalScope.Symbols);
        Assert.Single(tree.GlobalScope.Children);
        var loopScope = tree.GlobalScope.Children[0];
        Assert.Equal(ScopeKind.Loop, loopScope.Kind);
        Assert.Contains(loopScope.Symbols, s => s.Name == "item" && s.Kind == SymbolKind.LoopVariable);
        Assert.Contains(loopScope.Symbols, s => s.Name == "inner" && s.Kind == SymbolKind.Variable);
    }

    [Fact]
    public void NestedFunctions_CreateSeparateScopes()
    {
        // Line 1: fn outer() {
        // Line 2:     let x = 1;
        // Line 3:     fn inner() {
        // Line 4:         let y = 2;
        // Line 5:     }
        // Line 6: }
        const string src = "fn outer() {\n    let x = 1;\n    fn inner() {\n        let y = 2;\n    }\n}";
        var tree = Analyze(src);

        Assert.Single(tree.GlobalScope.Symbols);
        Assert.Equal("outer", tree.GlobalScope.Symbols[0].Name);

        Assert.Single(tree.GlobalScope.Children);
        var outerFnScope = tree.GlobalScope.Children[0];
        Assert.Equal(ScopeKind.Function, outerFnScope.Kind);
        Assert.Contains(outerFnScope.Symbols, s => s.Name == "x" && s.Kind == SymbolKind.Variable);
        Assert.Contains(outerFnScope.Symbols, s => s.Name == "inner" && s.Kind == SymbolKind.Function);

        Assert.Single(outerFnScope.Children);
        var innerFnScope = outerFnScope.Children[0];
        Assert.Equal(ScopeKind.Function, innerFnScope.Kind);
        Assert.Contains(innerFnScope.Symbols, s => s.Name == "y" && s.Kind == SymbolKind.Variable);
        Assert.DoesNotContain(innerFnScope.Symbols, s => s.Name == "x");
    }

    [Fact]
    public void FindDefinition_ResolvesInnerScopeFirst()
    {
        // Line 1: let x = 1;
        // Line 2: fn foo() {
        // Line 3:     let x = 2;      <- inner x at col 9
        // Line 4: }
        // Query at (3, 15): inside function scope, after inner x declaration.
        const string src = "let x = 1;\nfn foo() {\n    let x = 2;\n}";
        var tree = Analyze(src);

        var sym = tree.FindDefinition("x", 3, 15);

        Assert.NotNull(sym);
        Assert.Equal(3, sym.Span.StartLine);
    }

    [Fact]
    public void FindDefinition_FallsBackToOuterScope()
    {
        // Line 1: let x = 1;
        // Line 2: fn foo() {
        // Line 3:     let y = x;
        // Line 4: }
        // Query at (3, 15): inside function scope, x is not declared locally.
        const string src = "let x = 1;\nfn foo() {\n    let y = x;\n}";
        var tree = Analyze(src);

        var sym = tree.FindDefinition("x", 3, 15);

        Assert.NotNull(sym);
        Assert.Equal(1, sym.Span.StartLine);
    }

    [Fact]
    public void GetVisibleSymbols_ReturnsOnlyInScopeSymbols()
    {
        // Line 1: let a = 1;
        // Line 2: fn foo(b) {
        // Line 3:     let c = 2;
        // Line 4: }
        // Line 5: let d = 3;
        // Query at (3, 15): inside function body.
        const string src = "let a = 1;\nfn foo(b) {\n    let c = 2;\n}\nlet d = 3;";
        var tree = Analyze(src);

        var visible = tree.GetVisibleSymbols(3, 15).ToList();

        Assert.Contains(visible, s => s.Name == "c");
        Assert.Contains(visible, s => s.Name == "b");
        Assert.Contains(visible, s => s.Name == "foo");
        Assert.Contains(visible, s => s.Name == "a");
        Assert.DoesNotContain(visible, s => s.Name == "d");
    }

    [Fact]
    public void GetVisibleSymbols_ExcludesOutOfScopeBlockVariables()
    {
        // Line 1: if (true) {
        // Line 2:     let x = 1;
        // Line 3: }
        // Line 4: let y = 2;        <- y at col 5
        // Query at (4, 10): in global scope, after the if block.
        const string src = "if (true) {\n    let x = 1;\n}\nlet y = 2;";
        var tree = Analyze(src);

        var visible = tree.GetVisibleSymbols(4, 10).ToList();

        Assert.Contains(visible, s => s.Name == "y");
        Assert.DoesNotContain(visible, s => s.Name == "x");
    }

    [Fact]
    public void GetTopLevel_ReturnsOnlyGlobalSymbols()
    {
        var tree = Analyze("let x = 1; fn foo() { let y = 2; }");

        var topLevel = tree.GetTopLevel().ToList();

        Assert.Contains(topLevel, s => s.Name == "x");
        Assert.Contains(topLevel, s => s.Name == "foo");
        Assert.DoesNotContain(topLevel, s => s.Name == "y");
    }

    [Fact]
    public void StructAndEnum_InGlobalScope()
    {
        const string src = "struct Point { x, y }\nenum Color { Red, Green, Blue }";
        var tree = Analyze(src);

        var syms = tree.GlobalScope.Symbols;
        Assert.Contains(syms, s => s.Name == "Point" && s.Kind == SymbolKind.Struct);
        Assert.Contains(syms, s => s.Name == "x" && s.Kind == SymbolKind.Field);
        Assert.Contains(syms, s => s.Name == "y" && s.Kind == SymbolKind.Field);
        Assert.Contains(syms, s => s.Name == "Color" && s.Kind == SymbolKind.Enum);
        Assert.Contains(syms, s => s.Name == "Red" && s.Kind == SymbolKind.EnumMember);
        Assert.Contains(syms, s => s.Name == "Green" && s.Kind == SymbolKind.EnumMember);
        Assert.Contains(syms, s => s.Name == "Blue" && s.Kind == SymbolKind.EnumMember);
    }

    [Fact]
    public void WhileLoop_CreatesLoopScope()
    {
        const string src = "while (true) {\n    let x = 1;\n}";
        var tree = Analyze(src);

        Assert.Single(tree.GlobalScope.Children);
        var loopScope = tree.GlobalScope.Children[0];
        Assert.Equal(ScopeKind.Loop, loopScope.Kind);
        Assert.Contains(loopScope.Symbols, s => s.Name == "x" && s.Kind == SymbolKind.Variable);
    }

    [Fact]
    public void Import_AddsToGlobalScope()
    {
        var tree = Analyze("import { deploy } from \"utils.stash\";");

        Assert.Contains(tree.GlobalScope.Symbols, s =>
            s.Name == "deploy" &&
            s.Kind == SymbolKind.Variable &&
            s.Detail != null && s.Detail.Contains("imported from"));
    }

    [Fact]
    public void GetHierarchicalSymbols_ReturnsFunctionWithParameters()
    {
        var tree = Analyze("fn add(a, b) { return a + b; }");
        var hier = tree.GetHierarchicalSymbols().ToList();

        Assert.Single(hier);
        var (sym, children) = hier[0];
        Assert.Equal("add", sym.Name);
        Assert.Equal(SymbolKind.Function, sym.Kind);
        Assert.Equal(2, children.Count);
        Assert.Contains(children, c => c.Name == "a" && c.Kind == SymbolKind.Parameter);
        Assert.Contains(children, c => c.Name == "b" && c.Kind == SymbolKind.Parameter);
    }

    [Fact]
    public void GetHierarchicalSymbols_ReturnsStructWithFields()
    {
        var tree = Analyze("struct Point { x, y }");
        var hier = tree.GetHierarchicalSymbols().ToList();

        Assert.Single(hier);
        var (sym, children) = hier[0];
        Assert.Equal("Point", sym.Name);
        Assert.Equal(SymbolKind.Struct, sym.Kind);
        Assert.Equal(2, children.Count);
        Assert.Contains(children, c => c.Name == "x" && c.Kind == SymbolKind.Field);
        Assert.Contains(children, c => c.Name == "y" && c.Kind == SymbolKind.Field);
    }

    [Fact]
    public void GetHierarchicalSymbols_ReturnsEnumWithMembers()
    {
        var tree = Analyze("enum Color { Red, Green, Blue }");
        var hier = tree.GetHierarchicalSymbols().ToList();

        Assert.Single(hier);
        var (sym, children) = hier[0];
        Assert.Equal("Color", sym.Name);
        Assert.Equal(SymbolKind.Enum, sym.Kind);
        Assert.Equal(3, children.Count);
        Assert.Contains(children, c => c.Name == "Red" && c.Kind == SymbolKind.EnumMember);
        Assert.Contains(children, c => c.Name == "Green" && c.Kind == SymbolKind.EnumMember);
        Assert.Contains(children, c => c.Name == "Blue" && c.Kind == SymbolKind.EnumMember);
    }

    [Fact]
    public void GetHierarchicalSymbols_DoesNotMixFieldsAcrossStructs()
    {
        var tree = Analyze(@"
struct Point { x, y }
struct Size { width, height }
");
        var hier = tree.GetHierarchicalSymbols().ToList();

        Assert.Equal(2, hier.Count);

        var point = hier.First(h => h.Symbol.Name == "Point");
        Assert.Equal(2, point.Children.Count);
        Assert.Contains(point.Children, c => c.Name == "x");
        Assert.Contains(point.Children, c => c.Name == "y");

        var size = hier.First(h => h.Symbol.Name == "Size");
        Assert.Equal(2, size.Children.Count);
        Assert.Contains(size.Children, c => c.Name == "width");
        Assert.Contains(size.Children, c => c.Name == "height");
    }

    [Fact]
    public void EmptyFile_ProducesEmptyGlobalScope()
    {
        var tree = Analyze("");
        Assert.Empty(tree.GlobalScope.Symbols);
        Assert.Empty(tree.GlobalScope.Children);
        Assert.Empty(tree.GetTopLevel());
    }

    [Fact]
    public void DeeplyNestedScopes_ResolveCorrectly()
    {
        var tree = Analyze(@"
let a = 1;
fn outer() {
    let b = 2;
    fn middle() {
        let c = 3;
        fn inner() {
            let d = 4;
        }
    }
}
");
        // d should be visible inside inner, along with c, b, a, and all function names
        // inner function body is deeply nested; find a position inside it
        // Line numbers (1-based): line 8 has "let d = 4;" — d token at col 17
        var visible = tree.GetVisibleSymbols(8, 20).ToList();
        Assert.Contains(visible, s => s.Name == "d");
        Assert.Contains(visible, s => s.Name == "c");
        Assert.Contains(visible, s => s.Name == "b");
        Assert.Contains(visible, s => s.Name == "a");
        Assert.Contains(visible, s => s.Name == "inner");
        Assert.Contains(visible, s => s.Name == "middle");
        Assert.Contains(visible, s => s.Name == "outer");
    }

    [Fact]
    public void ShadowedVariable_InnerScopeWins()
    {
        var tree = Analyze(@"
let x = 1;
fn foo() {
    let x = 2;
}
");
        // Inside foo body (line 4), FindDefinition should find the inner x
        // "    let x = 2;" — x token starts at col 9; query after it
        var def = tree.FindDefinition("x", 4, 15);
        Assert.NotNull(def);
        Assert.Equal(SymbolKind.Variable, def!.Kind);
        // The inner x is on line 4; the outer is on line 2
        Assert.Equal(4, def.Span.StartLine);
    }

    [Fact]
    public void ShadowedVariable_OuterScopeVisibleOutside()
    {
        var tree = Analyze(@"
let x = 1;
fn foo() {
    let x = 2;
}
let y = x;
");
        // At line 6 (outside foo), FindDefinition should find the outer x
        var def = tree.FindDefinition("x", 6, 9);
        Assert.NotNull(def);
        Assert.Equal(2, def!.Span.StartLine);
    }

    [Fact]
    public void GetVisibleSymbols_AtGlobalScope_ExcludesInnerScopeVariables()
    {
        var tree = Analyze(@"
let a = 1;
fn foo() {
    let inner = 2;
}
let b = 3;
");
        // At line 6, outside foo, inner should NOT be visible
        var visible = tree.GetVisibleSymbols(6, 5).ToList();
        Assert.Contains(visible, s => s.Name == "a");
        Assert.Contains(visible, s => s.Name == "foo");
        Assert.Contains(visible, s => s.Name == "b");
        Assert.DoesNotContain(visible, s => s.Name == "inner");
    }

    [Fact]
    public void FindDefinition_FunctionDeclaredAfterUsage_NotFound()
    {
        var tree = Analyze(@"
let x = foo();
fn foo() { return 1; }
");
        // At line 2, foo is used but declared on line 3 — should NOT be found
        // since IsBeforeOrAt checks position
        var def = tree.FindDefinition("foo", 2, 9);
        Assert.Null(def);
    }

    [Fact]
    public void BuiltIns_CommandResultStruct_HasTypedFields()
    {
        var tree = Analyze("", includeBuiltIns: true);
        var syms = tree.GlobalScope.Symbols;

        Assert.Contains(syms, s => s.Name == "CommandResult" && s.Kind == SymbolKind.Struct);
        Assert.Contains(syms, s => s.Name == "stdout" && s.Kind == SymbolKind.Field && s.TypeHint == "string");
        Assert.Contains(syms, s => s.Name == "stderr" && s.Kind == SymbolKind.Field && s.TypeHint == "string");
        Assert.Contains(syms, s => s.Name == "exitCode" && s.Kind == SymbolKind.Field && s.TypeHint == "int");
    }

    [Fact]
    public void BuiltIns_GlobalFunctions_HaveReturnTypes()
    {
        var tree = Analyze("", includeBuiltIns: true);
        var syms = tree.GlobalScope.Symbols;

        var typeofSym = syms.First(s => s.Name == "typeof" && s.Kind == SymbolKind.Function);
        Assert.Equal("string", typeofSym.TypeHint);

        var lenSym = syms.First(s => s.Name == "len" && s.Kind == SymbolKind.Function);
        Assert.Equal("int", lenSym.TypeHint);
    }

    [Fact]
    public void BuiltIns_Namespaces_RegisteredInGlobalScope()
    {
        var tree = Analyze("", includeBuiltIns: true);
        var syms = tree.GlobalScope.Symbols;

        Assert.Contains(syms, s => s.Name == "io" && s.Kind == SymbolKind.Namespace);
        Assert.Contains(syms, s => s.Name == "fs" && s.Kind == SymbolKind.Namespace);
        Assert.Contains(syms, s => s.Name == "env" && s.Kind == SymbolKind.Namespace);
        Assert.Contains(syms, s => s.Name == "conv" && s.Kind == SymbolKind.Namespace);
        Assert.Contains(syms, s => s.Name == "process" && s.Kind == SymbolKind.Namespace);
        Assert.Contains(syms, s => s.Name == "path" && s.Kind == SymbolKind.Namespace);
    }

    [Fact]
    public void BuiltIns_VisibleAlongsideUserCode()
    {
        var tree = Analyze("let x = 1;", includeBuiltIns: true);
        var visible = tree.GetVisibleSymbols(1, 15).ToList();

        Assert.Contains(visible, s => s.Name == "x");
        Assert.Contains(visible, s => s.Name == "typeof");
        Assert.Contains(visible, s => s.Name == "CommandResult");
    }

    [Fact]
    public void FunctionScope_WithDefaultParams_ContainsParameters()
    {
        var tree = Analyze("fn greet(name, greeting = \"Hello\") { return greeting; }");

        var fnSymbol = tree.GlobalScope.Symbols.First(s => s.Name == "greet");
        Assert.Equal(SymbolKind.Function, fnSymbol.Kind);
        Assert.Contains("greeting = \"Hello\"", fnSymbol.Detail);

        var fnScope = tree.GlobalScope.Children[0];
        Assert.Equal(ScopeKind.Function, fnScope.Kind);
        Assert.Equal(2, fnScope.Symbols.Count);
        Assert.Contains(fnScope.Symbols, s => s.Name == "name" && s.Kind == SymbolKind.Parameter);
        Assert.Contains(fnScope.Symbols, s => s.Name == "greeting" && s.Kind == SymbolKind.Parameter);
    }

    [Fact]
    public void Interface_RegistersInterfaceSymbol()
    {
        var tree = Analyze("interface Printable { toString() }");
        Assert.Contains(tree.GlobalScope.Symbols, s => s.Name == "Printable" && s.Kind == SymbolKind.Interface);
    }

    [Fact]
    public void Interface_RegistersMethodSymbol()
    {
        var tree = Analyze("interface Printable { toString() }");
        Assert.Contains(tree.GlobalScope.Symbols, s => s.Name == "toString" && s.Kind == SymbolKind.Method && s.ParentName == "Printable");
    }

    [Fact]
    public void Interface_RegistersFieldSymbol()
    {
        var tree = Analyze("interface HasName { name }");
        Assert.Contains(tree.GlobalScope.Symbols, s => s.Name == "name" && s.Kind == SymbolKind.Field && s.ParentName == "HasName");
    }

    [Fact]
    public void Interface_FieldWithTypeHint_SetsTypeHint()
    {
        var tree = Analyze("interface HasName { name: string }");
        var field = tree.GlobalScope.Symbols.First(s => s.Name == "name" && s.Kind == SymbolKind.Field);
        Assert.Equal("string", field.TypeHint);
    }

    [Fact]
    public void Interface_MethodWithParams_SetsParameterInfo()
    {
        var tree = Analyze("interface Calc { add(a, b) }");
        var method = tree.GlobalScope.Symbols.First(s => s.Name == "add" && s.Kind == SymbolKind.Method);
        Assert.Equal("Calc", method.ParentName);
        Assert.NotNull(method.ParameterNames);
        Assert.Equal(new[] { "a", "b" }, method.ParameterNames);
        Assert.Equal(2, method.RequiredParameterCount);
    }

    [Fact]
    public void Interface_MethodWithReturnType_SetsTypeHint()
    {
        var tree = Analyze("interface Printable { toString() -> string }");
        var method = tree.GlobalScope.Symbols.First(s => s.Name == "toString" && s.Kind == SymbolKind.Method);
        Assert.Equal("string", method.TypeHint);
    }

    [Fact]
    public void Interface_DetailIncludesAllMembers()
    {
        var tree = Analyze("interface Shape { area() -> float, name: string }");
        var iface = tree.GlobalScope.Symbols.First(s => s.Name == "Shape" && s.Kind == SymbolKind.Interface);
        Assert.NotNull(iface.Detail);
        Assert.Contains("area", iface.Detail);
        Assert.Contains("name", iface.Detail);
    }

    [Fact]
    public void Interface_MethodWithTypedParams_SetsParameterTypes()
    {
        var tree = Analyze("interface Calc { add(a: int, b: string) -> float }");
        var method = tree.GlobalScope.Symbols.First(s => s.Name == "add" && s.Kind == SymbolKind.Method);
        Assert.NotNull(method.ParameterTypes);
        Assert.Equal(2, method.ParameterTypes.Length);
        Assert.Equal("int", method.ParameterTypes[0]);
        Assert.Equal("string", method.ParameterTypes[1]);
    }
}
