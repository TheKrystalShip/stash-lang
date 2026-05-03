using System.Collections.Generic;
using Stash.Bytecode;
using Stash.Lexing;
using Stash.Parsing;
using Stash.Parsing.AST;
using Stash.Resolution;
using Xunit;

namespace Stash.Tests.Bytecode;

/// <summary>
/// Differential tests: verify that LVN-enabled compilation produces identical execution
/// results to LVN-disabled compilation — spec §13.1 risk mitigation (§10.7).
/// </summary>
public class LvnDifferentialTests : BytecodeTestBase
{
    private static (object? withPipeline, object? withoutPipeline) RunDifferential(string source)
    {
        static Chunk Compile(string src, bool enablePipeline)
        {
            var lexer = new Lexer(src, "<test>");
            List<Token> tokens = lexer.ScanTokens();
            List<Stmt> stmts = new Parser(tokens).ParseProgram();
            SemanticResolver.Resolve(stmts);
            return Compiler.Compile(stmts,
                enableDce: true, enableOptimizationPipeline: enablePipeline);
        }

        Chunk with    = Compile(source, enablePipeline: true);
        Chunk without = Compile(source, enablePipeline: false);
        object? resultWithout = new VirtualMachine().Execute(without);
        object? resultWith    = new VirtualMachine().Execute(with);
        return (resultWith, resultWithout);
    }

    [Fact]
    public void Differential_BasicArithmetic_IdenticalResult()
    {
        var (a, b) = RunDifferential("return 5 + 3 + 5 + 3;");
        Assert.Equal(16L, a);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Differential_ConstGlobal_IdenticalResult()
    {
        var (a, b) = RunDifferential("const G = 100; return G + G + G;");
        Assert.Equal(300L, a);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Differential_Fibonacci_IdenticalResult()
    {
        const string fib = """
            fn fib(n) {
                if (n <= 1) return n;
                return fib(n - 1) + fib(n - 2);
            }
            return fib(10);
            """;
        var (a, b) = RunDifferential(fib);
        Assert.Equal(55L, a);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Differential_StructFieldAccess_IdenticalResult()
    {
        const string src = """
            struct Point { x, y }
            let p = Point { x: 3, y: 4 };
            let dist = p.x * p.x + p.y * p.y;
            return dist;
            """;
        var (a, b) = RunDifferential(src);
        Assert.Equal(25L, a);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Differential_NestedFunctions_IdenticalResult()
    {
        const string src = """
            fn outer(x) {
                fn inner(y) { return x + y; }
                return inner(10) + inner(20);
            }
            return outer(5);
            """;
        var (a, b) = RunDifferential(src);
        Assert.Equal(40L, a);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Differential_Loop_IdenticalResult()
    {
        const string src = """
            let sum = 0;
            let i = 0;
            while (i < 100) {
                sum = sum + i;
                i = i + 1;
            }
            return sum;
            """;
        var (a, b) = RunDifferential(src);
        Assert.Equal(4950L, a);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Differential_HigherOrderFunctions_IdenticalResult()
    {
        const string src = """
            fn apply(f, n) { return f(n); }
            fn double(x) { return x * 2; }
            fn triple(x) { return x * 3; }
            return apply(double, 10) + apply(triple, 5);
            """;
        var (a, b) = RunDifferential(src);
        Assert.Equal(35L, a);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Differential_TryCatch_IdenticalResult()
    {
        const string src = """
            fn safeDivide(a, b) {
                try {
                    return a / b;
                } catch (e) {
                    return -1;
                }
            }
            return safeDivide(10, 2) + safeDivide(6, 3);
            """;
        var (a, b) = RunDifferential(src);
        Assert.Equal(7L, a);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Differential_EnumAndMatch_IdenticalResult()
    {
        const string src = """
            enum Color { Red, Green, Blue }
            let c = Color.Green;
            let x = 0;
            if (c == Color.Red)   { x = 1; }
            if (c == Color.Green) { x = 2; }
            if (c == Color.Blue)  { x = 3; }
            return x;
            """;
        var (a, b) = RunDifferential(src);
        Assert.Equal(2L, a);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Differential_ArithmeticChain_IdenticalResult()
    {
        const string src = """
            let a = 2;
            let b = 3;
            let c = a + b;
            let d = a + b;
            let e = c * d;
            let f = e - a;
            return f;
            """;
        var (a, b) = RunDifferential(src);
        Assert.Equal(23L, a);  // c=5, d=5, e=25, f=23
        Assert.Equal(a, b);
    }

    [Fact]
    public void Differential_Recursion_IdenticalResult()
    {
        const string src = """
            fn factorial(n) {
                if (n <= 1) return 1;
                return n * factorial(n - 1);
            }
            return factorial(10);
            """;
        var (a, b) = RunDifferential(src);
        Assert.Equal(3628800L, a);
        Assert.Equal(a, b);
    }
}
