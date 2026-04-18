using Stash.Lexing;
using Stash.Parsing;
using Stash.Analysis;

namespace Stash.Tests.Analysis;

public class DocCommentTests
{
    /// <summary>
    /// Analyzes source with trivia preservation and doc comment resolution,
    /// mirroring the AnalysisEngine pipeline.
    /// </summary>
    private static ScopeTree AnalyzeWithDocs(string source)
    {
        var lexer = new Lexer(source, "<test>", preserveTrivia: true);
        var tokens = lexer.ScanTokens();

        // Filter trivia for parser (same as AnalysisEngine)
        var parserTokens = new List<Token>();
        foreach (var t in tokens)
        {
            if (t.Type is not (TokenType.DocComment or TokenType.SingleLineComment
                or TokenType.BlockComment or TokenType.Shebang))
            {
                parserTokens.Add(t);
            }
        }

        var parser = new Parser(parserTokens);
        var stmts = parser.ParseProgram();
        var collector = new SymbolCollector();
        var tree = collector.Collect(stmts);

        DocCommentResolver.Resolve(tokens, tree);

        return tree;
    }

    private static SymbolInfo? FindSymbol(ScopeTree tree, string name)
    {
        return tree.All.FirstOrDefault(s => s.Name == name && s.Span.StartLine > 0);
    }

    // ── Triple-slash doc comments ────────────────────────────────────────────

    [Fact]
    public void TripleSlash_AttachesToFunction()
    {
        var tree = AnalyzeWithDocs("/// Adds two numbers.\nfn add(a, b) { return a + b; }");
        var sym = FindSymbol(tree, "add");
        Assert.NotNull(sym);
        Assert.NotNull(sym!.Documentation);
        Assert.Contains("Adds two numbers", sym.Documentation);
    }

    [Fact]
    public void TripleSlash_AttachesToStruct()
    {
        var tree = AnalyzeWithDocs("/// A point in 2D space.\nstruct Point { x, y }");
        var sym = FindSymbol(tree, "Point");
        Assert.NotNull(sym);
        Assert.NotNull(sym!.Documentation);
        Assert.Contains("point in 2D space", sym.Documentation);
    }

    [Fact]
    public void TripleSlash_AttachesToVariable()
    {
        var tree = AnalyzeWithDocs("/// The maximum retry count.\nlet maxRetries = 3;");
        var sym = FindSymbol(tree, "maxRetries");
        Assert.NotNull(sym);
        Assert.NotNull(sym!.Documentation);
        Assert.Contains("maximum retry count", sym.Documentation);
    }

    [Fact]
    public void TripleSlash_AttachesToConst()
    {
        var tree = AnalyzeWithDocs("/// Pi constant.\nconst PI = 3.14;");
        var sym = FindSymbol(tree, "PI");
        Assert.NotNull(sym);
        Assert.NotNull(sym!.Documentation);
        Assert.Contains("Pi constant", sym.Documentation);
    }

    [Fact]
    public void TripleSlash_MultipleLines_CombinedIntoDoc()
    {
        var source = "/// First line.\n/// Second line.\n/// Third line.\nfn test() {}";
        var tree = AnalyzeWithDocs(source);
        var sym = FindSymbol(tree, "test");
        Assert.NotNull(sym);
        Assert.NotNull(sym!.Documentation);
        Assert.Contains("First line", sym.Documentation);
        Assert.Contains("Second line", sym.Documentation);
        Assert.Contains("Third line", sym.Documentation);
    }

    // ── Block doc comments ───────────────────────────────────────────────────

    [Fact]
    public void DocBlock_AttachesToFunction()
    {
        var source = "/**\n * Computes factorial.\n * Uses recursion.\n */\nfn fact(n) { return n; }";
        var tree = AnalyzeWithDocs(source);
        var sym = FindSymbol(tree, "fact");
        Assert.NotNull(sym);
        Assert.NotNull(sym!.Documentation);
        Assert.Contains("Computes factorial", sym.Documentation);
        Assert.Contains("Uses recursion", sym.Documentation);
    }

    [Fact]
    public void DocBlock_SingleLine_AttachesToFunction()
    {
        var source = "/** A simple helper. */\nfn helper() {}";
        var tree = AnalyzeWithDocs(source);
        var sym = FindSymbol(tree, "helper");
        Assert.NotNull(sym);
        Assert.NotNull(sym!.Documentation);
        Assert.Contains("simple helper", sym.Documentation);
    }

    // ── @param and @return tags ──────────────────────────────────────────────

    [Fact]
    public void DocComment_WithParamTags()
    {
        var source = "/// Adds two numbers.\n/// @param a First number\n/// @param b Second number\nfn add(a, b) { return a + b; }";
        var tree = AnalyzeWithDocs(source);
        var sym = FindSymbol(tree, "add");
        Assert.NotNull(sym);
        Assert.NotNull(sym!.Documentation);
        Assert.Contains("Adds two numbers", sym.Documentation);
        Assert.Contains("@param a First number", sym.Documentation);
        Assert.Contains("@param b Second number", sym.Documentation);
    }

    [Fact]
    public void DocBlock_WithParamAndReturn()
    {
        var source = "/**\n * Test function\n * @param arg1 The number to test\n * @return If arg1 is greater than 10\n */\nfn test(arg1: int) { return arg1 > 10; }";
        var tree = AnalyzeWithDocs(source);
        var sym = FindSymbol(tree, "test");
        Assert.NotNull(sym);
        Assert.NotNull(sym!.Documentation);
        Assert.Contains("Test function", sym.Documentation);
        Assert.Contains("@param arg1 The number to test", sym.Documentation);
        Assert.Contains("@return If arg1 is greater than 10", sym.Documentation);
    }

    // ── Lambda doc comments ──────────────────────────────────────────────────

    [Fact]
    public void DocComment_AttachesToLambdaVariable()
    {
        var source = "/**\n * Test function\n * @param arg1 The number to test\n * @return If arg1 is greater than 10\n */\nlet test = (arg1: int) => { return arg1 > 10; };";
        var tree = AnalyzeWithDocs(source);
        var sym = FindSymbol(tree, "test");
        Assert.NotNull(sym);
        Assert.NotNull(sym!.Documentation);
        Assert.Contains("Test function", sym.Documentation);
        Assert.Contains("@param arg1", sym.Documentation);
    }

    // ── Non-attachment cases ─────────────────────────────────────────────────

    [Fact]
    public void RegularComment_DoesNotAttach()
    {
        var tree = AnalyzeWithDocs("// Regular comment.\nfn test() {}");
        var sym = FindSymbol(tree, "test");
        Assert.NotNull(sym);
        Assert.Null(sym!.Documentation);
    }

    [Fact]
    public void RegularBlockComment_DoesNotAttach()
    {
        var tree = AnalyzeWithDocs("/* Block comment. */\nfn test() {}");
        var sym = FindSymbol(tree, "test");
        Assert.NotNull(sym);
        Assert.Null(sym!.Documentation);
    }

    [Fact]
    public void DocComment_WithGap_DoesNotAttach()
    {
        // Doc comment separated from declaration by a blank line
        var source = "/// Orphaned doc.\n\nfn test() {}";
        var tree = AnalyzeWithDocs(source);
        var sym = FindSymbol(tree, "test");
        Assert.NotNull(sym);
        // The doc comment is on line 1, function on line 3 — gap > 1
        Assert.Null(sym!.Documentation);
    }

    [Fact]
    public void MultipleDocComments_EachAttachesToOwnSymbol()
    {
        var source = "/// First func.\nfn first() {}\n/// Second func.\nfn second() {}";
        var tree = AnalyzeWithDocs(source);
        var sym1 = FindSymbol(tree, "first");
        var sym2 = FindSymbol(tree, "second");
        Assert.NotNull(sym1);
        Assert.NotNull(sym2);
        Assert.Contains("First func", sym1!.Documentation);
        Assert.Contains("Second func", sym2!.Documentation);
    }

    // ── Enum doc comment ─────────────────────────────────────────────────────

    [Fact]
    public void DocComment_AttachesToEnum()
    {
        var tree = AnalyzeWithDocs("/// Supported colors.\nenum Color { Red, Green, Blue }");
        var sym = FindSymbol(tree, "Color");
        Assert.NotNull(sym);
        Assert.NotNull(sym!.Documentation);
        Assert.Contains("Supported colors", sym.Documentation);
    }
}
