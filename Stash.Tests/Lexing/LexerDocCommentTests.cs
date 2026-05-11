namespace Stash.Tests.Lexing;

using Stash.Lexing;

/// <summary>
/// Tests that the lexer attaches <c>///</c> doc-comment blocks to the following real token
/// via <see cref="Token.LeadingDoc"/>.
/// </summary>
public class LexerDocCommentTests
{
    private static List<Token> Scan(string source)
    {
        var lexer = new Lexer(source);
        return lexer.ScanTokens();
    }

    [Fact]
    public void SingleDocLine_AttachedToNextToken()
    {
        const string source = "/// Does something.\nfn foo() {}";
        var tokens = Scan(source);

        // Find the 'fn' token
        var fn = tokens.First(t => t.Type == TokenType.Fn);
        Assert.NotNull(fn.LeadingDoc);
        Assert.Equal("Does something.", fn.LeadingDoc);
    }

    [Fact]
    public void MultipleDocLines_JoinedWithNewline()
    {
        const string source = "/// Line one.\n/// Line two.\nfn foo() {}";
        var tokens = Scan(source);

        var fn = tokens.First(t => t.Type == TokenType.Fn);
        Assert.NotNull(fn.LeadingDoc);
        Assert.Equal("Line one.\nLine two.", fn.LeadingDoc);
    }

    [Fact]
    public void DocComment_LeadingSpaceStripped()
    {
        // One optional leading space after /// is stripped.
        const string source = "///  Two leading spaces.\nfn foo() {}";
        var tokens = Scan(source);

        var fn = tokens.First(t => t.Type == TokenType.Fn);
        Assert.NotNull(fn.LeadingDoc);
        Assert.Equal(" Two leading spaces.", fn.LeadingDoc);
    }

    [Fact]
    public void RegularLineComment_NotAttachedAsDoc()
    {
        const string source = "// regular comment\nfn foo() {}";
        var tokens = Scan(source);

        var fn = tokens.First(t => t.Type == TokenType.Fn);
        Assert.Null(fn.LeadingDoc);
    }

    [Fact]
    public void DocCommentWithInterveningCode_ResetsBetweenFunctions()
    {
        const string source = "/// First fn.\nfn a() {}\n/// Second fn.\nfn b() {}";
        var tokens = Scan(source);

        var fnTokens = tokens.Where(t => t.Type == TokenType.Fn).ToList();
        Assert.Equal(2, fnTokens.Count);
        Assert.Equal("First fn.", fnTokens[0].LeadingDoc);
        Assert.Equal("Second fn.", fnTokens[1].LeadingDoc);
    }

    [Fact]
    public void NonDocFirstToken_AfterDocComment_AttachedCorrectly()
    {
        // Even a non-fn keyword immediately follows the doc comment
        const string source = "/// Constant doc.\nconst X = 1;";
        var tokens = Scan(source);

        var constTok = tokens.First(t => t.Type == TokenType.Const);
        Assert.NotNull(constTok.LeadingDoc);
        Assert.Equal("Constant doc.", constTok.LeadingDoc);
    }

    [Fact]
    public void DocBuffer_ClearedAfterAttachment()
    {
        // If doc buffer is attached to `const`, the next `fn` should have no doc.
        const string source = "/// Constant.\nconst X = 1;\nfn bar() {}";
        var tokens = Scan(source);

        var fn = tokens.First(t => t.Type == TokenType.Fn);
        Assert.Null(fn.LeadingDoc);
    }

    [Fact]
    public void EmptyDocLine_PreservedAsBlankInBlock()
    {
        const string source = "/// First.\n///\n/// Third.\nfn foo() {}";
        var tokens = Scan(source);

        var fn = tokens.First(t => t.Type == TokenType.Fn);
        Assert.NotNull(fn.LeadingDoc);
        // Middle empty line becomes an empty string segment
        Assert.Equal("First.\n\nThird.", fn.LeadingDoc);
    }
}
