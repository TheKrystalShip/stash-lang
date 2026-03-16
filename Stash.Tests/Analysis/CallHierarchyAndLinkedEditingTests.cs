using System.Linq;
using Stash.Common;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Lsp.Analysis;

namespace Stash.Tests.Analysis;

public class CallHierarchyAndLinkedEditingTests
{
    private static ScopeTree Analyze(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        var collector = new SymbolCollector();
        return collector.Collect(stmts);
    }

    private static bool IsInsideSpan(SourceSpan? span, SourceSpan target)
    {
        if (span == null) return false;
        if (target.StartLine < span.StartLine || target.EndLine > span.EndLine) return false;
        if (target.StartLine == span.StartLine && target.StartColumn < span.StartColumn) return false;
        if (target.EndLine == span.EndLine && target.EndColumn > span.EndColumn) return false;
        return true;
    }

    // -------------------------------------------------------------------------
    // Call Hierarchy Tests
    // -------------------------------------------------------------------------

    // Line 1: fn hello() {
    // Line 2: }
    // "hello" starts at col 4 (after "fn ")
    [Fact]
    public void CallHierarchy_Prepare_FindsFunctionDefinition()
    {
        const string src = "fn hello() {\n}\n";
        var tree = Analyze(src);
        var symbol = tree.FindDefinition("hello", 1, 4);
        Assert.NotNull(symbol);
        Assert.Equal(SymbolKind.Function, symbol.Kind);
        Assert.Equal("hello", symbol.Name);
    }

    // Line 1: let x = 42;
    // "x" at col 5 (after "let ")
    [Fact]
    public void CallHierarchy_Prepare_ReturnsNonFunctionForVariable()
    {
        const string src = "let x = 42;";
        var tree = Analyze(src);
        var symbol = tree.FindDefinition("x", 1, 5);
        Assert.NotNull(symbol);
        Assert.NotEqual(SymbolKind.Function, symbol.Kind);
    }

    // Line 1: fn greet() {
    // Line 2: }
    // Line 3: fn main() {
    // Line 4:   greet();    <- "greet" at col 3
    // Line 5: }
    [Fact]
    public void CallHierarchy_IncomingCalls_FindsCallers()
    {
        const string src = "fn greet() {\n}\nfn main() {\n  greet();\n}\n";
        var tree = Analyze(src);
        var refs = tree.References.Where(r => r.Kind == ReferenceKind.Call && r.Name == "greet").ToList();
        Assert.Single(refs);
        Assert.Equal(4, refs[0].Span.StartLine);
    }

    // Line 1: fn target() {
    // Line 2: }
    // Line 3: fn caller1() {
    // Line 4:   target();
    // Line 5: }
    // Line 6: fn caller2() {
    // Line 7:   target();
    // Line 8: }
    [Fact]
    public void CallHierarchy_IncomingCalls_MultipleCallers()
    {
        const string src = "fn target() {\n}\nfn caller1() {\n  target();\n}\nfn caller2() {\n  target();\n}\n";
        var tree = Analyze(src);
        var refs = tree.References.Where(r => r.Kind == ReferenceKind.Call && r.Name == "target").ToList();
        Assert.Equal(2, refs.Count);
    }

    // Line 1: fn a() {
    // Line 2: }
    // Line 3: fn b() {
    // Line 4: }
    // Line 5: fn main() {
    // Line 6:   a();
    // Line 7:   b();
    // Line 8: }
    [Fact]
    public void CallHierarchy_OutgoingCalls_FindsCallees()
    {
        const string src = "fn a() {\n}\nfn b() {\n}\nfn main() {\n  a();\n  b();\n}\n";
        var tree = Analyze(src);

        var mainSymbol = tree.All.FirstOrDefault(s => s.Kind == SymbolKind.Function && s.Name == "main");
        Assert.NotNull(mainSymbol);
        Assert.NotNull(mainSymbol.FullSpan);

        var callsInMain = tree.References
            .Where(r => r.Kind == ReferenceKind.Call && IsInsideSpan(mainSymbol.FullSpan, r.Span))
            .ToList();
        Assert.Equal(2, callsInMain.Count);
        Assert.Contains(callsInMain, r => r.Name == "a");
        Assert.Contains(callsInMain, r => r.Name == "b");
    }

    // Line 1: fn empty() {
    // Line 2: }
    [Fact]
    public void CallHierarchy_OutgoingCalls_EmptyFunction()
    {
        const string src = "fn empty() {\n}\n";
        var tree = Analyze(src);
        var calls = tree.References.Where(r => r.Kind == ReferenceKind.Call && r.Name != "empty").ToList();
        Assert.Empty(calls);
    }

    // Line 1: fn fib(n) {
    // Line 2:   if (n <= 1) { return n; }
    // Line 3:   return fib(n - 1) + fib(n - 2);
    // Line 4: }
    // Two recursive calls to "fib" on line 3
    [Fact]
    public void CallHierarchy_RecursiveFunction()
    {
        const string src = "fn fib(n) {\n  if (n <= 1) { return n; }\n  return fib(n - 1) + fib(n - 2);\n}\n";
        var tree = Analyze(src);
        var fibCalls = tree.References.Where(r => r.Kind == ReferenceKind.Call && r.Name == "fib").ToList();
        Assert.Equal(2, fibCalls.Count);
    }

    // Line 1: fn inner() {
    // Line 2: }
    // Line 3: fn outer() {
    // Line 4:   inner();
    // Line 5: }
    // Line 6: fn main() {
    // Line 7:   outer();
    // Line 8: }
    [Fact]
    public void CallHierarchy_NestedCalls()
    {
        const string src = "fn inner() {\n}\nfn outer() {\n  inner();\n}\nfn main() {\n  outer();\n}\n";
        var tree = Analyze(src);

        var innerCalls = tree.References.Where(r => r.Kind == ReferenceKind.Call && r.Name == "inner").ToList();
        Assert.Single(innerCalls);

        var outerCalls = tree.References.Where(r => r.Kind == ReferenceKind.Call && r.Name == "outer").ToList();
        Assert.Single(outerCalls);
    }

    // Line 1: fn hello() {
    // Line 2: }
    // Line 3: hello();
    // "hello" definition at (1, 4); call at (3, 1)
    [Fact]
    public void CallHierarchy_CallReference_ResolvesToFunction()
    {
        const string src = "fn hello() {\n}\nhello();\n";
        var tree = Analyze(src);
        var callRef = tree.References.First(r => r.Kind == ReferenceKind.Call && r.Name == "hello");
        Assert.NotNull(callRef.ResolvedSymbol);
        Assert.Equal(SymbolKind.Function, callRef.ResolvedSymbol!.Kind);
        Assert.Equal("hello", callRef.ResolvedSymbol!.Name);
    }

    // Line 1: fn target() {
    // Line 2: }
    // Line 3: fn caller() {
    // Line 4:   target();
    // Line 5:   target();
    // Line 6:   target();
    // Line 7: }
    [Fact]
    public void CallHierarchy_MultipleCallsToSameFunction()
    {
        const string src = "fn target() {\n}\nfn caller() {\n  target();\n  target();\n  target();\n}\n";
        var tree = Analyze(src);
        var calls = tree.References.Where(r => r.Kind == ReferenceKind.Call && r.Name == "target").ToList();
        Assert.Equal(3, calls.Count);
    }

    // -------------------------------------------------------------------------
    // Linked Editing Range Tests
    // -------------------------------------------------------------------------

    // Line 1: let x = 1;       <- "x" declared at col 5
    // Line 2: let y = x + 2;   <- "x" read at col 9
    // Line 3: x = 5;           <- "x" written at col 1
    [Fact]
    public void LinkedEditing_Variable_MultipleReferences()
    {
        const string src = "let x = 1;\nlet y = x + 2;\nx = 5;";
        var tree = Analyze(src);
        var refs = tree.FindReferences("x", 1, 5);
        Assert.True(refs.Count >= 2);
    }

    // Line 1: fn greet(name) {
    // Line 2:   println(name);
    // Line 3: }
    // Line 4: greet("world");
    // "greet" defined at (1, 4); called at (4, 1)
    [Fact]
    public void LinkedEditing_Function_DefinitionAndCalls()
    {
        const string src = "fn greet(name) {\n  println(name);\n}\ngreet(\"world\");";
        var tree = Analyze(src);
        var refs = tree.FindReferences("greet", 1, 4);
        Assert.Equal(2, refs.Count);
    }

    // Line 1: let unused = 42;
    // "unused" at col 5, only the declaration — no other usage
    [Fact]
    public void LinkedEditing_SingleReference_NoLinks()
    {
        const string src = "let unused = 42;";
        var tree = Analyze(src);
        var refs = tree.FindReferences("unused", 1, 5);
        Assert.Single(refs);
    }

    // Line 1: let x = 1;          <- outer x at col 5
    // Line 2: fn test() {
    // Line 3:     let x = 2;      <- inner x at col 9
    // Line 4:     x;              <- refers to inner x
    // Line 5: }
    // Line 6: x;                  <- refers to outer x
    [Fact]
    public void LinkedEditing_RespectsScope()
    {
        const string src = "let x = 1;\nfn test() {\n    let x = 2;\n    x;\n}\nx;";
        var tree = Analyze(src);

        // Outer x (line 1, col 5): should include line 1 decl + line 6 use, but NOT inner x lines
        var outerRefs = tree.FindReferences("x", 1, 5);
        Assert.DoesNotContain(outerRefs, r => r.Span.StartLine == 3);
        Assert.DoesNotContain(outerRefs, r => r.Span.StartLine == 4);

        // Inner x (line 3, col 9): should include line 3 decl + line 4 use, but NOT outer x lines
        var innerRefs = tree.FindReferences("x", 3, 9);
        Assert.DoesNotContain(innerRefs, r => r.Span.StartLine == 1);
        Assert.DoesNotContain(innerRefs, r => r.Span.StartLine == 6);
    }

    // Line 1: fn greet(name) {
    // Line 2:   println(name);   <- "name" read at col 11
    // Line 3:   name;            <- "name" read at col 3
    // Line 4: }
    // Use position inside function body to locate the parameter declaration
    [Fact]
    public void LinkedEditing_Parameter_References()
    {
        const string src = "fn greet(name) {\n  println(name);\n  name;\n}";
        var tree = Analyze(src);
        // Position (2, 11): inside body, "name" argument to println
        var refs = tree.FindReferences("name", 2, 11);
        Assert.True(refs.Count >= 3);
    }

    // Line 1: const PI = 3.14;      <- "PI" declared at col 7
    // Line 2: let area = PI * 2;    <- "PI" read at col 12
    [Fact]
    public void LinkedEditing_Constant_References()
    {
        const string src = "const PI = 3.14;\nlet area = PI * 2;";
        var tree = Analyze(src);
        var refs = tree.FindReferences("PI", 1, 7);
        Assert.Equal(2, refs.Count);
    }

    // Line 1: let items = [1, 2, 3];
    // Line 2: for (let item in items) {     <- "item" loop var at col 10
    // Line 3:   println(item);              <- "item" read at col 11
    // Line 4: }
    // Use position inside loop body to locate the loop variable
    [Fact]
    public void LinkedEditing_LoopVariable()
    {
        const string src = "let items = [1, 2, 3];\nfor (let item in items) {\n  println(item);\n}";
        var tree = Analyze(src);
        // Position (3, 11): inside loop body, "item" argument to println
        var refs = tree.FindReferences("item", 3, 11);
        Assert.True(refs.Count >= 2);
    }
}
