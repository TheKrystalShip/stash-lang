namespace Stash.Tests.Cli;

using Stash.Cli.Shell;

/// <summary>
/// Unit tests for <see cref="ReplLinePreprocessor.Apply"/>.
/// Verifies that <c>$?</c> is replaced with <c>shell.lastExitCode()</c>
/// in code contexts and left unchanged inside strings and comments.
/// </summary>
public class ReplLinePreprocessorTests
{
    private const string Expanded = "shell.lastExitCode()";

    [Fact]
    public void Apply_BareDollarQuestion_Replaced()
    {
        string result = ReplLinePreprocessor.Apply("$?");
        Assert.Equal(Expanded, result);
    }

    [Fact]
    public void Apply_DollarQuestionInExpression_Replaced()
    {
        string result = ReplLinePreprocessor.Apply("if $? != 0 { 1 }");
        Assert.Equal($"if {Expanded} != 0 {{ 1 }}", result);
    }

    [Fact]
    public void Apply_DollarQuestionInDoubleQuoteString_NotReplaced()
    {
        string input = "\"$?\"";
        string result = ReplLinePreprocessor.Apply(input);
        Assert.Equal(input, result);
    }

    [Fact]
    public void Apply_DollarQuestionInSingleQuoteString_NotReplaced()
    {
        string input = "'$?'";
        string result = ReplLinePreprocessor.Apply(input);
        Assert.Equal(input, result);
    }

    [Fact]
    public void Apply_DollarQuestionInBacktick_NotReplaced()
    {
        string input = "`$?`";
        string result = ReplLinePreprocessor.Apply(input);
        Assert.Equal(input, result);
    }

    [Fact]
    public void Apply_DollarQuestionInLineComment_NotReplaced()
    {
        string input = "// $?";
        string result = ReplLinePreprocessor.Apply(input);
        Assert.Equal(input, result);
    }

    [Fact]
    public void Apply_DollarQuestionInBlockComment_NotReplaced()
    {
        string input = "/* $? */";
        string result = ReplLinePreprocessor.Apply(input);
        Assert.Equal(input, result);
    }

    [Fact]
    public void Apply_DollarQuestionWithEscapedQuoteContext_HandledCorrectly()
    {
        // String ends after the escaped inner quote; $? outside is replaced.
        string input = "\"a\\\"\" + $?";
        string result = ReplLinePreprocessor.Apply(input);
        Assert.Equal($"\"a\\\"\" + {Expanded}", result);
    }

    [Fact]
    public void Apply_NoDollarQuestion_Unchanged()
    {
        string input = "let x = 42";
        string result = ReplLinePreprocessor.Apply(input);
        Assert.Same(input, result); // fast path: same reference
    }

    [Fact]
    public void Apply_DollarSpaceQuestion_Unchanged()
    {
        // '$ ?' with a space between dollar and question mark — must NOT be replaced.
        string input = "ls $ ?";
        string result = ReplLinePreprocessor.Apply(input);
        Assert.Equal(input, result);
    }

    [Fact]
    public void Apply_MultipleDollarQuestion_AllReplaced()
    {
        string result = ReplLinePreprocessor.Apply("$? + $?");
        Assert.Equal($"{Expanded} + {Expanded}", result);
    }

    [Fact]
    public void Apply_DollarDollarQuestion_SecondReplaced()
    {
        // '$$?' — first '$' is lone, second '$?' becomes the call.
        string result = ReplLinePreprocessor.Apply("$$?");
        Assert.Equal($"${Expanded}", result);
    }

    [Fact]
    public void Apply_TemplateInterpolationContext_NotReplaced()
    {
        // "$?" inside a double-quoted string is just two literal chars — not replaced.
        string input = "\"${$?}\"";
        string result = ReplLinePreprocessor.Apply(input);
        Assert.Equal(input, result);
    }

    [Fact]
    public void Apply_DollarQuestionAfterCode_OnlyCodePortionReplaced()
    {
        // Code before and after a string; $? outside string is replaced, inside is not.
        string input = "$? + \"$?\" + $?";
        string result = ReplLinePreprocessor.Apply(input);
        Assert.Equal($"{Expanded} + \"$?\" + {Expanded}", result);
    }

    [Fact]
    public void Apply_DollarQuestionAfterBlockCommentEnd_Replaced()
    {
        // After */ we are back in Code state — $? should be replaced.
        string input = "/* comment */ $?";
        string result = ReplLinePreprocessor.Apply(input);
        Assert.Equal($"/* comment */ {Expanded}", result);
    }
}
