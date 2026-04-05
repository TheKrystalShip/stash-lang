using Stash.Lexing;
using Stash.Parsing;
using Stash.Bytecode;
using Stash.Resolution;
using Stash.Runtime;

namespace Stash.Tests.Interpreting;

public class StrBuiltInsTests
{
    private static object? Run(string source)
    {
        string full = source + "\nreturn result;";
        var lexer = new Lexer(full, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(TestVM.CreateGlobals());
        return vm.Execute(chunk);
    }

    private static void RunExpectingError(string source)
    {
        var lexer = new Lexer(source, "<test>");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var stmts = parser.ParseProgram();
        SemanticResolver.Resolve(stmts);
        var chunk = Compiler.Compile(stmts);
        var vm = new VirtualMachine(TestVM.CreateGlobals());
        Assert.Throws<RuntimeError>(() => vm.Execute(chunk));
    }

    // ── str.capitalize ────────────────────────────────────────────────────

    [Fact]
    public void Capitalize_BasicString()
    {
        var result = Run("let result = str.capitalize(\"hello world\");");
        Assert.Equal("Hello world", result);
    }

    [Fact]
    public void Capitalize_AlreadyCapitalized()
    {
        var result = Run("let result = str.capitalize(\"HELLO\");");
        Assert.Equal("Hello", result);
    }

    [Fact]
    public void Capitalize_EmptyString()
    {
        var result = Run("let result = str.capitalize(\"\");");
        Assert.Equal("", result);
    }

    [Fact]
    public void Capitalize_SingleChar()
    {
        var result = Run("let result = str.capitalize(\"a\");");
        Assert.Equal("A", result);
    }

    [Fact]
    public void Capitalize_NonStringThrows()
    {
        RunExpectingError("str.capitalize(42);");
    }

    // ── str.title ─────────────────────────────────────────────────────────

    [Fact]
    public void Title_BasicString()
    {
        var result = Run("let result = str.title(\"hello world\");");
        Assert.Equal("Hello World", result);
    }

    [Fact]
    public void Title_MixedCase()
    {
        var result = Run("let result = str.title(\"hELLO wORLD\");");
        Assert.Equal("Hello World", result);
    }

    [Fact]
    public void Title_EmptyString()
    {
        var result = Run("let result = str.title(\"\");");
        Assert.Equal("", result);
    }

    [Fact]
    public void Title_SingleWord()
    {
        var result = Run("let result = str.title(\"hello\");");
        Assert.Equal("Hello", result);
    }

    [Fact]
    public void Title_NonStringThrows()
    {
        RunExpectingError("str.title(42);");
    }

    // ── str.lines ─────────────────────────────────────────────────────────

    [Fact]
    public void Lines_SplitsByNewline()
    {
        var result = Run("let result = str.lines(\"a\\nb\\nc\");");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
        Assert.Equal("a", list[0]);
        Assert.Equal("b", list[1]);
        Assert.Equal("c", list[2]);
    }

    [Fact]
    public void Lines_EmptyString()
    {
        var result = Run("let result = str.lines(\"\");");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Single(list);
        Assert.Equal("", list[0]);
    }

    [Fact]
    public void Lines_NoNewlines()
    {
        var result = Run("let result = str.lines(\"hello\");");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Single(list);
        Assert.Equal("hello", list[0]);
    }

    [Fact]
    public void Lines_NonStringThrows()
    {
        RunExpectingError("str.lines(42);");
    }

    // ── str.words ─────────────────────────────────────────────────────────

    [Fact]
    public void Words_SplitsByWhitespace()
    {
        var result = Run("let result = str.words(\"hello  world  foo\");");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
        Assert.Equal("hello", list[0]);
        Assert.Equal("world", list[1]);
        Assert.Equal("foo", list[2]);
    }

    [Fact]
    public void Words_EmptyString()
    {
        var result = Run("let result = str.words(\"\");");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Empty(list);
    }

    [Fact]
    public void Words_TabsAndSpaces()
    {
        var result = Run("let result = str.words(\"a\\tb\\tc\");");
        var list = Assert.IsType<List<object?>>(result);
        Assert.Equal(3, list.Count);
    }

    [Fact]
    public void Words_NonStringThrows()
    {
        RunExpectingError("str.words(42);");
    }

    // ── str.truncate ──────────────────────────────────────────────────────

    [Fact]
    public void Truncate_ShorterThanMax()
    {
        var result = Run("let result = str.truncate(\"hello\", 10);");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void Truncate_LongerThanMax()
    {
        var result = Run("let result = str.truncate(\"hello world\", 8);");
        Assert.Equal("hello...", result);
    }

    [Fact]
    public void Truncate_CustomSuffix()
    {
        var result = Run("let result = str.truncate(\"hello world\", 8, \"--\");");
        Assert.Equal("hello --", result);
    }

    [Fact]
    public void Truncate_ExactLength()
    {
        var result = Run("let result = str.truncate(\"hello\", 5);");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void Truncate_NonStringThrows()
    {
        RunExpectingError("str.truncate(42, 5);");
    }

    [Fact]
    public void Truncate_NonIntLenThrows()
    {
        RunExpectingError("str.truncate(\"hello\", \"five\");");
    }

    // ── str.slug ──────────────────────────────────────────────────────────

    [Fact]
    public void Slug_BasicString()
    {
        var result = Run("let result = str.slug(\"Hello World!\");");
        Assert.Equal("hello-world", result);
    }

    [Fact]
    public void Slug_SpecialCharacters()
    {
        var result = Run("let result = str.slug(\"My Article #1: Great!\");");
        Assert.Equal("my-article-1-great", result);
    }

    [Fact]
    public void Slug_MultipleSpaces()
    {
        var result = Run("let result = str.slug(\"  hello   world  \");");
        Assert.Equal("hello-world", result);
    }

    [Fact]
    public void Slug_EmptyString()
    {
        var result = Run("let result = str.slug(\"\");");
        Assert.Equal("", result);
    }

    [Fact]
    public void Slug_NonStringThrows()
    {
        RunExpectingError("str.slug(42);");
    }

    // ── str.wrap ──────────────────────────────────────────────────────────

    [Fact]
    public void Wrap_ShortLine()
    {
        var result = Run("let result = str.wrap(\"hello world\", 20);");
        Assert.Equal("hello world", result);
    }

    [Fact]
    public void Wrap_LongLine()
    {
        var result = Run("let result = str.wrap(\"hello world foo bar\", 10);");
        Assert.Contains("\n", (string)result!);
    }

    [Fact]
    public void Wrap_ZeroWidthThrows()
    {
        RunExpectingError("str.wrap(\"hello\", 0);");
    }

    [Fact]
    public void Wrap_NonStringThrows()
    {
        RunExpectingError("str.wrap(42, 10);");
    }

    [Fact]
    public void Wrap_NonIntWidthThrows()
    {
        RunExpectingError("str.wrap(\"hello\", \"ten\");");
    }
}
