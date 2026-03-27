using Stash.Lexing;
using Stash.Parsing;
using Stash.Analysis;
using Stash.Lsp.Analysis;
using Xunit;

namespace Stash.Tests.Analysis;

public class StructMethodAnalysisTests
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

    private static List<SemanticDiagnostic> Validate(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        var collector = new SymbolCollector();
        var scopeTree = collector.Collect(stmts);
        var validator = new SemanticValidator(scopeTree);
        return validator.Validate(stmts);
    }

    [Fact]
    public void Method_AppearsAsMethodSymbol()
    {
        var tree = Analyze("""
            struct Counter {
                count
                fn increment() {
                    self.count = self.count + 1;
                }
            }
            """);

        var methods = tree.All.Where(s => s.Kind == SymbolKind.Method).ToList();
        Assert.Single(methods);
        Assert.Equal("increment", methods[0].Name);
        Assert.Equal("Counter", methods[0].ParentName);
    }

    [Fact]
    public void Method_HasCorrectParameterNames()
    {
        var tree = Analyze("""
            struct Point {
                x, y
                fn add(other) {
                    return Point { x: self.x + other.x, y: self.y + other.y };
                }
            }
            """);

        var method = tree.All.Single(s => s.Kind == SymbolKind.Method && s.Name == "add");
        Assert.NotNull(method.ParameterNames);
        Assert.Equal(["other"], method.ParameterNames);
        Assert.Equal(1, method.RequiredParameterCount);
    }

    [Fact]
    public void Method_SelfIsParameterInMethodScope()
    {
        var tree = Analyze("""
            struct Box {
                value
                fn get() {
                    return self.value;
                }
            }
            """);

        // self lives in the method's child scope, visible at line 4 (inside the body)
        var visible = tree.GetVisibleSymbols(4, 20).ToList();
        var self = visible.SingleOrDefault(s => s.Name == "self");
        Assert.NotNull(self);
        Assert.Equal(SymbolKind.Parameter, self.Kind);
        Assert.Equal("Box", self.TypeHint);
    }

    [Fact]
    public void Method_MultipleMethodsTracked()
    {
        var tree = Analyze("""
            struct Stack {
                items
                fn push(item) {
                    arr::push(self.items, item);
                }
                fn pop() {
                    return arr::pop(self.items);
                }
            }
            """);

        var methods = tree.All.Where(s => s.Kind == SymbolKind.Method).ToList();
        Assert.Equal(2, methods.Count);
        Assert.Contains(methods, m => m.Name == "push" && m.ParentName == "Stack");
        Assert.Contains(methods, m => m.Name == "pop" && m.ParentName == "Stack");
    }

    [Fact]
    public void Method_AppearsInHierarchicalSymbols()
    {
        var tree = Analyze("""
            struct Shape {
                color
                fn describe() {
                    return self.color;
                }
            }
            """);

        var hierarchical = tree.GetHierarchicalSymbols().ToList();
        var structEntry = hierarchical.SingleOrDefault(h => h.Symbol.Name == "Shape");
        Assert.NotNull(structEntry.Symbol);

        var childNames = structEntry.Children.Select(c => c.Name).ToList();
        Assert.Contains("color", childNames);
        Assert.Contains("describe", childNames);

        var methodChild = structEntry.Children.Single(c => c.Name == "describe");
        Assert.Equal(SymbolKind.Method, methodChild.Kind);
    }

    [Fact]
    public void Method_FieldsAndMethodsBothPresent()
    {
        var tree = Analyze("""
            struct Person {
                name, age
                fn greet() {
                    return self.name;
                }
            }
            """);

        var all = tree.All.ToList();

        var fields = all.Where(s => s.Kind == SymbolKind.Field && s.ParentName == "Person").ToList();
        Assert.Equal(2, fields.Count);
        Assert.Contains(fields, f => f.Name == "name");
        Assert.Contains(fields, f => f.Name == "age");

        var methods = all.Where(s => s.Kind == SymbolKind.Method && s.ParentName == "Person").ToList();
        Assert.Single(methods);
        Assert.Equal("greet", methods[0].Name);
    }

    [Fact]
    public void Method_WithTypeHints_NoWarnings()
    {
        var diagnostics = Validate("""
            struct Calc {
                value
                fn add(n: int) -> int {
                    return self.value + n;
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Level == DiagnosticLevel.Error);
        Assert.DoesNotContain(diagnostics, d => d.Message.Contains("Unknown type") && d.Level == DiagnosticLevel.Warning);
    }

    [Fact]
    public void Method_UnreachableCodeInMethodBody()
    {
        var diagnostics = Validate("""
            struct Counter {
                count
                fn getCount() {
                    return self.count;
                    let unused = 2;
                }
            }
            """);

        var unreachable = diagnostics.Where(d => d.Message.Contains("Unreachable code detected.")).ToList();
        Assert.Single(unreachable);
        Assert.Equal(DiagnosticLevel.Information, unreachable[0].Level);
        Assert.True(unreachable[0].IsUnnecessary);
    }

    [Fact]
    public void Method_VisibleSymbolsInsideMethodBody()
    {
        var tree = Analyze("""
            struct Vec {
                x, y
                fn scale(factor) {
                    return Vec { x: self.x * factor, y: self.y * factor };
                }
            }
            """);

        // Line 4 is inside the method body
        var visible = tree.GetVisibleSymbols(4, 20).ToList();
        Assert.Contains(visible, s => s.Name == "self" && s.Kind == SymbolKind.Parameter);
        Assert.Contains(visible, s => s.Name == "factor" && s.Kind == SymbolKind.Parameter);
    }

    [Fact]
    public void MethodOnly_Struct()
    {
        var tree = Analyze("""
            struct Greeter {
                fn hello() {
                    return "hello";
                }
                fn world() {
                    return "world";
                }
            }
            """);

        var structSymbol = tree.All.Single(s => s.Kind == SymbolKind.Struct && s.Name == "Greeter");
        Assert.Equal("Greeter", structSymbol.Name);

        var fields = tree.All.Where(s => s.Kind == SymbolKind.Field && s.ParentName == "Greeter").ToList();
        Assert.Empty(fields);

        var methods = tree.All.Where(s => s.Kind == SymbolKind.Method && s.ParentName == "Greeter").ToList();
        Assert.Equal(2, methods.Count);
        Assert.Contains(methods, m => m.Name == "hello");
        Assert.Contains(methods, m => m.Name == "world");
    }
}
